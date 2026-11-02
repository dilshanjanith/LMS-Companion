param(
    [int]$McpPort = 5124,
    [switch]$SkipPluginUpdate
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$cloudflared = 'C:\Program Files (x86)\cloudflared\cloudflared.exe'
$mcpProject = Join-Path $repoRoot 'backend\src\LmsCompanion.Mcp\LmsCompanion.Mcp.csproj'
$pluginMcpJson = Join-Path $repoRoot 'claude-plugin\lms-companion\.mcp.json'
$stateDir = Join-Path $repoRoot '.local'

foreach ($path in @($dotnet, $cloudflared, $mcpProject, $pluginMcpJson)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required path is missing: $path" }
}

New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
$serverOut = Join-Path $stateDir 'live-mcp.out.log'
$serverErr = Join-Path $stateDir 'live-mcp.err.log'
$tunnelOut = Join-Path $stateDir 'live-mcp-tunnel.out.log'
$tunnelErr = Join-Path $stateDir 'live-mcp-tunnel.err.log'

try {
    $healthResponse = Invoke-WebRequest -Uri "http://127.0.0.1:$McpPort/health" -TimeoutSec 2 -UseBasicParsing
    if ($healthResponse.StatusCode -ne 200) { throw "Health check failed on port $McpPort." }
    Write-Host "MCP host already listening on http://127.0.0.1:$McpPort"
    $server = $null
} catch {
    Write-Host "Starting LmsCompanion.Mcp on port $McpPort..."
    $server = Start-Process -FilePath $dotnet -ArgumentList @(
        'run', '--project', "`"$mcpProject`"", '--urls', "http://127.0.0.1:$McpPort"
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr
    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($server.HasExited) { throw 'MCP host exited. Check .local\live-mcp.err.log.' }
        try {
            $probe = Invoke-WebRequest -Uri "http://127.0.0.1:$McpPort/health" -TimeoutSec 2 -UseBasicParsing
            if ($probe.StatusCode -eq 200) { $healthy = $true; break }
        } catch { }
    }
    if (-not $healthy) { throw 'MCP host did not become healthy.' }
}

$tunnel = Start-Process -FilePath $cloudflared -ArgumentList @(
    'tunnel', '--url', "http://127.0.0.1:$McpPort", '--no-autoupdate'
) -WindowStyle Hidden -PassThru -RedirectStandardOutput $tunnelOut -RedirectStandardError $tunnelErr

$baseUrl = $null
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    Start-Sleep -Seconds 1
    if ($tunnel.HasExited) { throw 'Cloudflare tunnel exited. Check .local\live-mcp-tunnel.err.log.' }
    $log = if (Test-Path -LiteralPath $tunnelErr) { Get-Content -LiteralPath $tunnelErr -Raw } else { '' }
    if ($log -match 'https://[a-z0-9-]+\.trycloudflare\.com') {
        $baseUrl = $Matches[0]
        break
    }
}
if (-not $baseUrl) { throw 'Cloudflare did not provide a tunnel URL.' }

$mcpUrl = "$baseUrl/mcp"
Set-Content -LiteralPath (Join-Path $stateDir 'live-mcp-public-url.txt') -Value $mcpUrl -NoNewline

if (-not $SkipPluginUpdate) {
    $mcpConfig = @{
        mcpServers = @{
            'LMS Companion Canvas' = @{
                type = 'http'
                url  = $mcpUrl
            }
        }
    } | ConvertTo-Json -Depth 5
    Set-Content -LiteralPath $pluginMcpJson -Value $mcpConfig
}

$mcpUrl | Set-Clipboard
Write-Host ''
Write-Host 'Public MCP connector URL (copied to clipboard):'
Write-Host "  $mcpUrl"
Write-Host ''
Write-Host 'Before Claude can sign in, register this exact URL in Auth0 as an API identifier and set the MCP host audience to match:'
Write-Host "  Authentication__Audience=$mcpUrl"
Write-Host "  Mcp__PublicResourceUri=$mcpUrl"
Write-Host ''
Write-Host 'Restart LmsCompanion.Mcp with those environment variables, then add the connector in Claude (Sign in now, Streamable HTTP).'
Write-Host 'Connect Stanford SEED in the portal with your personal Canvas token before asking for courses.'
if ($server) { Write-Host "MCP host PID: $($server.Id)" }
Write-Host "Tunnel PID: $($tunnel.Id)"
Write-Host 'Stop the tunnel and MCP process when finished.'
