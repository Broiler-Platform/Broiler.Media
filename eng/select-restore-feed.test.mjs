import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { chooseFeed, readRequiredPackages, selectFeed } from './select-restore-feed.mjs';

const props = `<Project>
  <ItemGroup>
    <PackageVersion Include="Broiler.Native.Windows" Version="0.1.0-preview.6" />
    <PackageVersion Include="Broiler.Native" Version="0.1.0-preview.6" />
    <PackageVersion Include="Some.ThirdParty" Version="1.2.3" />
  </ItemGroup>
</Project>`;
const packages = readRequiredPackages(props);

const nugetFeed = 'https://api.nuget.org/v3';

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
        const versions = packageVersions[match[1]] ?? 404;
        return typeof versions === 'number' ? new Response(null, { status: versions }) : Response.json({ versions });
      }
    }
    assert.fail(`Unexpected request ${url}`);
  };
}

const complete = {
  'broiler.native.windows': ['0.1.0-preview.5', '0.1.0-preview.6'],
  'broiler.native': ['0.1.0-preview.6'],
};
const partial = { 'broiler.native.windows': ['0.1.0-preview.5'], 'broiler.native': ['0.1.0-preview.6'] };

test('only pinned Broiler packages are required, with their exact versions', () => {
  assert.deepEqual(packages, [
    { id: 'Broiler.Native.Windows', version: '0.1.0-preview.6' },
    { id: 'Broiler.Native', version: '0.1.0-preview.6' },
  ]);
  assert.throws(() => readRequiredPackages('<PackageVersion Include="Broiler.X" />'));
});

test('the repository pins Broiler packages the selection can check', () => {
  const repository = readRequiredPackages(readFileSync(new URL('../Directory.Packages.props', import.meta.url), 'utf8'));
  assert.ok(repository.length > 0);
  assert.ok(repository.every(({ id }) => id.startsWith('Broiler.')));
});

test('auto chooses NuGet.org when it hosts every required version', async () => {
  const requests = [];
  assert.equal(await selectFeed('auto', packages, {}, fakeFeeds({ [nugetFeed]: complete }, requests)), 'nuget');
  assert.ok(requests.every(url => url.startsWith(nugetFeed)));
});

test('auto fails and names the missing packages when NuGet.org is missing any required version', async () => {
  await assert.rejects(
    selectFeed('auto', packages, {}, fakeFeeds({ [nugetFeed]: partial })),
    /NuGet\.org does not host every required package: Broiler\.Native\.Windows 0\.1\.0-preview\.6/);
});

test('explicit nuget feed selection requires every package on NuGet.org', async () => {
  await assert.rejects(selectFeed('nuget', packages, {}, fakeFeeds({ [nugetFeed]: partial })), /NuGet\.org does not host every required package/);
  const requests = [];
  assert.equal(await selectFeed('nuget', packages, {}, fakeFeeds({ [nugetFeed]: complete }, requests)), 'nuget');
  assert.ok(requests.every(url => url.startsWith(nugetFeed)));
});

test('version matching is exact and case-insensitive', () => {
  const hosted = new Map([['broiler.native.windows', ['0.1.0-PREVIEW.6']], ['broiler.native', ['0.1.0-preview.60']]]);
  assert.throws(() => chooseFeed('nuget', packages, { nuget: hosted }), /Broiler\.Native 0\.1\.0-preview\.6/);
  hosted.set('broiler.native', ['0.1.0-preview.6']);
  assert.equal(chooseFeed('nuget', packages, { nuget: hosted }), 'nuget');
});

test('feed errors and unknown feeds stop the run', async () => {
  for (const status of [401, 403, 500]) {
    await assert.rejects(selectFeed('auto', packages, {}, fakeFeeds({
      [nugetFeed]: { 'broiler.native.windows': status },
    })));
  }
  await assert.rejects(selectFeed('github', packages, {}, fakeFeeds({})), /Unknown feed 'github'/);
  await assert.rejects(selectFeed('azure', packages, {}, fakeFeeds({})), /Unknown feed 'azure'/);
});
