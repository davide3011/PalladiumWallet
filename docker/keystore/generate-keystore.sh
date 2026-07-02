#!/usr/bin/env bash
# Generates the persistent Android release-signing keystore used by
# docker/build.sh. Every APK built with this keystore carries the same
# signature, which is what lets Android treat a new release as an update to
# a previously installed one instead of a conflicting, different app.
#
# Run this ONCE. Re-running it after the keystore already exists is refused
# below on purpose: regenerating it changes the signature, and every device
# with a prior release installed would then need a full uninstall (and lose
# any app data that isn't in the wallet file itself) to receive the next
# update.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KEYSTORE_PATH="${SCRIPT_DIR}/release.keystore"
ALIAS="palladiumwallet"

if [[ -f "$KEYSTORE_PATH" ]]; then
    echo "A keystore already exists at $KEYSTORE_PATH — refusing to overwrite it." >&2
    echo "Delete it manually first only if you understand the consequences above." >&2
    exit 1
fi

command -v keytool &>/dev/null || {
    echo "keytool not found on PATH — install a JDK (e.g. openjdk-17-jdk) and retry." >&2
    exit 1
}

echo "Creating the Palladium Wallet Android release-signing keystore."
echo "This file and the passwords you enter now must be kept safe OUTSIDE this"
echo "repository (password manager, encrypted backup, ...). They are never"
echo "committed to git. Losing them means no future release can ever update"
echo "existing installs in place — only a fresh keystore + full user reinstall."
echo

read -r -s -p "Keystore password (min 6 characters): " KS_PASS
echo
read -r -s -p "Confirm keystore password: " KS_PASS_CONFIRM
echo
if [[ "$KS_PASS" != "$KS_PASS_CONFIRM" ]]; then
    echo "Passwords do not match." >&2
    exit 1
fi
if [[ "${#KS_PASS}" -lt 6 ]]; then
    echo "Password too short (keytool requires at least 6 characters)." >&2
    exit 1
fi

read -r -s -p "Key password (press Enter to reuse the keystore password): " KEY_PASS
echo
KEY_PASS="${KEY_PASS:-$KS_PASS}"

export KS_PASS KEY_PASS
keytool -genkeypair -v \
    -keystore "$KEYSTORE_PATH" \
    -alias "$ALIAS" \
    -keyalg RSA -keysize 2048 -validity 10000 \
    -storepass:env KS_PASS -keypass:env KEY_PASS \
    -dname "CN=PalladiumWallet, OU=PalladiumWallet, O=PalladiumWallet, C=IT"
unset KS_PASS KEY_PASS

echo
echo "Keystore created at $KEYSTORE_PATH (git-ignored, alias: $ALIAS)."
echo "Back it up now, together with the passwords — it cannot be regenerated."
echo "./docker/build.sh android will prompt for these same passwords at build time."
