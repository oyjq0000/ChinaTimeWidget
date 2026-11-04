# 第三波验证：N2 近期日程列表 + N1 当日提醒气泡（--list-demo，全程无鼠标操作）
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[W]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$dir = $PSScriptRoot

# 日程种子：今天两条（一条带时间）、明天一条、公历每年、农历每年（今天=农历九月廿六）、13 天后一条
$cfg = Join-Path $env:APPDATA 'ChinaClock\agenda.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
Set-Content -Path $cfg -Value @(
  '2026-11-04|提交周报',
  '2026-11-04|15:00 例会',
  '2026-11-05|看牙医',
  '每年 11-12|结婚纪念日',
  '农历 09-26|妈妈生日(农历)',
  '2026-11-15|季度评审'
) -Encoding UTF8

Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$p = Start-Process -FilePath (Join-Path $dir 'TaskbarChinaClock.exe') -ArgumentList '--list-demo' -PassThru

function Shot([string]$name, [int]$w, [int]$h, [int]$up) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Width - $w, $b.Height - $h - $up, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "saved: $name"
}

Start-Sleep -Milliseconds 2500
Shot 'n2-list.png' 620 720 0      # 列表视图（面板 + 近期日程）

# N1：15s 定时器触发"今日日程"气泡，连拍三张抓 toast
Start-Sleep -Milliseconds 13500
Shot 'n1-toast-1.png' 560 400 0
Start-Sleep -Milliseconds 2500
Shot 'n1-toast-2.png' 560 400 0
Start-Sleep -Milliseconds 2500
Shot 'n1-toast-3.png' 560 400 0

Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output 'done'
