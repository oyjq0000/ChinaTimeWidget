# Update the deployed copy in %LocalAppData%\ChinaClock and restart the clock process.
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.)
$ErrorActionPreference = 'Stop'

$repoExe   = Join-Path $PSScriptRoot 'TaskbarChinaClock.exe'
$deployExe = Join-Path $env:LOCALAPPDATA 'ChinaClock\TaskbarChinaClock.exe'

# 0) Sanity check: repo exe must contain the new-layout marker (UTF-16LE of U+8FD1 U+671F U+65E5 U+7A0B)
$needle = [byte[]](0xD1,0x8F, 0x1F,0x67, 0xE5,0x65, 0x0B,0x7A)
$raw = [IO.File]::ReadAllBytes($repoExe)
$found = $false
for ($i = 0; $i -le $raw.Length - $needle.Length; $i++) {
    if ($raw[$i] -ne $needle[0]) { continue }
    $ok = $true
    for ($j = 1; $j -lt $needle.Length; $j++) {
        if ($raw[$i + $j] -ne $needle[$j]) { $ok = $false; break }
    }
    if ($ok) { $found = $true; break }
}
Write-Host ("repo exe contains new-layout marker: " + $found)
if (-not $found) { throw 'repo exe does not look like the new version, aborting.' }

# 1) Stop the old process
$p = Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue
if ($p) {
    Write-Host ("stopping old process PID " + $p.Id)
    Stop-Process -Id $p.Id -Force
    Wait-Process -Id $p.Id -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# 2) Overwrite the deployed copy
Copy-Item $repoExe $deployExe -Force
$done = Get-Item $deployExe
Write-Host ("deployed: " + $done.FullName + "  size=" + $done.Length + "  mtime=" + $done.LastWriteTime)

# 3) Start it again
Start-Process $deployExe
Start-Sleep -Seconds 2
$np = Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue
if ($np) {
    Write-Host ("running: PID " + $np.Id + "  start=" + $np.StartTime)
    Write-Host ("path: " + $np.Path)
} else {
    throw 'new process did not start'
}
