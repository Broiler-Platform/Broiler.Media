param(
    [Parameter(Mandatory)][string] $Version,
    [string] $Path = './artifacts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

# A release is one complete dependency graph, including both Windows packages.
$ids = @(
    'Broiler.Media', 'Broiler.Media.All',
    'Broiler.Media.Audio', 'Broiler.Media.Audio.Managed',
    'Broiler.Media.Image', 'Broiler.Media.Image.Managed',
    'Broiler.Media.Video', 'Broiler.Media.Video.Windows',
    'Broiler.Media.Video.MediaFoundation'
)
$packages = @(Get-ChildItem -LiteralPath $Path -Filter '*.nupkg')
if ($packages.Count -ne $ids.Count) { throw "Expected $($ids.Count) packages, found $($packages.Count)." }

foreach ($id in $ids) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $Path "$id.$Version.nupkg"))
    try {
        $entry = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
        if ($entry.Count -ne 1) { throw "$id must contain exactly one nuspec." }
        $reader = [IO.StreamReader]::new($entry[0].Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $manifest.package.metadata
        if ($metadata.id -ne $id -or $metadata.version -ne $Version) { throw "$id has incorrect identity/version." }
        foreach ($file in @('README.md', 'icon.png')) {
            if (-not $archive.GetEntry($file)) { throw "$id is missing $file." }
        }
        $dependencies = @($manifest.SelectNodes("//*[local-name()='dependency']"))
        foreach ($dependency in $dependencies) {
            if ($dependency.id -notin $ids) { throw "$id references unexpected package $($dependency.id)." }
            if ($dependency.version -notin @($Version, "[$Version, )", "[$Version]")) {
                throw "$id dependency $($dependency.id) has version $($dependency.version), expected $Version."
            }
        }
        if ($id -eq 'Broiler.Media.All') {
            if (@($dependencies | Select-Object -ExpandProperty id -Unique).Count -ne $ids.Count - 1) {
                throw 'Broiler.Media.All must depend on every runtime package.'
            }
        }
        else {
            foreach ($extension in @('dll', 'xml')) {
                if (-not $archive.GetEntry("lib/net10.0/$id.$extension")) { throw "$id is missing its $extension output." }
            }
            $symbols = [IO.Compression.ZipFile]::OpenRead((Join-Path $Path "$id.$Version.snupkg"))
            try {
                if (-not $symbols.GetEntry("lib/net10.0/$id.pdb")) { throw "$id is missing its symbols." }
            }
            finally { $symbols.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
Write-Host "Verified all $($ids.Count) packages, symbols, metadata, and dependency versions ($Version)."
