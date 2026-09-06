$ErrorActionPreference = 'Stop'
$serviceName = 'FindUpTo POS Server'
$installDir = if ($args.Count -gt 0) { (Resolve-Path $args[0]).Path } else { Join-Path $env:ProgramFiles 'FindUpTo POS Server' }
$exe = Join-Path $installDir 'FindUpTo.Pos.Server.exe'

if (-not (Test-Path $exe)) { throw "Server executable not found: $exe. Run publish.ps1 first." }

New-Item -ItemType Directory -Force -Path $installDir | Out-Null

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
    -StartupType Automatic

Start-Service -Name $serviceName
Write-Host "Installed and started $serviceName"
