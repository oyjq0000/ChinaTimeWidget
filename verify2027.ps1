# B1 端到端验证：打开面板 → 标题下钻选年 2027 → 选月 2月 → 截图（应出现在线数据的休/班角标）
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

Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$p = Start-Process -FilePath (Join-Path $dir 'TaskbarChinaClock.exe') -ArgumentList '--show-panel' -PassThru

$script:targetPid = $p.Id
$hw = [IntPtr]::Zero
foreach ($i in 1..25) {
  Start-Sleep -Milliseconds 200
  $script:hit = [IntPtr]::Zero
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
  [W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
  if ($script:hit -ne [IntPtr]::Zero) { $hw = $script:hit; break }
}
if ($hw -eq [IntPtr]::Zero) { throw 'panel not found' }

function Click-Logical([double]$lx, [double]$lyFromBottom) {
  $r = New-Object W+RECT
  [W]::GetWindowRect($hw, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $h = $r.B - $r.T
  $s = $h / 575.0
  $x = $r.L + [int][Math]::Round($lx * $w / 464.0)
  $y = $r.B - [int][Math]::Round($lyFromBottom * $s)
  [W]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 80
  [W]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 60
  [W]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 550
}

function Shot([string]$name) {
  Start-Sleep -Milliseconds 400
  $r = New-Object W+RECT
  [W]::GetWindowRect($hw, [ref]$r) | Out-Null
  $m = 8
  $scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $x = [Math]::Max(0, $r.L - $m); $y = [Math]::Max(0, $r.T - $m)
  $w2 = [Math]::Min($scr.Width - $x, ($r.R - $r.L) + $m * 2)
  $h2 = [Math]::Min($scr.Height - $y, ($r.B - $r.T) + $m * 2)
  $bmp = New-Object System.Drawing.Bitmap($w2, $h2)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w2, $h2)))
  $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "saved: $name"
}

Click-Logical 217 452     # 标题 → 选月 2026
Click-Logical 217 452     # 标题 → 选年 2020-2029
Click-Logical 395 243     # 第 1 行第 3 格 → 2026 年选月
Click-Logical 177 372     # 首行第 2 格 → 2月（2/14 调休补班应显示"班"）
Shot 'b1-2026feb.png'
Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output 'done'
