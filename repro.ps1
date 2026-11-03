$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public struct PTRR { public int X, Y; }
public class MC {
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PTRR p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    public static void ClickAt(int x, int y) {
        uint sx = (uint)GetSystemMetrics(0), sy = (uint)GetSystemMetrics(1);
        mouse_event(0x8000 | 0x0001, (uint)(x * 65535 / (sx - 1)), (uint)(y * 65535 / (sy - 1)), 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
    public static uint WhoAt(int x, int y) {
        PTRR p; p.X = x; p.Y = y;
        uint pid = 0;
        GetWindowThreadProcessId(WindowFromPoint(p), out pid);
        return pid;
    }
}
'@
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$target = (Get-Process TaskbarChinaClock | Select-Object -First 1).Id
function CapClock($path) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $w = 380; $h = 90
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Width - $w, $b.Height - $h, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
[MC]::ClickAt(900, 500)
Start-Sleep -Milliseconds 600
[MC]::ClickAt(1300, 1056)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$covered = 0
while ($sw.ElapsedMilliseconds -lt 1500) {
    if ([MC]::WhoAt(1850, 1056) -ne $target) { $covered++; "t=$($sw.ElapsedMilliseconds)ms covered by pid=$([MC]::WhoAt(1850, 1056))" }
    Start-Sleep -Milliseconds 25
}
CapClock (Join-Path $PSScriptRoot 'r4.png')
"result: covered_ticks=$covered / 60 (0 = perfect)"
