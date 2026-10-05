# Signed updates

The launcher installs a game build only if its `update.json` is signed with the Conquer release key. Without this,
anyone who could write to the GitHub release (a leaked token, a compromised account or build tool) could make every
installed launcher download and run their program.

## How it works

- CI builds the game zip and a small manifest (version, build number, SHA-256, size).
- The `publish` job signs the manifest with the release key (ECDSA P-256) into `update.json` and uploads it with
  the zip. It only runs on `main`, in the `release` environment that holds the key.
- The launcher has the public key built in (`installer/update-public-key.txt`). It refuses an `update.json` with a
  bad or missing signature, a zip that doesn't match the signed checksum or size, and any build number lower than
  the one installed, so an old build can't be pushed back out.

## One-time setup

1. On your PC, from the repository root: `powershell -ExecutionPolicy Bypass -File installer\new-update-key.ps1`.
   It needs the .NET 8 SDK. If the GitHub CLI (`gh`) is installed and signed in, it also saves the secret for you.
2. If it didn't, open the repository's Settings > Environments > `release` (create it if it's missing), and add an
   environment secret named `UPDATE_SIGNING_KEY` with the whole contents of the private key file.
3. In the same `release` environment, set **Deployment branches** to `main` only, so a workflow on another branch
   can't read the key. Optionally add yourself as a **required reviewer** to approve each release by hand.
4. Commit `installer/update-public-key.txt` and push. The next build on `main` is signed and published.
5. Back up the private key file (a password manager is fine), then delete it from your PC.

Until steps 1 to 4 are done, builds on `main` stop at "Sign the update" and nothing is published.

## Players who installed before signing

Their launcher still reads the old unsigned `latest.json`, which CI keeps publishing so their game keeps updating.
Those launchers stay unprotected until the player downloads and runs the new `ConquerSetup.exe` once.

## If the key is lost or leaked

Run the script again (after deleting the old public key file), commit the new public key and update the secret.
Every player then has to reinstall from the new `ConquerSetup.exe`, because their launcher only trusts the old key.
