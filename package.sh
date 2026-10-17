#!/usr/bin/env bash
# Builds a Thunderstore-ready zip:
#   <OUTPUT_DIR>/<NAMESPACE>-<NAME>-<VERSION>.zip
# containing manifest.json, icon.png, README.md, CHANGELOG.md and the plugin DLL.
#
# Usage: ./package.sh [path/to/RemoveIntroCinematic.dll]
set -euo pipefail

cd "$(dirname "$0")"
source ./thunderstore.conf

DLL=${1:-bin/Release/RemoveIntroCinematic.dll}

die() {
  echo "package.sh: $*" >&2
  exit 1
}

[[ -f "$DLL" ]] || die "plugin DLL not found at '$DLL' (run 'make build' first)"
[[ -f README_MOD.md ]] || die "README.md is missing"
[[ -f CHANGELOG.md ]] || die "CHANGELOG.md is missing"
[[ -f icon.png ]] || die "icon.png is missing"

[[ "$NAME" =~ ^[A-Za-z0-9_]+$ ]] || die "NAME may only contain letters, digits and underscores"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "VERSION must be Major.Minor.Patch"
((${#DESCRIPTION} <= 250)) || die "DESCRIPTION is longer than 250 characters"

# Thunderstore requires a 256x256 PNG icon. Read the size from the PNG IHDR chunk.
[[ "$(head -c 8 icon.png | od -An -tx1 | tr -d ' \n')" == "89504e470d0a1a0a" ]] || die "icon.png is not a PNG"
size=$(od -An -tu1 -j16 -N8 icon.png | awk '{print $1*16777216+$2*65536+$3*256+$4 "x" $5*16777216+$6*65536+$7*256+$8}')
[[ "$size" == "256x256" ]] || die "icon.png must be 256x256 (is $size)"

# Keep the BepInPlugin version in sync with the package version.
src_version=$(grep -oP 'PluginVersion\s*=\s*"\K[^"]+' src/RemoveIntroCinematicPlugin.cs)
[[ "$src_version" == "$VERSION" ]] || die "PluginVersion in source ($src_version) != VERSION in thunderstore.conf ($VERSION)"

json_escape() {
  local s=${1//\\/\\\\}
  s=${s//\"/\\\"}
  printf '%s' "$s"
}

deps_json=""
for dep in $DEPENDENCIES; do
  [[ "$dep" =~ ^[A-Za-z0-9_]+-[A-Za-z0-9_]+-[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "bad dependency string '$dep'"
  deps_json+="${deps_json:+, }\"$dep\""
done

staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT

cat >"$staging/manifest.json" <<EOF
{
  "name": "$(json_escape "$NAME")",
  "version_number": "$VERSION",
  "website_url": "$(json_escape "$WEBSITE_URL")",
  "description": "$(json_escape "$DESCRIPTION")",
  "dependencies": [$deps_json]
}
EOF

cp icon.png CHANGELOG.md "$DLL" "$staging/"
cp README_MOD.md "$staging/README.md"

mkdir -p "$OUTPUT_DIR"
out="$(realpath "$OUTPUT_DIR")/${NAMESPACE}-${NAME}-${VERSION}.zip"
rm -f "$out"
(cd "$staging" && zip -q -X "$out" manifest.json icon.png README.md CHANGELOG.md "$(basename "$DLL")")

echo "Created $out"
unzip -l "$out"
