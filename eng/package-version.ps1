# Resolve a preview, or reserve the resolved version immediately before publishing.
# Reservations survive feed failures; rerunning the same Actions run reuses its version.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseVersion,
    [Parameter(Mandatory)][string] $RunId,
    [string] $Tag,
    [switch] $Reserve,
    [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)][string[]] $Arguments)
    $result = & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments[0]) failed ($LASTEXITCODE)." }
    $result
}

if ($BaseVersion -notmatch '^(\d+\.\d+\.\d+)-preview\.([1-9]\d*)$') {
    throw "Expected a base version such as 0.1.0-preview.7; got '$BaseVersion'."
}
$prefix = $Matches[1]
$next = [long] $Matches[2]
$minimum = $next
$previewPattern = '^' + [regex]::Escape($prefix) + '-preview\.([1-9]\d*)$'
$releasePattern = '^' + [regex]::Escape($prefix) + '(-preview\.[1-9]\d*)?$'
$owner = "Broiler.Media publish run $RunId"
$headCommit = Invoke-Git rev-parse HEAD

# Checkout must fetch all tags. A single workflow concurrency group serializes both feeds.
$records = @(Invoke-Git for-each-ref '--format=%(refname:strip=2)|%(contents:subject)' refs/tags)
$resolved = $null
foreach ($record in $records) {
    $name, $subject = $record -split '\|', 2
    $candidate = $name -replace '^(publish/|v)', ''
    if ($candidate -match $previewPattern) {
        $next = [Math]::Max($next, [long] $Matches[1] + 1)
    }
    if ($name.StartsWith('publish/') -and $subject -eq $owner) {
        if ($resolved) { throw "Multiple version reservations for run $RunId." }
        if ((Invoke-Git rev-parse "$name^{}") -ne $headCommit) {
            throw 'A reserved version cannot be reused for a different commit.'
        }
        $resolved = $candidate
    }
}

if ($Tag) {
    $tagVersion = $Tag -creplace '^v', ''
    if (-not $Tag.StartsWith('v') -or $tagVersion -notmatch $releasePattern) {
        throw "Tag '$Tag' must be v$prefix or v$prefix-preview.N."
    }
    if ($tagVersion -match $previewPattern -and [long] $Matches[1] -lt $minimum) {
        throw "Tag '$Tag' precedes the repository preview floor '$BaseVersion'."
    }
    if ($resolved -and $resolved -ne $tagVersion) { throw "Tag differs from this run's reservation." }
    $resolved = $tagVersion
}
if (-not $resolved) { $resolved = "$prefix-preview.$next" }

if ($Reserve) {
    if ($Version -ne $resolved) {
        throw "Version changed from '$Version' to '$resolved'; rebuild before publishing."
    }
    $reservation = "publish/$resolved"
    $existing = @($records | Where-Object { ($_ -split '\|', 2)[0] -eq $reservation })
    if ($existing.Count -gt 0) {
        if (($existing[0] -split '\|', 2)[1] -ne $owner) {
            throw "Version '$resolved' is already reserved by another run."
        }
    }
    else {
        Invoke-Git -Arguments @('-c', 'user.name=github-actions[bot]', '-c',
            'user.email=41898282+github-actions[bot]@users.noreply.github.com',
            'tag', '-a', $reservation, '-m', $owner, 'HEAD') | Out-Host
    }
    # Never force: a concurrent reservation or a protected tag fails before any feed push.
    Invoke-Git push origin "refs/tags/$reservation" | Out-Host
}

$resolved
