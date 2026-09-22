import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { chooseFeed, readRequiredPackages, selectFeed } from './select-restore-feed.mjs';

const props = `<Project>
  <ItemGroup>
    <PackageVersion Include="Broiler.Native.Windows" Version="0.1.0-preview.5" />
    <PackageVersion Include="Broiler.Native" Version="0.1.0-preview.4" />
    <PackageVersion Include="Some.ThirdParty" Version="1.2.3" />
  </ItemGroup>
</Project>`;
const packages = readRequiredPackages(props);

const env = { GITHUB_REPOSITORY_OWNER: 'owner', GITHUB_ACTOR: 'actor', GITHUB_TOKEN: 'token' };
const nugetFeed = 'https://api.nuget.org/v3';
const githubFeed = 'https://nuget.pkg.github.com/owner';

// versionsByFeed: { feedBase: { lowercased id: versions | 404 | HTTP status } }
function fakeFeeds(versionsByFeed, requests = []) {
  return async (url, options) => {
    requests.push(url);
    for (const [feed, packageVersions] of Object.entries(versionsByFeed)) {
      if (url === `${feed}/index.json`) return Response.json({
        resources: [{ '@type': 'PackageBaseAddress/3.0.0', '@id': `${feed}/flat/` }],
      });
      const match = url.startsWith(`${feed}/flat/`) && /\/flat\/([^/]+)\/index\.json$/.exec(url);
      if (match) {
        if (feed === githubFeed) assert.match(options.headers.authorization, /^Basic /);
        const versions = packageVersions[match[1]] ?? 404;
        return typeof versions === 'number' ? new Response(null, { status: versions }) : Response.json({ versions });
      }
    }
    assert.fail(`Unexpected request ${url}`);
  };
}

const complete = {
  'broiler.native.windows': ['0.1.0-preview.3', '0.1.0-preview.5'],
  'broiler.native': ['0.1.0-preview.4'],
};
const partial = { 'broiler.native.windows': ['0.1.0-preview.3'], 'broiler.native': ['0.1.0-preview.4'] };

test('only pinned Broiler packages are required, with their exact versions', () => {
  assert.deepEqual(packages, [
    { id: 'Broiler.Native.Windows', version: '0.1.0-preview.5' },
    { id: 'Broiler.Native', version: '0.1.0-preview.4' },
  ]);
  assert.throws(() => readRequiredPackages('<PackageVersion Include="Broiler.X" />'));
});

test('the repository pins Broiler packages the selection can check', () => {
  const repository = readRequiredPackages(readFileSync(new URL('../Directory.Packages.props', import.meta.url), 'utf8'));
  assert.ok(repository.length > 0);
  assert.ok(repository.every(({ id }) => id.startsWith('Broiler.')));
});

test('auto chooses NuGet.org when it hosts every required version, without asking GitHub', async () => {
  const requests = [];
  assert.equal(await selectFeed('auto', packages, {}, fakeFeeds({ [nugetFeed]: complete }, requests)), 'nuget');
  assert.ok(requests.every(url => url.startsWith(nugetFeed)));
});

test('auto falls back to GitHub Packages when a required version is only there', async () => {
  const fetchImpl = fakeFeeds({ [nugetFeed]: partial, [githubFeed]: complete });
  assert.equal(await selectFeed('auto', packages, env, fetchImpl), 'github');
  assert.equal(await selectFeed('auto', packages, env, fakeFeeds({ [nugetFeed]: {}, [githubFeed]: complete })), 'github');
});

test('auto fails and names the missing packages when neither feed is complete', async () => {
  await assert.rejects(
    selectFeed('auto', packages, env, fakeFeeds({ [nugetFeed]: partial, [githubFeed]: { 'broiler.native': ['0.1.0-preview.4'] } })),
    /nuget: Broiler\.Native\.Windows 0\.1\.0-preview\.5; github: Broiler\.Native\.Windows 0\.1\.0-preview\.5/);
});

test('a publish destination must host every required version itself', async () => {
  const split = fakeFeeds({ [nugetFeed]: partial, [githubFeed]: complete });
  assert.equal(await selectFeed('github', packages, env, split), 'github');
  await assert.rejects(selectFeed('nuget', packages, env, split), /Missing on nuget: Broiler\.Native\.Windows/);
  await assert.rejects(selectFeed('github', packages, env, fakeFeeds({ [githubFeed]: partial })), /Missing on github/);
  const requests = [];
  assert.equal(await selectFeed('nuget', packages, env, fakeFeeds({ [nugetFeed]: complete }, requests)), 'nuget');
  assert.ok(requests.every(url => url.startsWith(nugetFeed)));
});

test('version matching is exact and case-insensitive', () => {
  const hosted = new Map([['broiler.native.windows', ['0.1.0-PREVIEW.5']], ['broiler.native', ['0.1.0-preview.40']]]);
  assert.throws(() => chooseFeed('nuget', packages, { nuget: hosted }), /Broiler\.Native 0\.1\.0-preview\.4/);
  hosted.set('broiler.native', ['0.1.0-preview.4']);
  assert.equal(chooseFeed('nuget', packages, { nuget: hosted }), 'nuget');
});

test('feed errors, missing GitHub credentials, and unknown feeds stop the run', async () => {
  for (const status of [401, 403, 500]) {
    await assert.rejects(selectFeed('auto', packages, env, fakeFeeds({
      [nugetFeed]: partial, [githubFeed]: { 'broiler.native.windows': status },
    })));
  }
  await assert.rejects(selectFeed('auto', packages, {}, fakeFeeds({ [nugetFeed]: partial })), /GITHUB_TOKEN/);
  await assert.rejects(selectFeed('azure', packages, env, fakeFeeds({})), /Unknown feed 'azure'/);
});
