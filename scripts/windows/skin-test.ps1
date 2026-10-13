# Turns "skin test mode" on or off for your normal CS2 install.
#
#   on   - adds Metamod to gameinfo.gi, starts a local LAN server and starts CS2 with -insecure
#   off  - removes Metamod from gameinfo.gi so CS2 is back to normal (do this before playing online!)
#
# Usage: powershell -ExecutionPolicy Bypass -File skin-test.ps1 on|off [-GamePath "D:\...\Counter-Strike Global Offensive"]
param(
    [Parameter(Mandatory = $true)][ValidateSet('on', 'off')][string]$Mode,
    [string]$GamePath,
    [string]$Map = 'de_dust2'
)

$ErrorActionPreference = 'Stop'
$MetamodLine = 'Game' + "`t" + 'csgo/addons/metamod'

function Find-Cs2 {
    $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }

    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $libraries += $m.Groups[1].Value -replace '\\\\', '\'
        }
    }

    foreach ($lib in $libraries | Select-Object -Unique) {
        $candidate = Join-Path $lib 'steamapps\common\Counter-Strike Global Offensive'
        if (Test-Path (Join-Path $candidate 'game\csgo\gameinfo.gi')) { return $candidate }
    }
    return $null
}

if (-not $GamePath) { $GamePath = Find-Cs2 }
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath 'game\csgo\gameinfo.gi'))) {
    Write-Host 'Could not find CS2. Run again with -GamePath "<...>\Counter-Strike Global Offensive"' -ForegroundColor Red
    exit 1
}

$csgo = Join-Path $GamePath 'game\csgo'
$gameinfo = Join-Path $csgo 'gameinfo.gi'
$exe = Join-Path $GamePath 'game\bin\win64\cs2.exe'
Write-Host "CS2 found: $GamePath"

if (Get-Process -Name cs2 -ErrorAction SilentlyContinue) {
    Write-Host 'Close CS2 (and any CS2 server window) first, then run this again.' -ForegroundColor Red
    exit 1
}

$lines = [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($gameinfo))
$isMetamodLine = { param($l) $l -match '^\s*Game\s+csgo/addons/metamod\s*$' }

if ($Mode -eq 'off') {
    $removed = $lines.RemoveAll([Predicate[string]] { param($l) & $isMetamodLine $l })
    if ($removed -gt 0) { [System.IO.File]::WriteAllLines($gameinfo, $lines) }
    Write-Host 'Skin test mode is OFF. CS2 is back to normal - you can play online again.' -ForegroundColor Green
    Write-Host '(Remove -insecure from your Steam launch options if you added it yourself.)'
    exit 0
}

# --- on ---
if (-not (Test-Path (Join-Path $csgo 'addons\metamod'))) {
    Write-Host "Metamod is not installed in $csgo\addons - see the README (step 1 and 2)." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path (Join-Path $csgo 'addons\counterstrikesharp\plugins\LanSkinTester\LanSkinTester.dll'))) {
    Write-Host 'LanSkinTester is not installed - extract LanSkinTester.zip into game\csgo first.' -ForegroundColor Red
    exit 1
}

if (-not ($lines | Where-Object { & $isMetamodLine $_ })) {
    $index = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^(\s*)Game\s+csgo\s*$') { $index = $i; $indent = $Matches[1]; break }
    }
    if ($index -lt 0) {
        Write-Host 'Unexpected gameinfo.gi format: no "Game csgo" line found. Nothing was changed.' -ForegroundColor Red
        exit 1
    }

    $backup = "$gameinfo.backup"
    Copy-Item $gameinfo $backup -Force
    $lines.Insert($index, $indent + $MetamodLine)
    [System.IO.File]::WriteAllLines($gameinfo, $lines)
    Write-Host "Added Metamod to gameinfo.gi (backup: $backup)"
}

# CounterStrikeSharp's FollowCS2ServerGuidelines (on by default) blocks skins - only the knife model would change.
$coreJson = Join-Path $csgo 'addons\counterstrikesharp\configs\core.json'
if (-not (Test-Path $coreJson)) {
    Set-Content -Path $coreJson -Value '{ "FollowCS2ServerGuidelines": false }' -Encoding ASCII
    Write-Host 'Created core.json with FollowCS2ServerGuidelines = false'
} else {
    $text = Get-Content $coreJson -Raw
    if ($text -match '"FollowCS2ServerGuidelines"\s*:\s*true') {
        $text = $text -replace '("FollowCS2ServerGuidelines"\s*:\s*)true', '${1}false'
        [System.IO.File]::WriteAllText($coreJson, $text)
        Write-Host 'Set FollowCS2ServerGuidelines = false in core.json'
    } elseif ($text -notmatch '"FollowCS2ServerGuidelines"\s*:\s*false') {
        Write-Host 'Add  "FollowCS2ServerGuidelines": false  to core.json or skins will not apply.' -ForegroundColor Yellow
    }
}

Write-Host 'Starting local LAN server...'
Start-Process -FilePath $exe -ArgumentList @('-dedicated', '-insecure', '+sv_lan', '1', '+map', $Map, '+game_type', '0', '+game_mode', '1')

Write-Host 'Starting CS2 with -insecure (VAC off, so you cannot join official/VAC servers in this mode)...'
Start-Process -FilePath $exe -ArgumentList @('-insecure', '+connect', 'localhost')

Write-Host ''
Write-Host 'Skin test mode is ON.' -ForegroundColor Yellow
Write-Host 'When the server has finished loading, CS2 connects by itself (if not: open the console and type  connect localhost).'
Write-Host 'Type !skinhelp in chat.'
Write-Host ''
Write-Host 'IMPORTANT: when you are done, close CS2 and the server, then run skin-test-OFF.bat' -ForegroundColor Yellow
Write-Host 'before you play matchmaking / FACEIT / any online server.' -ForegroundColor Yellow
