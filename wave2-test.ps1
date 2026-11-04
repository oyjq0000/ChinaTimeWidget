# 第二波交互测试：日程圆点 → 悬停日程提示 → 双击打开日程编辑器 → 设置窗口截图
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr i);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  public struct RECT { public int L, T, R, B; }
}
'@
[W]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$dir = $PSScriptRoot

# 测试设置：浅色 + 红强调色 + 周数列 + 东京第二时钟；并预置日程
$cfg = Join-Path $env:APPDATA 'ChinaClock\settings.ini'
New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
Set-Content -Path $cfg -Value @('ShowSeconds=True','ShowWeekday=True','ShowDateLine=True','CustomTimeFormat=','ShowSecondClock=True','SecondTzId=Tokyo Standard Time','SecondTzLabel=东京','ShowWeekNumbers=True','ThemeMode=1','Accent=#C94F4F','AutoFetchHolidays=True','HolidaysFetchedAt=') -Encoding UTF8
Set-Content -Path (Join-Path $env:APPDATA 'ChinaClock\agenda.txt') -Value @('2026-11-04|提交周报','2026-11-04|15:00 例会','2026-11-11|口径评审') -Encoding UTF8

function Find-BigWindow($procId) {
  $found = [IntPtr]::Zero
  $cb = [W+EnumProc]{
    param($h, $l)
    $pid2 = 0
    [W]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
    if ($pid2 -eq $script:targetPid -and [W]::IsWindowVisible($h)) {
      $r = New-Object W+RECT
      [W]::GetWindowRect($h, [ref]$r) | Out-Null
      if (($r.R - $r.L) -gt 300 -and ($r.B - $r.T) -gt 400) { $script:hit = $h; return $false }
    }
    return $true
  }
  $script:targetPid = $procId; $script:hit = [IntPtr]::Zero
  [W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
  return $script:hit
}

function Wait-Window($procId) {
  foreach ($i in 1..25) {
    $hw = Find-BigWindow $procId
    if ($hw -ne [IntPtr]::Zero) { return $hw }
    Start-Sleep -Milliseconds 200
  }
  throw 'window not found'
}

function Shot-Rect($r, $name) {
  $m = 8
  $scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $x = [Math]::Max(0, $r.L - $m); $y = [Math]::Max(0, $r.T - $m)
  $w2 = [Math]::Min($scr.Width - $x, ($r.R - $r.L) + $m * 2)
  $h2 = [Math]::Min($scr.Height - $y, ($r.B - $r.T) + $m * 2)
  $bmp = New-Object System.Drawing.Bitmap($w2, $h2)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w2, $h2)))
  $out = Join-Path $dir $name
  $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "saved: $name"
}

function Get-Rect($hw) {
  $r = New-Object W+RECT
  [W]::GetWindowRect($hw, [ref]$r) | Out-Null
  return $r
}

# ---- 1. 日历面板：圆点/悬停/双击 ----
Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$p = Start-Process -FilePath (Join-Path $dir 'TaskbarChinaClock.exe') -ArgumentList '--show-panel' -PassThru
Start-Sleep -Milliseconds 600
$hw = Wait-Window $p.Id
$r = Get-Rect $hw
$w = $r.R - $r.L; $h = $r.B - $r.T
# 今天 2026-11-04（周三）在第 1 行第 2 列：格心逻辑坐标(189,285)，面板逻辑尺寸 464x575
$tx = $r.L + [int][Math]::Round(189 * $w / 464.0)
$ty = $r.T + [int][Math]::Round(285 * $h / 575.0)

Shot-Rect $r 'ag-1-dot.png'                                    # 格底圆点
[W]::SetCursorPos($tx, $ty) | Out-Null
Start-Sleep -Milliseconds 1000
Shot-Rect $r 'ag-2-tip.png'                                    # 悬停日程提示
[W]::mouse_event(0x02,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
[W]::mouse_event(0x04,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120
[W]::mouse_event(0x02,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
[W]::mouse_event(0x04,0,0,0,[UIntPtr]::Zero)                   # 双击 → 编辑器
Start-Sleep -Milliseconds 1000
# 编辑器弹出在面板上方，截图区域向上扩展 420px
$r2 = Get-Rect $hw
$m2 = 8
$scr2 = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$x2 = [Math]::Max(0, $r2.L - $m2); $y2 = [Math]::Max(0, $r2.T - 420)
$w3 = [Math]::Min($scr2.Width - $x2, ($r2.R - $r2.L) + $m2 * 2)
$h3 = [Math]::Min($scr2.Height - $y2, ($r2.B - $r2.T) + 420 + $m2 * 2)
$bmp2 = New-Object System.Drawing.Bitmap($w3, $h3)
$g2 = [System.Drawing.Graphics]::FromImage($bmp2)
$g2.CopyFromScreen($x2, $y2, 0, 0, (New-Object System.Drawing.Size($w3, $h3)))
$bmp2.Save((Join-Path $dir 'ag-3-editor.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g2.Dispose(); $bmp2.Dispose()
Write-Output 'saved: ag-3-editor.png'

# ---- 2. 设置窗口 ----
Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$p2 = Start-Process -FilePath (Join-Path $dir 'TaskbarChinaClock.exe') -ArgumentList '--settings' -PassThru
Start-Sleep -Milliseconds 1500
$hw2 = Wait-Window $p2.Id
Shot-Rect (Get-Rect $hw2) 'settings.png'

Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output 'done'
