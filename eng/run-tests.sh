#!/usr/bin/env bash
#
# Runs the Broiler.Input suites for one solution configuration.
#
# The suites are self-hosted console runners, not a test framework, so there is
# nothing for `dotnet test` to discover. Each runner prints PASS/FAIL per case
# and returns its failure count as the exit code.
#
# Which runners apply depends on the configuration, because the platform suffix
# decides which provider family the solution builds:
#
#   *-Windows        contract runner (net10.0-windows, win-x64) + Android runner
#   *-Linux          evdev runner    (net10.0, linux-x64)       + Android runner
#   Debug / Release  Android runner only
#
# The Android runner is platform-neutral, so it declares only Debug and Release
# and the solution maps the suffixed configurations onto those. That is why it
# starts with the base configuration rather than the suffixed one.
#
# Usage: eng/run-tests.sh [configuration]
#
# Run it after `dotnet build Broiler.Input.slnx -c <configuration>`. The runners
# start with --no-build, so they exercise exactly the binaries that build
# produced, including any -p:VersionSuffix the release workflow passed in.

set -euo pipefail

configuration="${1:-${CONFIGURATION:-}}"

if [ -z "$configuration" ]; then
  case "$(uname -s)" in
    Linux) configuration='Release-Linux' ;;
    *)     configuration='Release-Windows' ;;
  esac
  echo "No configuration given; assuming $configuration on $(uname -s)."
fi

base="${configuration%-Windows}"
base="${base%-Linux}"

if [ "$base" != 'Debug' ] && [ "$base" != 'Release' ]; then
  echo "Unknown configuration '$configuration'." >&2
  exit 2
fi

failed=''

run_suite() {
  local name="$1"
  local project="$2"
  local suite_configuration="$3"

  echo
  echo "=== $name suite ($suite_configuration) ==="
  if dotnet run --project "$project" -c "$suite_configuration" --no-build; then
    echo "OK   $name"
  else
    echo "FAIL $name" >&2
    failed="$failed $name"
  fi
}

case "$configuration" in
  *-Windows)
    run_suite 'contract' \
      'src/tests/Broiler.Input.Contract.Tests/Broiler.Input.Contract.Tests.csproj' \
      "$configuration"
    ;;
  *-Linux)
    run_suite 'linux' \
      'src/tests/Broiler.Input.Linux.Tests/Broiler.Input.Linux.Tests.csproj' \
      "$configuration"
    ;;
esac

run_suite 'android' \
  'src/tests/Broiler.Input.Android.Tests/Broiler.Input.Android.Tests.csproj' \
  "$base"

echo
if [ -n "$failed" ]; then
  echo "Failed suites:$failed" >&2
  exit 1
fi

echo 'All suites passed.'
