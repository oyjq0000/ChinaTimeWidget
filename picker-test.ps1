# 年月选择器交互测试：启动面板 → 点底部年月标签 → 选月视图 → 点头部 → 选年视图 → 选年 → 选月
# 用法：powershell -File picker-test.ps1   （测试结束后新实例保持运行）
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

# ---- 启动 ----
Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$p = Start-Process -FilePath (Join-Path $dir 'TaskbarChinaClock.exe') -ArgumentList '--show-panel' -PassThru
Start-Sleep -Milliseconds 600

# ---- 找面板窗口（该进程中可见且宽>300 高>400 的顶层窗口）----
function Find-Panel {
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
  $script:targetPid = $p.Id; $script:hit = [IntPtr]::Zero
  [W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
  return $script:hit
}

$hw = [IntPtr]::Zero
foreach ($i in 1..25) { $hw = Find-Panel; if ($hw -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 200 }
if ($hw -eq [IntPtr]::Zero) { throw 'panel window not found' }

function Get-Rect {
  $r = New-Object W+RECT
  [W]::GetWindowRect($hw, [ref]$r) | Out-Null
  return $r
}

# 面板内部逻辑坐标（96DPI 基准）：宽434。头部高度为动态测量值，故纵向改用"距面板底部"定位：
# 底边距8 + 底部40 + 网格360 + 星期28 = 网格底缘距底48；标题中心距底452；
# 选择器区高388，选年 2027 格中心距底114，选月 1月 格中心距底372
function Click-Logical([double]$lx, [double]$lyFromBottom) {
  $r = Get-Rect
  $w = $r.R - $r.L; $h = $r.B - $r.T
  $s = $h / 575.0
  $x = $r.L + [int][Math]::Round($lx * $w / 434.0)
  $y = $r.B - [int][Math]::Round($lyFromBottom * $s)
  [W]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 80
  [W]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)  # left down
  Start-Sleep -Milliseconds 60
  [W]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)  # left up
  Start-Sleep -Milliseconds 550                     # 间隔超过系统双击时限，避免被合并为双击
}

function Shot([string]$name) {
  Start-Sleep -Milliseconds 400
  $r = Get-Rect
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

Shot 'pick-1-monthview.png'          # 月历 + 标题栏（未进入选择器）
Click-Logical 217 452                # 点标题"2026年11月" → 选月视图
Shot 'pick-2-monthpick.png'
Click-Logical 150 510                # 点头部信息区（选择器以外）→ 应失焦收起回月历
Shot 'pick-3-dismiss.png'
Click-Logical 217 452                # 点标题 → 选月视图
Click-Logical 217 452                # 再点标题 → 选年视图（十年区）
Shot 'pick-4-yearpick.png'
Click-Logical 64 114                 # 十年区第 2 行首格 → 该年选月
Click-Logical 64 372                 # 选月视图首格（1月）→ 跳转
Shot 'pick-5-aftermonth.png'
Write-Output 'done'
