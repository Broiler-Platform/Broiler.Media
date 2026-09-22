import { appendFileSync, copyFileSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readVersions } from './resolve-preview-version.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const nugetSource = 'https://api.nuget.org/v3/index.json';
export const feeds = ['nuget', 'github'];

// Only Broiler.* packages differ between the feeds; NuGet.config maps them to GitHub
// Packages and every other package comes from NuGet.org in both configurations.
export function readRequiredPackages(packagesProps) {
  const packages = [];
  for (const [element] of packagesProps.matchAll(/<PackageVersion\b[^>]*>/g)) {
    const id = /\bInclude="([^"]+)"/.exec(element)?.[1];
    const version = /\bVersion="([^"]+)"/.exec(element)?.[1];
    if (!id || !version) throw new Error(`Cannot read package reference: ${element}`);
    if (/^Broiler\./i.test(id)) packages.push({ id, version });
  }
  return packages;
}

export function missingPackages(packages, hosted) {
  return packages.filter(({ id, version }) =>
    !(hosted.get(id.toLowerCase()) ?? []).some(value => value.toLowerCase() === version.toLowerCase()));
}

// auto prefers NuGet.org and falls back to GitHub Packages. A publish destination is
// strict: the release must build against the feed its consumers restore from.
export function chooseFeed(requested, packages, hostedByFeed) {
  if (requested !== 'auto' && !feeds.includes(requested)) {
    throw new Error(`Unknown feed '${requested}'; use auto, nuget, or github.`);
  }
  const candidates = requested === 'auto' ? feeds : [requested];
  const missing = {};
  for (const feed of candidates) {
    if (!hostedByFeed[feed]) continue;
    missing[feed] = missingPackages(packages, hostedByFeed[feed]);
    if (!missing[feed].length) return feed;
  }
  const details = Object.entries(missing).map(([feed, list]) =>
    `${feed}: ${list.map(({ id, version }) => `${id} ${version}`).join(', ')}`);
  throw new Error(`No restore feed hosts every required package (${candidates.join(', ')}). Missing on ${details.join('; ')}.`);
}

async function readHosted(source, packages, headers, fetchImpl) {
  const hosted = new Map();
  await Promise.all(packages.map(async ({ id }) =>
    hosted.set(id.toLowerCase(), await readVersions(source, [id], headers, fetchImpl))));
  return hosted;
}

// Feeds are queried lazily in preference order, so a NuGet.org-complete dependency
// set never needs GitHub credentials (fork pull requests, local runs).
export async function selectFeed(requested, packages, env, fetchImpl = fetch) {
  if (requested !== 'auto' && !feeds.includes(requested)) return chooseFeed(requested, packages, {});
  const hostedByFeed = {};
  for (const feed of requested === 'auto' ? feeds : [requested]) {
    if (feed === 'nuget') {
      hostedByFeed.nuget = await readHosted(nugetSource, packages, {}, fetchImpl);
    } else {
      const { GITHUB_REPOSITORY_OWNER: owner = 'Broiler-Platform', GITHUB_ACTOR: actor, GITHUB_TOKEN: token } = env;
      if (!actor || !token) throw new Error('GitHub Packages lookup requires GITHUB_ACTOR and GITHUB_TOKEN.');
      const authorization = `Basic ${Buffer.from(`${actor}:${token}`).toString('base64')}`;
      hostedByFeed.github = await readHosted(`https://nuget.pkg.github.com/${owner}/index.json`, packages, { authorization }, fetchImpl);
    }
    if (!missingPackages(packages, hostedByFeed[feed]).length) break;
  }
  return chooseFeed(requested, packages, hostedByFeed);
}

async function main() {
  const requested = process.env.FEED || 'auto';
  const packages = readRequiredPackages(readFileSync(resolve(root, 'Directory.Packages.props'), 'utf8'));
  const feed = packages.length ? await selectFeed(requested, packages, process.env)
    : requested === 'auto' ? 'nuget' : chooseFeed(requested, packages, {});
  // The repository NuGet.config is the GitHub Packages configuration.
  if (feed === 'nuget') copyFileSync(resolve(root, 'eng/NuGet.nuget-org.config'), resolve(root, 'NuGet.config'));

  const listed = packages.map(({ id, version }) => `${id} ${version}`).join(', ') || 'none';
  console.log(`Restore feed: ${feed} (requested: ${requested}; Broiler packages: ${listed})`);
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `feed=${feed}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(process.env.GITHUB_STEP_SUMMARY,
      `Restore feed: **${feed}** (requested: ${requested})\n\n${packages.map(({ id, version }) => `- ${id} ${version}`).join('\n')}\n`);
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
