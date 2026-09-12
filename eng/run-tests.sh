#!/usr/bin/env bash
# Run after dotnet build Broiler.Media.slnx using the same configuration.
# Tests are self-hosted console runners; dotnet test cannot discover them.
set -euo pipefail

configuration="${1:-${CONFIGURATION:-Release}}"
case "$configuration" in
  Debug|Release) ;;
  *) echo "Unknown configuration '$configuration'; use Debug or Release." >&2; exit 2 ;;
esac

failed=''
run_suite() {
  local name="$1"
  local project="src/tests/$name/$name.csproj"
  echo
  echo "=== $name ($configuration) ==="
  if dotnet run --project "$project" -c "$configuration" --no-build; then
    echo "OK   $name"
  else
    echo "FAIL $name" >&2
    failed="$failed $name"
  fi
}

for suite in \
  Broiler.Media.Tests \
  Broiler.Media.Audio.Tests \
  Broiler.Media.Audio.Managed.Tests \
  Broiler.Media.Video.Tests \
  Broiler.Media.Image.Tests \
  Broiler.Media.Image.Managed.Tests; do
  run_suite "$suite"
done

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) run_suite Broiler.Media.Video.MediaFoundation.Tests ;;
  *) echo 'Skipping Media Foundation tests: Windows is required.' ;;
esac

echo
if [ -n "$failed" ]; then
  echo "Failed suites:$failed" >&2
  exit 1
fi
echo 'All suites passed.'
