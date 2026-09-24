# CI, packages, and releases

The Broiler component repositories use a unified workflow structure and release helpers.
`Directory.Packages.props` centrally manages external dependency versions. Release versions
are configured separately: `eng/Broiler.Packaging.props` supplies suite-wide defaults, with
component overrides in `Directory.Build.props`. Packages within this repository share a version;
each component repository advances its own preview sequence.

## Build, test, and pack

Requirements:
- .NET 10 SDK (`10.0.x`)
- Node.js 24 (for release validation scripts)
- PowerShell 7 or Windows PowerShell 5.1 (for packaging and feed verification)
- Bash (Git Bash on Windows or Linux bash) for the test runner

Run the developer workflow:

```sh
dotnet build Broiler.Media.slnx -c Release
bash ./eng/run-tests.sh Release
node --test eng/resolve-preview-version.test.mjs eng/select-restore-feed.test.mjs
pwsh -File eng/pack.ps1
```

Use `Debug` or `Release` configuration. The test script executes all suites enabled for
the host platform: the six portable test suites run on both Linux and Windows, while the
`Broiler.Media.Video.MediaFoundation.Tests` suite runs on Windows. Tests are self-hosted console
runners rather than unit-test framework assemblies, executing directly via `dotnet run`.

`eng/pack.ps1` enumerates **every packable project** in the solution and verifies all 9 packages,
versions, internal dependencies, README, icon, assemblies, XML API documentation, and symbol packages.
Tests, demos, and diagnostic tools do not pack. Use Windows to pack the complete set.
The output directory must contain no previous packages; specify `-Output <empty-directory>` for a
custom run. Optional `-Version 0.1.0-preview.N` stamps the assembly and package versions together.

## Package feeds

All package restore is performed strictly from **NuGet.org** (`https://api.nuget.org/v3/index.json`).
GitHub Packages is not used.

`NuGet.config` explicitly clears inherited sources, disabled sources, and source mappings so machine
or user-level settings cannot silently alter package resolution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Cross-repository dependencies (such as `Broiler.Native.Windows`) are pinned in `Directory.Packages.props`
and restore publicly from NuGet.org. No GitHub authentication tokens or `NuGetPackageSourceCredentials_*`
environment variables are required for developer restores or CI builds.

`eng/select-restore-feed.mjs` verifies that NuGet.org hosts every pinned `Broiler.*` package version
before building. Run the test suite for feed selection with:

```sh
node --test eng/select-restore-feed.test.mjs
```

## CI and Publish

### Continuous Integration (CI)

The CI workflow (`.github/workflows/ci.yml`) runs on pushes to `main`, pull requests, and workflow dispatch:
- Builds and runs tests in `Release` configuration across `ubuntu-latest` and `windows-latest`.
- Runs release script tests (`eng/resolve-preview-version.test.mjs` and `eng/select-restore-feed.test.mjs`).
- Verifies package restore from NuGet.org.
- On Windows, packs and validates all 9 packages and 8 symbol packages (`.snupkg`).
- Attaches the validated artifacts as `nuget-packages`.

Publish calls this same CI workflow with the resolved release version and consumes the validated artifacts
without rebuilding.

### Publishing to NuGet.org

Publishing is handled by `.github/workflows/publish.yml`, targeting **NuGet.org** exclusively:

1. **Version Resolution:** `eng/resolve-preview-version.mjs` queries the NuGet.org registration/flat container
   API to find the highest published preview number for the current `0.1.0-preview.*` line and computes the
   next unused preview version (or honours an explicit `version-suffix`).
2. **Validation and Pack:** Invokes the CI workflow with the computed version.
3. **Consumer Verification:** `eng/verify-feed.ps1 -Target nuget` sets up an isolated temporary consumer
   project and restores against NuGet.org and the local package artifacts with `--no-http-cache`. This verifies
   that consumers will be able to restore the packages without missing dependencies.
4. **Push:** When `dry-run` is false, pushes all `.nupkg` and matching `.snupkg` symbol packages to NuGet.org:

```sh
dotnet nuget push 'artifacts/*.nupkg' --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
```

Publishing requires the repository secret `NUGET_TOKEN` (NuGet.org API key).
Publishing can also be triggered by pushing a tag matching `v0.1.0-preview.N`.
Dry runs (`dry-run: true`, the default for manual workflow dispatches) perform all validations and attach
packages as artifacts without pushing to NuGet.org.
