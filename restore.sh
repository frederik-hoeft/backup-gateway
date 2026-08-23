#!/usr/bin/env bash
set -euo pipefail

CACHE="$(pwd)/.nuget-cache"
OUT="$(pwd)/nuget-packages"

rm -rf "$CACHE" "$OUT"
mkdir -p "$CACHE" "$OUT"

# Avoid an externally defined Platform variable confusing MSBuild solution restore.
unset Platform || true

dotnet restore Source/BackupGateway.slnx \
    --packages "$CACHE" \
    --source https://api.nuget.org/v3/index.json

find "$CACHE" -type f -name '*.nupkg' -exec cp -n {} "$OUT/" \;

echo "Downloaded packages:"
ls -1 "$OUT"
