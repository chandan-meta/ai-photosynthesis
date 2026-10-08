#!/bin/sh
# Fast compile check of the module scripts against Unity's assemblies (no editor needed).
cd "$(dirname "$0")"
for p in ${1:-Runtime Editor}; do
  echo "== $p"; dotnet build "$p.csproj" -nologo -v q -clp:NoSummary 2>&1 | grep -E "error|warning CS" | sed 's#.*Assets#Assets#' | sort -u | head -${2:-60}
done
