# Measure overlay window rect of running TaskbarChinaClock + native clock element rect. ASCII only.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WinEnum {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lparam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$p = Get-Process TaskbarChinaClock -ErrorAction SilentlyContinue
if (-not $p) { Write-Host 'widget not running'; exit 1 }
$targetPid = [uint32]$p.Id
Write-Host ("widget PID: " + $targetPid)

$cb = [WinEnum+EnumProc]{
    param($h, $l)
    [uint32]$pid2 = 0
    [WinEnum]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
    if ($pid2 -eq $targetPid -and [WinEnum]::IsWindowVisible($h)) {
        $r = New-Object WinEnum+RECT
        [WinEnum]::GetWindowRect($h, [ref]$r) | Out-Null
        $sb = New-Object System.Text.StringBuilder 256
        [WinEnum]::GetWindowText($h, $sb, 256) | Out-Null
        Write-Host ("overlay hwnd=" + $h + " title=[" + $sb.ToString() + "] rect L=" + $r.Left + " T=" + $r.Top + " R=" + $r.Right + " B=" + $r.Bottom + " W=" + ($r.Right - $r.Left) + " H=" + ($r.Bottom - $r.Top))
    }
    return $true
}
[WinEnum]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

# native clock element (physical coords)
$el = [Windows.Automation.AutomationElement]::RootElement.FindFirst(
    [Windows.Automation.TreeScope]::Children,
    (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, '任务栏')))
if (-not $el) {
    # fallback: enumerate desktop children and list them
    $kids = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)
    foreach ($k in $kids) { Write-Host ("desktop child: [" + $k.Current.Name + "] class=[" + $k.Current.ClassName + "]") }
} else {
    $clock = $el.FindFirst([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ClassNameProperty, 'SystemTray.OmniButtonLeft')))
    if ($clock) {
        $b = $clock.Current.BoundingRectangle
        Write-Host ("native clock rect L=" + [int]$b.X + " T=" + [int]$b.Y + " R=" + [int]($b.X + $b.Width) + " B=" + [int]($b.Y + $b.Height) + " W=" + [int]$b.Width)
    } else { Write-Host 'native clock element not found' }
}
