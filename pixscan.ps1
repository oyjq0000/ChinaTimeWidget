$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Pix2 {
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern int GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT3 r);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public delegate bool EnumProc(IntPtr h, IntPtr lp);
}
public struct RECT3 { public int L, T, R, B; }
'@
$target = (Get-Process TaskbarChinaClock | Select-Object -First 1).Id
$found = $null
$cb = {
    param($h, $lp)
    [uint32]$p = 0
    [Pix2]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
    if ($p -eq $target -and [Pix2]::IsWindowVisible($h)) {
        $r = New-Object RECT3
        [Pix2]::GetWindowRect($h, [ref]$r) | Out-Null
        if (($r.R - $r.L) -gt 30) { $script:found = $r }
    }
    return $true
}
[Pix2]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
$f = $found
"clock window: ($($f.L),$($f.T))-($($f.R),$($f.B))  size $($f.R - $f.L) x $($f.B - $f.T)"
$dc = [Pix2]::GetDC([IntPtr]::Zero)
$yMid = [int](($f.T + $f.B) / 2)
$row = ""
for ($x = $f.L - 60; $x -le [Math]::Min($f.R + 8, 1919); $x += 8) {
    $c = [Pix2]::GetPixel($dc, $x, $yMid)
    $row += ("{0}:#{1:x2}{2:x2}{3:x2}  " -f $x, ($c -band 255), (($c -shr 8) -band 255), (($c -shr 16) -band 255))
}
$row
$col = ""
for ($y = $f.T - 6; $y -le $f.B + 6; $y += 3) {
    $c = [Pix2]::GetPixel($dc, ($f.L - 4), $y)
    $col += ("y{0}:#{1:x2}{2:x2}{3:x2}  " -f $y, ($c -band 255), (($c -shr 8) -band 255), (($c -shr 16) -band 255))
}
$col
$incol = ""
for ($y = $f.T + 2; $y -le $f.B - 2; $y += 8) {
    $c = [Pix2]::GetPixel($dc, ($f.L + 6), $y)
    $incol += ("y{0}:#{1:x2}{2:x2}{3:x2}  " -f $y, ($c -band 255), (($c -shr 8) -band 255), (($c -shr 16) -band 255))
}
"inside-left: " + $incol
[Pix2]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null
