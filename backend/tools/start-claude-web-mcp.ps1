param([string]$CoursesJson)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$snapshotHome = if ($env:LMS_COMPANION_HOME -and [IO.Path]::IsPathRooted($env:LMS_COMPANION_HOME)) {
    $env:LMS_COMPANION_HOME
} else {
    Join-Path ([Environment]::GetFolderPath('UserProfile')) '.lms-companion'
}
$snapshot = Join-Path $snapshotHome 'canvas-courses-snapshot.json'

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$cloudflared = 'C:\Program Files (x86)\cloudflared\cloudflared.exe'
$dll = Join-Path $repoRoot 'backend\src\LmsCompanion.Mcp.Local\bin\Release\net10.0\publish-remote\LmsCompanion.Mcp.Local.dll'
foreach ($path in @($dotnet, $cloudflared, $dll)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required file is missing: $path" }
}
if ($CoursesJson) {
    if (-not (Test-Path -LiteralPath $CoursesJson)) { throw "Canvas JSON file is missing: $CoursesJson" }
    & $dotnet $dll import-courses 'https://seedonline.stanford.edu/' $CoursesJson
    if ($LASTEXITCODE -ne 0) { throw 'Canvas course import failed.' }
}
if (-not (Test-Path -LiteralPath $snapshot)) {
    throw 'Canvas course snapshot is missing for this Windows account. Provide -CoursesJson with the saved Canvas JSON file.'
}

$state = Join-Path $repoRoot '.local'
New-Item -ItemType Directory -Path $state -Force | Out-Null
$keyPath = Join-Path $state 'remote-key.txt'
if (-not (Test-Path -LiteralPath $keyPath)) {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $key = [BitConverter]::ToString($bytes).Replace('-', '')
    Set-Content -LiteralPath $keyPath -Value $key -NoNewline
}
$env:LMS_COMPANION_REMOTE_KEY = (Get-Content -LiteralPath $keyPath -Raw).Trim()
if ($env:LMS_COMPANION_REMOTE_KEY.Length -lt 32) { throw 'The remote key is too short.' }

$urlKeyPath = Join-Path $state 'url-key.txt'
if (-not (Test-Path -LiteralPath $urlKeyPath)) {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $urlKey = [BitConverter]::ToString($bytes).Replace('-', '')
    Set-Content -LiteralPath $urlKeyPath -Value $urlKey -NoNewline
}
$env:LMS_COMPANION_URL_KEY = (Get-Content -LiteralPath $urlKeyPath -Raw).Trim()
if ($env:LMS_COMPANION_URL_KEY.Length -lt 32) { throw 'The URL key is too short.' }

$serverOut = Join-Path $state 'mcp-server.out.log'
$serverErr = Join-Path $state 'mcp-server.err.log'
$server = Start-Process -FilePath $dotnet -ArgumentList @('"' + $dll + '"', 'serve-http-snapshot') -WindowStyle Hidden -PassThru -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr
$healthy = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Milliseconds 500
    if ($server.HasExited) { throw 'MCP server exited. Check .local\mcp-server.err.log.' }
    try {
        $response = Invoke-RestMethod -Uri 'http://127.0.0.1:5188/health' -TimeoutSec 2
        if ($response.status -eq 'healthy') { $healthy = $true; break }
    } catch { }
}
if (-not $healthy) { throw 'MCP server did not become healthy.' }

$tunnelOut = Join-Path $state 'cloudflared.out.log'
$tunnelErr = Join-Path $state 'cloudflared.err.log'
$tunnel = Start-Process -FilePath $cloudflared -ArgumentList @('tunnel', '--url', 'http://127.0.0.1:5188', '--no-autoupdate') -WindowStyle Hidden -PassThru -RedirectStandardOutput $tunnelOut -RedirectStandardError $tunnelErr
$url = $null
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    Start-Sleep -Seconds 1
    if ($tunnel.HasExited) { throw 'Cloudflare tunnel exited. Check .local\cloudflared.err.log.' }
    $log = if (Test-Path -LiteralPath $tunnelErr) { Get-Content -LiteralPath $tunnelErr -Raw } else { '' }
    if ($log -match 'https://[a-z0-9-]+\.trycloudflare\.com') {
        $url = $Matches[0]
        break
    }
}
if (-not $url) { throw 'Cloudflare did not provide a tunnel URL. Check .local\cloudflared.err.log.' }

($url + '/mcp/' + $env:LMS_COMPANION_URL_KEY) | Set-Clipboard
Write-Host 'Private MCP URL copied to clipboard for Claude. Paste it into the MCP server URL field; do not share it.'
Write-Host "Header-capable MCP clients can use $url/mcp with x-api-key."
Write-Host "Server PID: $($server.Id); tunnel PID: $($tunnel.Id)"
