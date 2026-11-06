# Reflection test for F3 main-timezone feature: MainClock resolution + LocalClock.Distinct.
# ASCII only (Windows PowerShell 5.1 reads BOM-less scripts as ANSI).
# NOTE: never touches the real %APPDATA% settings.ini - FilePath is redirected to a temp file.
# Build the library first (repo root):
#   csc -nologo -target:library -out:"$env:TEMP\ctz-test\ChinaClockLib.dll" ^
#     -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
#     -r:"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll" ^
#     -r:"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll" ^
#     -r:"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll" ^
#     TaskbarChinaClock.cs
$ErrorActionPreference = 'Stop'

$a = [Reflection.Assembly]::LoadFrom("$env:TEMP\ctz-test\ChinaClockLib.dll")

function Get-Static([type]$t, [string]$name) {
    $b = [Reflection.BindingFlags]'Public,Static'
    $p = $t.GetProperty($name, $b)
    if ($p) { return $p.GetValue($null, $null) }
    return $t.GetField($name, $b).GetValue($null)
}
function Set-Static([type]$t, [string]$name, $value) {
    $b = [Reflection.BindingFlags]'Public,Static'
    $p = $t.GetProperty($name, $b)
    if ($p -and $p.CanWrite) { $p.SetValue($null, $value, $null); return }
    $t.GetField($name, $b).SetValue($null, $value)
}

$fail = 0
function Assert-Eq([string]$name, $actual, $expected) {
    if ("$actual" -eq "$expected") { Write-Host "[PASS] $name = $actual" }
    else { Write-Host "[FAIL] $name expected=$expected actual=$actual"; $script:fail++ }
}

$settings  = $a.GetType('ChinaClock.Settings')
$mainClock = $a.GetType('ChinaClock.MainClock')
$localClock = $a.GetType('ChinaClock.LocalClock')
$localId = [TimeZoneInfo]::Local.Id   # Pacific Standard Time (UTC-8 winter) on this machine

# redirect ini writes to a temp file (protect the user's real settings)
$tmpIni = Join-Path ([IO.Path]::GetTempPath()) ("ChinaClock-test-" + [Guid]::NewGuid().ToString("N") + "\settings.ini")
Set-Static $settings 'FilePath' $tmpIni

# --- MainClock.Zone resolution ---
Set-Static $settings 'MainTzId' 'China Standard Time'
Assert-Eq 'zone default Beijing' (Get-Static $mainClock 'Zone').Id 'China Standard Time'
Assert-Eq 'now year sane' (Get-Static $mainClock 'Now').Year ([DateTime]::UtcNow.ToLocalTime().Year)

Set-Static $settings 'MainTzId' 'Pacific Standard Time'
Assert-Eq 'zone explicit local id' (Get-Static $mainClock 'Zone').Id $localId
Assert-Eq 'Distinct main=local false' (Get-Static $localClock 'Distinct') 'False'

Set-Static $settings 'MainTzId' 'Local'
Assert-Eq 'zone Local marker' (Get-Static $mainClock 'Zone').Id $localId
Assert-Eq 'Distinct Local false' (Get-Static $localClock 'Distinct') 'False'

# same UTC offset as local (both UTC-8 in November), different Id -> hidden
Set-Static $settings 'MainTzId' 'Pacific Standard Time (Mexico)'
Assert-Eq 'zone Mexico id kept' (Get-Static $mainClock 'Zone').Id 'Pacific Standard Time (Mexico)'
Assert-Eq 'Distinct same-offset-diff-id false' (Get-Static $localClock 'Distinct') 'False'

Set-Static $settings 'MainTzId' 'Tokyo Standard Time'
Assert-Eq 'zone Tokyo' (Get-Static $mainClock 'Zone').Id 'Tokyo Standard Time'
Assert-Eq 'Distinct Tokyo true' (Get-Static $localClock 'Distinct') 'True'

Set-Static $settings 'MainTzId' 'Bogus/Zone'
Assert-Eq 'zone invalid falls back Beijing' (Get-Static $mainClock 'Zone').Id 'China Standard Time'
Assert-Eq 'Distinct fallback true' (Get-Static $localClock 'Distinct') 'True'

# --- Settings ini round-trip (temp file) ---
Set-Static $settings 'MainTzId' 'Tokyo Standard Time'
$settings.GetMethod('Save', [Reflection.BindingFlags]'Public,Static').Invoke($null, $null)
Set-Static $settings 'MainTzId' 'China Standard Time'
$settings.GetMethod('Load', [Reflection.BindingFlags]'Public,Static').Invoke($null, $null)
Assert-Eq 'ini round-trip MainTzId' (Get-Static $settings 'MainTzId') 'Tokyo Standard Time'

# restore defaults in the temp file, then clean up
Set-Static $settings 'MainTzId' 'China Standard Time'
$settings.GetMethod('Save', [Reflection.BindingFlags]'Public,Static').Invoke($null, $null)
Remove-Item (Split-Path $tmpIni) -Recurse -Force

if ($fail -eq 0) { Write-Host 'RESULT: ALL PASS' } else { Write-Host "RESULT: $fail FAILED"; exit 1 }
