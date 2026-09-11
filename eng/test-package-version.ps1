# Integration tests use only a temporary local repository and bare remote.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script = Join-Path $PSScriptRoot 'package-version.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("broiler-version-tests-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Invoke-TestGit {
    & git @args | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "git failed ($LASTEXITCODE)." }
}
function Equal($expected, $actual) {
    if ($expected -ne $actual) { throw "Expected '$expected', got '$actual'." }
}
function MustFail([scriptblock] $body) {
    $failed = $false
    try { & $body | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw 'Expected operation to fail.' }
}

Push-Location $testRoot
try {
    Invoke-TestGit init --bare remote.git
    Invoke-TestGit init work
    Set-Location work
    Invoke-TestGit config user.name 'Version test'
    Invoke-TestGit config user.email 'version-test@example.invalid'
    Invoke-TestGit commit --allow-empty -m Initial
    Invoke-TestGit remote add origin ../remote.git
    $base = @{ BaseVersion = '0.1.0-preview.7' }
    Equal '0.1.0-preview.7' (& $script @base -RunId 1)
    Equal '0.1.0-preview.7' (& $script @base -RunId 2) # dry runs consume nothing
    Equal '0.1.0-preview.7' (& $script @base -RunId 1 -Reserve -Version '0.1.0-preview.7')
    Equal '0.1.0-preview.7' (& $script @base -RunId 1) # retry after a partial push
    Equal '0.1.0-preview.7' (& $script @base -RunId 1 -Reserve -Version '0.1.0-preview.7')
    Equal '0.1.0-preview.8' (& $script @base -RunId 2)
    MustFail { & $script @base -RunId 2 -Reserve -Version '0.1.0-preview.7' }
    MustFail { & $script @base -RunId 2 -Tag 'v0.1.0-preview.7' -Reserve -Version '0.1.0-preview.7' }
    Invoke-TestGit tag v0.1.0-preview.9
    Equal '0.1.0-preview.10' (& $script @base -RunId 2) # numeric, not lexical ordering
    Invoke-TestGit tag v0.2.0-preview.100
    Equal '0.1.0-preview.10' (& $script @base -RunId 2)
    Equal '0.1.0-preview.9' (& $script @base -RunId 2 -Tag 'v0.1.0-preview.9')
    Equal '0.1.0' (& $script @base -RunId 2 -Tag 'v0.1.0')
    MustFail { & $script @base -RunId 2 -Tag 'v0.2.0-preview.1' }
    MustFail { & $script @base -RunId 2 -Tag 'v0.1.0-preview.6' }
    MustFail { & $script @base -RunId 2 -Tag 'v0.1.0-preview.1;echo injected' }
    Invoke-TestGit commit --allow-empty -m Changed
    MustFail { & $script @base -RunId 1 }
    # Prove the reservation survives a fresh checkout.
    Invoke-TestGit push origin HEAD:refs/heads/main
    Set-Location ..
    Invoke-TestGit clone --branch main remote.git fresh
    Set-Location fresh
    Equal '0.1.0-preview.8' (& $script @base -RunId 3)
    Write-Host 'Package version integration tests passed.'
}
finally {
    Pop-Location
    # Only remove the unique temporary directory created above.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolved)).StartsWith('broiler-version-tests-')) {
        throw "Unsafe test cleanup path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

