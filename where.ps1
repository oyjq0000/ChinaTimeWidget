$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public struct RECTW { public int L, T, R, B; }
public class Where {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECTW r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    public delegate bool EnumProc(IntPtr h, IntPtr lp);
}
'@
$target = (Get-Process TaskbarChinaClock | Select-Object -First 1).Id
$cb = {
    param($h, $lp)
    [uint32]$p = 0
    [Where]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
    if ($p -eq $target) {
        $sb = New-Object System.Text.StringBuilder 128
        [Where]::GetClassName($h, $sb, 128) | Out-Null
        $r = New-Object RECTW
        [Where]::GetWindowRect($h, [ref]$r) | Out-Null
        $vis = [Where]::IsWindowVisible($h)
        $par = [Where]::GetParent($h)
        $sty = [Where]::GetWindowLong($h, -16)
        $ex = [Where]::GetWindowLong($h, -20)
        "hwnd=0x{0:X} class={1} visible={2} rect=({3},{4})-({5},{6}) parent=0x{7:X} style=0x{8:X} exstyle=0x{9:X}" -f $h.ToInt64(), $sb.ToString(), $vis, $r.L, $r.T, $r.R, $r.B, $par.ToInt64(), $sty, $ex
    }
    return $true
}
[Where]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
"scan done"
