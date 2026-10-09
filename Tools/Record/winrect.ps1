Add-Type @"
using System; using System.Runtime.InteropServices;
public class W { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref P p);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; } [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; } }
"@
$p = Get-Process Photosynthesis -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($p) { $c = New-Object W+R; [W]::GetClientRect($p.MainWindowHandle, [ref]$c) | Out-Null; $o = New-Object W+P; [W]::ClientToScreen($p.MainWindowHandle, [ref]$o) | Out-Null
  "client=$($c.Rt)x$($c.B) at=$($o.X),$($o.Y)" } else { "no window" }
