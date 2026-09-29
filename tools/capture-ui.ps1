Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W32b {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
}
'@
[W32b]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing, System.Windows.Forms

function Save-Shot([int]$x, [int]$y, [int]$w, [int]$h, [string]$path) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

# 1) launch app
Start-Process 'C:\Users\moham\.zcode\workspace\default\LiveTypeBridge\publish\LiveTypeBridge.exe'
Start-Sleep -Seconds 7

$p = Get-Process LiveTypeBridge -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[W32b]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Seconds 2

# 2) main window shot (DPI-correct physical pixels)
$r = New-Object W32b+R
[W32b]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.Rt - $r.L; $h = $r.B - $r.T
Write-Output "MAIN RECT: $($r.L),$($r.T) ${w}x${h}"
Save-Shot $r.L $r.T $w $h "$env:TEMP\ltb-main.png"

# 3) trigger the QR window via the global hotkey Ctrl+Alt+Shift+Q
[System.Windows.Forms.SendKeys]::SendWait('^%+q')
Start-Sleep -Seconds 4

# 4) full screen shot (QR window + main window together)
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Output "SCREEN: $($b.Width)x$($b.Height)"
Save-Shot $b.X $b.Y $b.Width $b.Height "$env:TEMP\ltb-qr.png"
Write-Output 'DONE'
