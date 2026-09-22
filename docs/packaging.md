# CI, packages, and releases

The five component repositories use the same workflow structure and release helpers.
`Directory.Packages.props` centrally manages dependency versions. Release versions
are separate: `eng/Broiler.Packaging.props` supplies defaults, with repository
overrides in `Directory.Build.props`. Packages within one repository share a version;
each repository advances its own preview sequence.

## Build, test, and pack

Use the .NET 10 SDK, Node.js 24 for release scripts, PowerShell 7 for packaging,
and Bash (Git Bash on Windows) for the test runner.

```sh
dotnet build Broiler.Media.slnx -c Release
bash ./eng/run-tests.sh Release
node --test eng/resolve-preview-version.test.mjs
pwsh -File eng/pack.ps1
```

Use `Debug` or `Release`; platform-suffixed solution configurations are obsolete.
The test script runs the suites appropriate to the host. Input and Graphics build
the host's platform suite explicitly because those projects are excluded from the
normal solution build. DOM uses `dotnet test`; the other components use console runners.

`eng/pack.ps1` enumerates **every packable project**, including excluded providers,
and verifies all 9 packages, versions, internal dependencies, README, icon,
assemblies, API documentation, and symbols. Tests, demos, and diagnostic tools do
not ship. Use Windows to pack the complete set. The output directory must contain
no previous packages; choose `-Output <empty-directory>` for another run. Optional
`-Version 0.1.0-preview.N` stamps the assembly and package versions together.

## Package feeds

This repository maps `Broiler.*` packages to GitHub Packages and other packages to NuGet.org. Cross-repository dependencies use the versions in `Directory.Packages.props`; sibling source checkouts do not replace them.
`NuGet.config` explicitly clears inherited sources, disabled-source settings, and
source mappings so machine settings cannot silently change the feed selection.

For GitHub Packages, set the process environment variable (never commit a token):

```text
NuGetPackageSourceCredentials_github=Username=<github-user>;Password=<PAT>;ValidAuthenticationTypes=Basic
```

The source name is exactly `github`. A local PAT needs `read:packages`. Workflows
supply `GITHUB_TOKEN`; every upstream package must grant this repository access
under **Manage Actions access**. `packages: read` alone does not grant access to
another repository's private packages. See [GitHub's registry documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry).

Native must be available before Media; Media before Input; and those dependencies
before Graphics. DOM is independent. Selecting NuGet.org as the publish destination
does not copy upstream packages there.
The `Broiler.Native.Windows` and `Broiler.Native` versions Media references are
already on NuGet.org, so a NuGet.org publish restores without GitHub Packages.

## CI and Publish

CI builds and tests `Release` on Ubuntu and Windows. Windows packs and attaches the
complete package set as `nuget-packages`. Publish calls this same CI workflow with
the resolved version and downloads its validated artifacts; it does not rebuild them.

Run **Publish** manually with `target=github` or `target=nuget`. `dry-run=true` is
the default: it selects a version, runs CI, packs, and verifies a fresh consumer
restore without pushing anything. The restore uses an isolated cache, the local
release artifacts, and only the destination feed (plus NuGet.org for public
third-party dependencies). It catches missing transitive dependencies before upload.
You can also run it locally:

```sh
pwsh -File eng/verify-feed.ps1 -Target github -Packages artifacts
```

Leave `version-suffix` empty to select the next unused `preview.N`. The resolver
checks every shipping package on **both** NuGet.org and GitHub Packages, whichever
feed is the destination, so the preview sequence is cumulative: with `preview.3` on
GitHub Packages and `preview.2` on NuGet.org, the next publish to either feed is
`preview.4`. The configured preview is the minimum; a partially published preview is skipped.
An explicit suffix must be unused and at least the computed next preview. Feed
errors stop the run. Publish runs are serialized within each repository.

A tag `v0.1.0-preview.N` publishes that exact version to NuGet.org after the same
checks. Only `X.Y.Z-preview.N` versions on the configured release line are accepted;
stable and other prerelease formats are rejected. After a partial upload, use a
new preview rather than reusing the old tag. Dry runs do not reserve a version.

NuGet.org publishing requires the repository secret `NUGET_API_KEY`. GitHub
publishing uses `GITHUB_TOKEN`. Symbol packages are attached to the workflow artifact
and pushed alongside packages to NuGet.org; GitHub receives `.nupkg` files only.
