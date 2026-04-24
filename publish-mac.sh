#!/bin/bash
set -e

CSPROJ="src/SchedulingAssistant/SchedulingAssistant.csproj"
REPO_URL="https://github.com/gschlitt/SchedulingAssistant"
PACK_ID="TermPoint"
TOKEN_FILE=".publish-token"

# ── Version ──────────────────────────────────────────────────────────────────

CURRENT_VERSION=$(grep -o '<Version>[^<]*</Version>' "$CSPROJ" | tail -1 | sed 's/<[^>]*>//g')

if [ -z "$VERSION" ]; then
    read -p "Version [$CURRENT_VERSION]: " input
    VERSION="${input:-$CURRENT_VERSION}"
fi

echo "Version: $VERSION"

# ── Token ────────────────────────────────────────────────────────────────────

TOKEN="${TERMPOINT_PUBLISH_TOKEN}"
if [ -z "$TOKEN" ] && [ -f "$TOKEN_FILE" ]; then
    TOKEN=$(cat "$TOKEN_FILE" | tr -d '[:space:]')
fi
if [ -z "$TOKEN" ]; then
    read -p "GitHub token (or create .publish-token file): " TOKEN
fi

# ── Clean ────────────────────────────────────────────────────────────────────

rm -rf publish/osx releases/osx

# ── Publish ──────────────────────────────────────────────────────────────────

echo "Publishing macOS $VERSION..."
dotnet publish "$CSPROJ" -c Release -r osx-x64 --self-contained -o ./publish/osx

# ── Pack ─────────────────────────────────────────────────────────────────────

echo "Packing macOS..."
vpk pack --packId "$PACK_ID" --packVersion "$VERSION" --packDir ./publish/osx \
    --mainExe TermPoint --outputDir ./releases/osx

# ── Upload ───────────────────────────────────────────────────────────────────

echo "Uploading macOS to GitHub..."
vpk upload github --repoUrl "$REPO_URL" --token "$TOKEN" \
    --outputDir ./releases/osx --tag "v$VERSION" --releaseName "v$VERSION"

# ── Done ─────────────────────────────────────────────────────────────────────

echo ""
echo "Done! macOS package uploaded to:"
echo "  https://github.com/gschlitt/SchedulingAssistant/releases"
