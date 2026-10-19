param(
    [string]$Ip,
    [string]$Adb,             # defaults to the adb bundled with this project's Unity version
    [switch]$NonInteractive   # never prompt (used by the Unity Editor hook)
)
if (-not $Adb) {
    # Same adb Unity uses, so the two never restart each other's adb server
    $version = (Select-String -Path "$PSScriptRoot\..\ProjectSettings\ProjectVersion.txt" -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
    $Adb = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
}
if (-not (Test-Path $Adb)) { Write-Host "adb not found: $Adb"; exit 3 }
$adb   = $Adb
$cache = "$env:LOCALAPPDATA\adb-phone-ips.json"
& $adb start-server | Out-Null

function Get-Connected { & $adb devices | Select-String '^\S+\s+device$' }
function Ask([string]$prompt) { if ($NonInteractive) { return $null }; Read-Host $prompt }

# Network name: Wi-Fi SSID if connected, else the Windows profile name of the active connection (e.g. Ethernet)
function Get-NetworkName {
    $wifi = netsh wlan show interfaces | Where-Object { $_ -match '^\s+SSID\s+:\s*(.+)$' } |
        ForEach-Object { $Matches[1].Trim() } | Select-Object -First 1
    if ($wifi) { return $wifi }
    $profile = Get-NetConnectionProfile -ErrorAction SilentlyContinue |
        Where-Object { $_.IPv4Connectivity -in 'Internet', 'LocalNetwork' } | Select-Object -First 1
    if ($profile) { return $profile.Name }
    return 'unknown-network'
}

# Cache: { "<network name>": { "Ip": "...", "Port": 12345, "Updated": "..." } }
$ssid = Get-NetworkName
$nets = @{}
if (Test-Path $cache) {
    (Get-Content $cache -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object {
        # Older cache format stored only the IP string
        if ($_.Value -is [string]) { $nets[$_.Name] = @{ Ip = $_.Value; Port = 0 } }
        else { $nets[$_.Name] = @{ Ip = $_.Value.Ip; Port = [int]$_.Value.Port; Updated = $_.Value.Updated } }
    }
}

function Save-Endpoint([string]$ip, [int]$port) {
    $nets[$ssid] = @{ Ip = $ip; Port = $port; Updated = (Get-Date).ToString('s') }
    $nets | ConvertTo-Json | Set-Content $cache
    Write-Host "Connected on ${ip}:$port (saved for '$ssid')"
    & $adb devices
}

# Already connected (e.g. via Unity or a manual adb connect): just remember where the phone is
$existing = & $adb devices | Select-String '^((?:\d{1,3}\.){3}\d{1,3}):(\d+)\s+device$' | Select-Object -First 1
if ($existing) { Save-Endpoint $existing.Matches[0].Groups[1].Value ([int]$existing.Matches[0].Groups[2].Value); exit 0 }
if (Get-Connected) { & $adb devices; exit 0 }   # USB or mDNS-named device

function Try-Connect([string]$ip, [int]$port) {
    & $adb connect "${ip}:$port" | Out-Null
    if (Get-Connected) { return $true }
    & $adb disconnect "${ip}:$port" | Out-Null
    return $false
}

function Find-AdbPort([string]$ip) {
    Write-Host "Scanning $ip ..."
    $open = @()
    for ($start = 30000; $start -le 65535; $start += 4000) {
        $end = [Math]::Min($start + 3999, 65535)
        $jobs = foreach ($p in $start..$end) {
            $c = New-Object Net.Sockets.TcpClient
            [pscustomobject]@{ Port = $p; Client = $c; Task = $c.ConnectAsync($ip, $p) }
        }
        try { [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]$jobs.Task, 700) | Out-Null } catch {}
        foreach ($j in $jobs) {
            if ($j.Task.Status -eq 'RanToCompletion' -and $j.Client.Connected) { $open += $j.Port }
            $j.Client.Close()
        }
    }
    foreach ($p in $open) { if (Try-Connect $ip $p) { return $p } }
    Write-Host "No adb port found on $ip. Open ports: $($open -join ', ')"
    return 0
}

# 1) mDNS (works on networks that allow multicast)
$m = & $adb mdns services | Select-String '_adb-tls-connect' | Select-Object -First 1
if ($m -and "$m" -match '((?:\d{1,3}\.){3}\d{1,3}):(\d+)') {
    if (Try-Connect $Matches[1] ([int]$Matches[2])) { Save-Endpoint $Matches[1] ([int]$Matches[2]); exit 0 }
}

# 2) Phone IP: argument > cached for this SSID > ask
$saved = $nets[$ssid]
$fromCache = -not $Ip -and $saved
if (-not $Ip -and $saved) { $Ip = $saved.Ip }
if (-not $Ip) { $Ip = Ask "Phone IP for '$ssid' (Wireless debugging screen, IP only)" }
if (-not $Ip) { Write-Host "No saved phone IP for network '$ssid'. Run Connect-Phone.ps1 once from a terminal."; exit 2 }

# 3) Last known port (still valid if Wireless debugging wasn't toggled)
if ($saved -and $saved.Ip -eq $Ip -and $saved.Port -gt 0) {
    Write-Host "Trying last port ${Ip}:$($saved.Port) ..."
    if (Try-Connect $Ip $saved.Port) { Save-Endpoint $Ip $saved.Port; exit 0 }
}

# 4) Scan for the current port
$port = Find-AdbPort $Ip
if ($port) { Save-Endpoint $Ip $port; exit 0 }

# 5) Cached IP may be stale (DHCP gave the phone a new one): ask once and rescan
if ($fromCache) {
    $Ip = Ask "Saved IP may have changed. Phone IP for '$ssid'"
    if ($Ip) {
        $port = Find-AdbPort $Ip
        if ($port) { Save-Endpoint $Ip $port; exit 0 }
    }
}
Write-Host "Could not connect. Is Wireless debugging on, and is the phone on '$ssid'?"
exit 1
