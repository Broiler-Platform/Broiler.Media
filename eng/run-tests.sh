#!/usr/bin/env bash
#
# Runs every test suite enabled by the selected solution configuration.
#
# The suites are self-hosted console runners rather than a test framework, so
# there is nothing for `dotnet test` to discover. Each one is an executable that
# prints its results and exits with the number of failures.
#
# Discover projects, then require their configured output. Do not run stale
# assemblies from previous Debug/Release or Linux/Windows builds.
#
# Run it from the repository root, after building:
#   dotnet build Broiler.Media.slnx -c Release-Linux
#   ./eng/run-tests.sh Release-Linux

set -uo pipefail

configuration="${1:-Release-Linux}"
case "$configuration" in
    Debug|Release|Debug-Linux|Release-Linux|Debug-Windows|Release-Windows) ;;
    *) echo "Unsupported solution configuration: $configuration" >&2; exit 1 ;;
esac
base_configuration="${configuration%-*}"

if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
    group_start() { echo "::group::$1"; }
    group_end() { echo "::endgroup::"; }
    report_error() { echo "::error::$1"; }
else
    group_start() { echo "--- $1"; }
    group_end() { echo; }
    report_error() { echo "ERROR: $1" >&2; }
fi

status=0
found=0
failed_suites=()

for project in src/tests/*.Tests/*.csproj; do
    name=$(basename "$project" .csproj)
    project_configuration="$base_configuration"
    if [ "$name" = 'Broiler.Media.Video.MediaFoundation.Tests' ]; then
        [[ "$configuration" = *-Windows ]] || continue
        project_configuration="$configuration"
    fi
    found=$((found + 1))
    group_start "$name"
    output="$(dirname "$project")/bin/$project_configuration"
    mapfile -t configs < <(find "$output" -name "$name.runtimeconfig.json" 2>/dev/null | sort)
    if [ "${#configs[@]}" -ne 1 ]; then
        report_error "Expected one $name runtime configuration under $output; build $configuration first."
        failed_suites+=("$name")
        status=1
    elif ! dotnet "${configs[0]%.runtimeconfig.json}.dll"; then
        failed_suites+=("$name")
        status=1
    fi
    group_end
done

if [ "$found" -eq 0 ]; then
    report_error "No test assemblies found under src/tests - did the build run, and with the configuration you expected?"
    exit 1
fi

if [ "$status" -ne 0 ]; then
    for suite in "${failed_suites[@]}"; do
        report_error "$suite reported failing tests"
    done
    echo "$((found - ${#failed_suites[@]}))/$found suite(s) passed."
    exit 1
fi

echo "All $found test suite(s) passed."
