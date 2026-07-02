# Android release-signing keystore

This folder holds the persistent keystore used to sign release APKs built by
`docker/build.sh android`. Every release must be signed with the **same**
key, or Android refuses to install a new build over a previously installed
one (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`) and forces the user to uninstall
first — which deletes the app's data.

## First-time setup

```bash
./docker/keystore/generate-keystore.sh
```

Run this **once**, on whichever machine will keep producing release builds.
It creates `release.keystore` in this folder and asks for a keystore
password (and, optionally, a separate key password). Nothing is written to
git — `release.keystore` is ignored by `.gitignore`, and the script never
saves the passwords anywhere.

`docker/build.sh android` will refuse to run without this file, and will
prompt you for the same passwords at every build.

## Back it up

The keystore file and its passwords cannot be regenerated: if lost, no
future release can ever update existing installs in place again — every
user would need to uninstall and reinstall from scratch. Copy
`release.keystore` and the passwords to a password manager or encrypted
backup outside this repository as soon as you generate them.

## What NOT to do

- Do not commit `release.keystore` (or any `*.jks`/`*.keystore` file) to git.
- Do not re-run `generate-keystore.sh` once a keystore already exists — it
  refuses on purpose; delete the file yourself only if you fully accept the
  consequences above.
