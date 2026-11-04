# 开源文档截图：时钟 / 面板(浅/深) / 年月选择器 / 近期日程 / 日程编辑器
# 全部使用 --xxx-demo 免鼠标入口；紧贴窗口矩形裁剪，避免桌面背景泄漏
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
  public struct RECT { public int L, T, R, B; }
}
'@
[W]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$dir = Join-Path $PSScriptRoot 'docs'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

function Shot-Rect([W+RECT]$r, [string]$name, [int]$pad = 1) {
  $scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $x = [Math]::Max(0, $r.L - $pad); $y = [Math]::Max(0, $r.T - $pad)
  $w2 = [Math]::Min($scr.Width - $x, ($r.R - $r.L) + $pad * 2)
  $h2 = [Math]::Min($scr.Height - $y, ($r.B - $r.T) + $pad * 2)
  $bmp = New-Object System.Drawing.Bitmap($w2, $h2)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w2, $h2)))
  $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "saved: $name ($w2 x $h2)"
}

function Find-Window([uint32]$procId, [bool]$big) {
  $script:procId = $procId
  foreach ($i in 1..25) {
    Start-Sleep -Milliseconds 200
    $script:hit = [IntPtr]::Zero
    $cb = [W+EnumProc]{
      param($h, $l)
      $pid2 = 0
      [W]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
      if ($pid2 -eq $script:procId -and [W]::IsWindowVisible($h)) {
        $r = New-Object W+RECT
        [W]::GetWindowRect($h, [ref]$r) | Out-Null
        $ww = $r.R - $r.L; $hh = $r.B - $r.T
        if ($big -and $ww -gt 300 -and $hh -gt 400) { $script:hit = $h; return $false }
        if (-not $big -and $ww -ge 330 -and $ww -le 380 -and $hh -ge 250 -and $hh -le 300) { $script:hit = $h; return $false }
      }
      return $true
    }
    [W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    if ($script:hit -ne [IntPtr]::Zero) { return $script:hit }
  }
  throw 'window not found'
}

function Launch([string]$args1) {
  Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 400
  return (Start-Process -FilePath (Join-Path $PSScriptRoot 'TaskbarChinaClock.exe') -ArgumentList $args1 -PassThru)
}

# 1. 任务栏时钟（右下角紧裁，只含时钟覆盖区）
Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Start-Process -FilePath (Join-Path $PSScriptRoot 'TaskbarChinaClock.exe') | Out-Null
Start-Sleep -Milliseconds 2500
$scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap(230, 64)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($scr.Width - 230, $scr.Height - 64, 0, 0, (New-Object System.Drawing.Size(230, 64)))
$bmp.Save((Join-Path $dir 'clock.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output 'saved: clock.png'

# 2. 面板（浅色）
$pr = Launch '--show-panel'
$hw = Find-Window $pr.Id $true
$r = New-Object W+RECT
[W]::GetWindowRect($hw, [ref]$r) | Out-Null
Start-Sleep -Milliseconds 600
Shot-Rect $r 'panel-light.png' 1

# 3. 面板（深色）
Set-Content -Path (Join-Path $env:APPDATA 'ChinaClock\settings.ini') -Value @('ThemeMode=2','Accent=#4472C4') -Encoding UTF8
$pr = Launch '--show-panel'
$hw = Find-Window $pr.Id $true
$r = New-Object W+RECT
[W]::GetWindowRect($hw, [ref]$r) | Out-Null
Start-Sleep -Milliseconds 600
Shot-Rect $r 'panel-dark.png' 1

# 4. 年月选择器（浅色）
Set-Content -Path (Join-Path $env:APPDATA 'ChinaClock\settings.ini') -Value @('ThemeMode=1','Accent=#4472C4') -Encoding UTF8
$pr = Launch '--picker-demo'
$hw = Find-Window $pr.Id $true
$r = New-Object W+RECT
[W]::GetWindowRect($hw, [ref]$r) | Out-Null
Start-Sleep -Milliseconds 600
Shot-Rect $r 'picker.png' 1

# 5. 近期日程列表
$pr = Launch '--list-demo'
$hw = Find-Window $pr.Id $true
$r = New-Object W+RECT
[W]::GetWindowRect($hw, [ref]$r) | Out-Null
Start-Sleep -Milliseconds 600
Shot-Rect $r 'agenda-list.png' 1

# 6. 日程编辑器（预置一条示例日程）
Set-Content -Path (Join-Path $env:APPDATA 'ChinaClock\agenda.txt') -Value @('2026-11-04|提交周报','2026-11-04|15:00 例会') -Encoding UTF8
$pr = Launch '--agenda-demo'
$hw2 = Find-Window $pr.Id $false   # 编辑器窗口 344x264
$r = New-Object W+RECT
[W]::GetWindowRect($hw2, [ref]$r) | Out-Null
Start-Sleep -Milliseconds 400
Shot-Rect $r 'agenda-editor.png' 2

# 还原默认配置、清理演示日程
Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Remove-Item (Join-Path $env:APPDATA 'ChinaClock\settings.ini') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'ChinaClock\agenda.txt') -Force -ErrorAction SilentlyContinue
Write-Output 'done'
