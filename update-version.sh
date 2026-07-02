#!/usr/bin/env bash
# Bumps the app version everywhere it's tracked and stubs a CHANGELOG.md entry.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

APP_CSPROJ="src/App/PalladiumWallet.App.csproj"
ANDROID_CSPROJ="src/App.Android/PalladiumWallet.App.Android.csproj"
CHANGELOG="CHANGELOG.md"

current_version=$(grep -oP '(?<=<Version>)[^<]+(?=</Version>)' "$APP_CSPROJ")
echo "Current version: $current_version"

read -rp "New version (e.g. 1.0.0): " new_version

if [[ -z "$new_version" ]]; then
    echo "No version entered, aborting." >&2
    exit 1
fi

if ! [[ "$new_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Invalid version format: '$new_version' (expected X.Y.Z)." >&2
    exit 1
fi

if [[ "$new_version" == "$current_version" ]]; then
    echo "New version is the same as the current one ($current_version), aborting." >&2
    exit 1
fi

# 1. Single source of truth: App csproj <Version>
sed -i "s|<Version>$current_version</Version>|<Version>$new_version</Version>|" "$APP_CSPROJ"

# 2. Android head: ApplicationDisplayVersion (versionName) must mirror it,
#    and ApplicationVersion (versionCode) must strictly increase on every
#    release or users can't update in place.
current_code=$(grep -oP '(?<=<ApplicationVersion>)[^<]+(?=</ApplicationVersion>)' "$ANDROID_CSPROJ")
new_code=$((current_code + 1))

sed -i "s|<ApplicationDisplayVersion>$current_version</ApplicationDisplayVersion>|<ApplicationDisplayVersion>$new_version</ApplicationDisplayVersion>|" "$ANDROID_CSPROJ"
sed -i "s|<ApplicationVersion>$current_code</ApplicationVersion>|<ApplicationVersion>$new_code</ApplicationVersion>|" "$ANDROID_CSPROJ"

# 3. CHANGELOG.md: stub a new entry above the previous top-most one.
today=$(date +%F)
if grep -q "^## \[$new_version\]" "$CHANGELOG"; then
    echo "CHANGELOG.md already has an entry for $new_version, leaving it untouched."
else
    awk -v ver="$new_version" -v date="$today" '
        !done && /^## \[/ {
            print "## [" ver "] — " date
            print ""
            print "TODO: describe the changes in this release."
            print ""
            done = 1
        }
        { print }
    ' "$CHANGELOG" > "$CHANGELOG.tmp"
    mv "$CHANGELOG.tmp" "$CHANGELOG"
fi

echo
echo "Updated:"
echo "  - $APP_CSPROJ: Version $current_version -> $new_version"
echo "  - $ANDROID_CSPROJ: ApplicationDisplayVersion $current_version -> $new_version, ApplicationVersion (versionCode) $current_code -> $new_code"
echo "  - $CHANGELOG: stubbed entry for $new_version (fill in the details manually)"
echo
echo "Review the diff, fill in the CHANGELOG entry, then commit and tag manually."
