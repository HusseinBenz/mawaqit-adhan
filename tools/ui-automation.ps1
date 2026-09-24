Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class U{
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr hdc,uint f);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref POINT p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint cmd);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb,IntPtr p);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 public delegate bool EnumProc(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct RECT{public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)] public struct POINT{public int X,Y;}
}
"@
[void][U]::SetProcessDPIAware()

function Get-AppWindows {
    $proc = Get-Process MawaqitAdhan -ErrorAction SilentlyContinue
    if (-not $proc) { return @() }
    $found = @()
    $cb = [U+EnumProc]{
        param($h, $p)
        $pid2 = 0
        [void][U]::GetWindowThreadProcessId($h, [ref]$pid2)
        if ($pid2 -eq $proc.Id -and [U]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            [void][U]::GetWindowTextW($h, $sb, 256)
            $t = $sb.ToString()
            if ($t) { $script:found += [pscustomobject]@{ Handle = $h; Title = $t } }
        }
        return $true
    }
    $script:found = @()
    [void][U]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:found
}

function Save-Window($h, $path) {
    $r = New-Object U+RECT
    [void][U]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { return "no window" }
    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc(); [void][U]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return "$path  ($w x $ht)"
}

function Click-Client($h, $x, $y) {
    [void][U]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 250
    $p = New-Object U+POINT; $p.X = $x; $p.Y = $y
    [void][U]::ClientToScreen($h, [ref]$p)
    [void][U]::SetCursorPos($p.X, $p.Y)
    Start-Sleep -Milliseconds 120
    [U]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [U]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}
