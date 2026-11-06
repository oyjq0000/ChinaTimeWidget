# Probe clock rects on this machine: Win32 TrayClockWClass vs UIA time-named elements. ASCII only.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Probe {
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$tray = [Probe]::FindWindow('Shell_TrayWnd', $null)
Write-Host ("Shell_TrayWnd: " + $tray)
$notify = [Probe]::FindWindowEx($tray, [IntPtr]::Zero, 'TrayNotifyWnd', $null)
Write-Host ("TrayNotifyWnd: " + $notify)
$clock = [Probe]::FindWindowEx($notify, [IntPtr]::Zero, 'TrayClockWClass', $null)
Write-Host ("TrayClockWClass: " + $clock)
if ($clock -ne [IntPtr]::Zero) {
    $r = New-Object Probe+RECT
    [Probe]::GetWindowRect($clock, [ref]$r) | Out-Null
    Write-Host ("Win32 clock rect: L=" + $r.Left + " T=" + $r.Top + " R=" + $r.Right + " B=" + $r.Bottom + "  W=" + ($r.Right - $r.Left) + " H=" + ($r.Bottom - $r.Top))
}

# UIA: list elements whose name starts with a time pattern or contains clock keywords, near right edge
$root = [Windows.Automation.AutomationElement]::FromHandle($tray)
$els = $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
Write-Host ("UIA elements total: " + $els.Count)
$rx = New-Object Text.RegularExpressions.Regex('^\s*\d{1,2}:\d{2}')
foreach ($e in $els) {
    try {
        $n = $e.Current.Name
        $b = $e.Current.BoundingRectangle
        if ($b.IsEmpty) { continue }
        if (($n -and ($rx.IsMatch($n) -or $n.Contains(':'))) -or ($e.Current.ClassName -like '*Clock*') -or ($e.Current.ClassName -like '*Tray*')) {
            Write-Host ("UIA hit: name=[" + $n + "] class=[" + $e.Current.ClassName + "] rect L=" + [int]$b.X + " T=" + [int]$b.Y + " R=" + [int]($b.X + $b.Width) + " B=" + [int]($b.Y + $b.Height))
        }
    } catch {}
}
