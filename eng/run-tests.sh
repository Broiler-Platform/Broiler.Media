#!/usr/bin/env bash
#
# Runs every test suite that the current build produced.
#
# The suites are self-hosted console runners rather than a test framework, so
# there is nothing for `dotnet test` to discover. Each one is an executable that
# prints its results and exits with the number of failures.
#
# Suites are discovered from the build output instead of being listed, so adding
# a test project needs no change here, and whichever suites the configuration
# actually built are the ones that run. Release-Linux, for example, excludes the
# MediaFoundation suite entirely, so it simply is not found.
#
# Run it from the repository root, after building:
#   dotnet build Broiler.Media.slnx -c Release-Linux
#   ./eng/run-tests.sh

set -uo pipefail

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

while IFS= read -r assembly; do
    found=$((found + 1))
    name=$(basename "$assembly" .dll)

    group_start "$name"
    if ! dotnet "$assembly"; then
        failed_suites+=("$name")
        status=1
    fi
    group_end
done < <(find src/tests -path '*/bin/*' -name '*.Tests.dll' -not -path '*/ref/*' | sort)

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
