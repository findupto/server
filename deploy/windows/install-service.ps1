$ErrorActionPreference = 'Stop'
$serviceName = 'FindUpTo POS Server'
$sourceDir = if ($args.Count -gt 0) { (Resolve-Path $args[0]).Path } else { Join-Path $PSScriptRoot '../../artifacts/windows-server' }
$installDir = if ($args.Count -gt 1) { [System.IO.Path]::GetFullPath($args[1]) } else { Join-Path $env:ProgramFiles 'FindUpTo POS Server' }
$exe = Join-Path $installDir 'FindUpTo.Pos.Server.exe'

if (-not (Test-Path (Join-Path $sourceDir 'FindUpTo.Pos.Server.exe'))) {
    throw "Published server executable not found in $sourceDir. Run publish.ps1 first."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $installDir -Recurse -Force

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    sc.exe delete "$serviceName" | Out-Null
    Start-Sleep -Seconds 1
}

New-Service -Name $serviceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName $serviceName `
    -Description 'FindUpTo restaurant POS API and realtime server' `
    -StartupType Automatic | Out-Null

sc.exe failure "$serviceName" reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

$ruleName = 'FindUpTo POS Server (HTTP 5000)'
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -Profile Domain,Private | Out-Null
}

Start-Service -Name $serviceName
Start-Sleep -Seconds 2
if ((Get-Service -Name $serviceName).Status -ne 'Running') {
    throw "$serviceName failed to start."
}

Write-Host "Installed and started $serviceName from $sourceDir"
Write-Host "Install directory: $installDir"
