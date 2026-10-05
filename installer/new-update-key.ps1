# Creates the release signing key for Conquer updates. Run once, from the repository root, on your own PC:
#
#   powershell -ExecutionPolicy Bypass -File installer\new-update-key.ps1
#
# It writes the public key to installer\update-public-key.txt (commit that file) and the private key to a file in
# your user folder, then stores the private key as the UPDATE_SIGNING_KEY secret of the "release" environment if the
# GitHub CLI (gh) is installed and signed in. See installer\SIGNING.md.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$public = Join-Path $root 'installer\update-public-key.txt'
$private = Join-Path $env:USERPROFILE 'conquer-update-signing-key.pem'

if (Test-Path $public) { throw "$public already exists. Delete it first only if you really mean to replace the key." }

dotnet run --project (Join-Path $root 'installer\Conquer.UpdateSigner') -c Release -- keygen $public $private
if ($LASTEXITCODE -ne 0) { exit 1 }

if (Get-Command gh -ErrorAction SilentlyContinue) {
    # Create the release environment and limit it to main, so only workflows on main can read the key.
    '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' |
        gh api -X PUT 'repos/{owner}/{repo}/environments/release' --input - | Out-Null
    gh api -X POST 'repos/{owner}/{repo}/environments/release/deployment-branch-policies' -f name=main 2>$null | Out-Null
    Get-Content $private -Raw | gh secret set UPDATE_SIGNING_KEY --env release
    if ($LASTEXITCODE -eq 0) { Write-Host 'Saved the private key as the UPDATE_SIGNING_KEY secret of the release environment.' }
} else {
    Write-Host 'GitHub CLI not found. Add the contents of the private key file as the UPDATE_SIGNING_KEY secret of the'
    Write-Host '"release" environment: repository Settings > Environments > release > Add environment secret.'
}

Write-Host ''
Write-Host "Next: commit installer\update-public-key.txt, back up $private somewhere safe (a password manager),"
Write-Host 'then delete it from this PC. If it is lost, installed games can only get updates again by reinstalling.'
