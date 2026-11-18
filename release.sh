#!/usr/bin/env bash
# Builds the plugin, zips it, and adds the new version to manifest.json.
#
#   GITHUB_REPO=owner/repo ./release.sh 0.1.1.0 "What changed"
#
# Needs python3, plus either the .NET 10 SDK or Docker. If the GitHub CLI (gh) is
# installed and logged in, the zip is also uploaded as a GitHub release.
set -euo pipefail

VERSION="${1:?usage: GITHUB_REPO=owner/repo ./release.sh 0.1.1.0 [\"changelog\"]}"
CHANGELOG="${2:-Release $VERSION}"
REPO="${GITHUB_REPO:?set GITHUB_REPO=owner/repo}"
TAG="v$VERSION"

cd "$(dirname "$0")"

if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must have four parts, like 0.1.1.0" >&2
  exit 1
fi

# Keep the version in the project files in sync with the release.
sed -i -E "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$VERSION</AssemblyVersion>|; s|<FileVersion>.*</FileVersion>|<FileVersion>$VERSION</FileVersion>|" Jellyfin.Plugin.SlackNewsletter.csproj
sed -i -E "s|^version: .*|version: \"$VERSION\"|" build.yaml

if [[ -z "${SKIP_BUILD:-}" ]]; then
  rm -rf out
  if command -v dotnet >/dev/null 2>&1; then
    dotnet publish -c Release -o out
  else
    docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
      -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
      dotnet publish -c Release -o out
  fi
fi

python3 - "$VERSION" "$REPO" "$TAG" "$CHANGELOG" <<'PY'
import datetime
import hashlib
import json
import os
import sys
import zipfile

version, repo, tag, changelog = sys.argv[1:5]
dll = "Jellyfin.Plugin.SlackNewsletter.dll"

os.makedirs("dist", exist_ok=True)
zip_name = f"slacknewsletter_{version}.zip"
zip_path = os.path.join("dist", zip_name)
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    z.write(os.path.join("out", dll), dll)

with open(zip_path, "rb") as f:
    checksum = hashlib.md5(f.read()).hexdigest().upper()

with open("manifest.json") as f:
    manifest = json.load(f)

plugin = manifest[0]
plugin["versions"] = [v for v in plugin["versions"] if v["version"] != version]
plugin["versions"].insert(0, {
    "version": version,
    "changelog": changelog,
    "targetAbi": "12.0.0.0",
    "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{zip_name}",
    "checksum": checksum,
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
})

with open("manifest.json", "w") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")

print(f"Built {zip_path} (MD5 {checksum})")
PY

ZIP="dist/slacknewsletter_$VERSION.zip"

if command -v gh >/dev/null 2>&1; then
  gh release create "$TAG" "$ZIP" --repo "$REPO" --title "$TAG" --notes "$CHANGELOG"
else
  echo
  echo "gh is not installed. Create the release by hand: on GitHub, make a release tagged $TAG"
  echo "and attach $ZIP."
fi

echo
echo "Now publish the manifest:"
echo "  git add manifest.json && git commit -m \"Release $TAG\" && git push"
echo
echo "Repository URL for Jellyfin:"
echo "  https://raw.githubusercontent.com/$REPO/main/manifest.json"
