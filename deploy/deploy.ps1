<#
    Publishes the scraper service and puts it on the server. Nothing restarts: the
    systemd timer starts the next run with the new files.

        pwsh -File deploy/deploy.ps1            # publish, rsync, show the timer
        pwsh -File deploy/deploy.ps1 -DryRun    # show what would happen, touch nothing

    The transfer follows the traps measured for the Armamentarium web services
    (Armamentarium deploy.ps1, docs/server/betrieb.md): rsync from MSYS2 (pacman -S
    rsync openssh) driving only the ssh beside it, the ssh options spelled out
    because that ssh resolves ~ to the MSYS2 home, and a relative source path
    because "C:" would be read as a host name.

    Unlike those services the files are not secret and the service runs as a
    DynamicUser outside any group, so they are world readable (755/644) and owned
    by the deploying account. One-time setup on the server: docs/server/betrieb.md
    of the Armamentarium repository, section "Boxinhalt-Dienst".
#>
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$repo     = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repo 'src/Armamentarium.BoxData.Scraper'
$assembly = 'Armamentarium.BoxData.Scraper.dll'
$staging  = Join-Path ([System.IO.Path]::GetTempPath()) 'armamentarium-box-data-publish'
$remote   = 'strecker-web'
$appDir   = '/srv/armamentarium-box-data/current'
$timer    = 'armamentarium-box-data.timer'

foreach ($tool in 'dotnet', 'rsync') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool not found in PATH ... rsync comes from MSYS2: pacman -S rsync openssh"
    }
}

$sshExe = Join-Path (Split-Path (Get-Command rsync).Source) 'ssh.exe'
if (-not (Test-Path -LiteralPath $sshExe)) {
    throw "ssh.exe is missing next to $((Get-Command rsync).Source) ... pacman -S openssh"
}
$sshDir = ($HOME -replace '^([A-Za-z]):', '/$1' -replace '\\', '/') + '/.ssh'
$sshOpts = @('-F', "$sshDir/config", '-o', "UserKnownHostsFile=$sshDir/known_hosts", '-o', "IdentityFile=$sshDir/id_ed25519_streckerit")
$rsh = "'$($sshExe.Replace('\', '/'))' -F '$sshDir/config' -o 'UserKnownHostsFile=$sshDir/known_hosts' -o 'IdentityFile=$sshDir/id_ed25519_streckerit'"

Write-Output '1/2  dotnet publish -c Release'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
dotnet publish $project -c Release -o $staging | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath (Join-Path $staging $assembly))) {
    throw "$assembly is missing from the publish ... the unit would find nothing to start"
}

Write-Output "2/2  rsync -> ${remote}:$appDir"
$rsyncArgs = @('-rlptz', '--delete', '--chmod=D755,F644', '-e', $rsh, './', "${remote}:$appDir/")
if ($DryRun) { $rsyncArgs = @('--dry-run', '-v') + $rsyncArgs }
Push-Location -LiteralPath $staging
try { & rsync @rsyncArgs }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "rsync failed with exit code $LASTEXITCODE" }

if ($DryRun) {
    Write-Output 'Dry run, nothing changed on the server.'
    return
}

# One ssh call: ufw limits port 22 to six connections in thirty seconds.
& $sshExe @sshOpts $remote "systemctl list-timers $timer --no-pager"
if ($LASTEXITCODE -ne 0) { throw "cannot read the timer on $remote" }
Write-Output "Deployed. The next run takes the new files. Journal: ssh $remote journalctl -u armamentarium-box-data -n 50"
