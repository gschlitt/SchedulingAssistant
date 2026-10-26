#!/bin/bash
# Runs the TermPoint test suite (or a filtered part of it) when C: is low on space or TermPoint is running from
# bin\Debug (which locks the normal build output).
#
# It builds into a temporary folder using MSBuild hard links, so the copied NuGet and runtime files take almost
# no extra disk space, and a watchdog kills the run (by its own PID only) if free space on C: drops below a
# safety margin. The temporary folder is deleted afterwards.
#
# Usage (Git Bash, from anywhere):
#   bash tools/run-tests-lowdisk.sh <log file> [extra dotnet test args...]
#   FILTER="FullyQualifiedName~SavePrompt|FullyQualifiedName~Harness" bash tools/run-tests-lowdisk.sh <log file>
# Example, full suite with a results log:
#   bash tools/run-tests-lowdisk.sh "$TEMP/full.log" --logger "trx;LogFileName=full.trx" --results-directory "$TEMP"
# Then read the summary:  grep -E "Passed!|Failed!|\[FAIL\]|ABORT|cleaned" "$TEMP/full.log"
#
# Without FILTER it runs the whole suite (about 3-4 minutes). The persistence harness tests are skipped unless
# TERMPOINT_HARNESS=1 is set (a full harness run takes about 20 minutes).

LOG="$1"; shift
OUT="$TEMP/sa-build"
MIN_FREE_MB="${MIN_FREE_MB:-300}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"

rm -rf "$OUT"
cd "$REPO" || exit 2

FILTER_ARGS=()
if [ -n "$FILTER" ]; then FILTER_ARGS=(--filter "$FILTER"); fi

dotnet test src/TermPoint.Tests/TermPoint.Tests.csproj --nologo -v q --no-restore -nodeReuse:false \
  -p:UseSharedCompilation=false \
  -p:CreateHardLinksForCopyLocalIfPossible=true \
  -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true \
  -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true \
  -o "$OUT" \
  "${FILTER_ARGS[@]}" "$@" > "$LOG" 2>&1 &
PID=$!

while kill -0 $PID 2>/dev/null; do
  FREE=$(df -m /c | awk 'NR==2 {print $4}')
  if [ "$FREE" -lt "$MIN_FREE_MB" ]; then
    echo "ABORT: only ${FREE} MB free on C:, killing dotnet pid $PID" >> "$LOG"
    kill -9 $PID 2>/dev/null
    taskkill //PID $PID //T //F > /dev/null 2>&1
    break
  fi
  sleep 1
done
wait $PID 2>/dev/null
echo "exit $?" >> "$LOG"
rm -rf "$OUT"
echo "cleaned; free: $(df -m /c | awk 'NR==2 {print $4}') MB" >> "$LOG"
