. (Join-Path $PSScriptRoot 'ui-automation.ps1')

Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class K{
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h,EnumProc cb,IntPtr p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RR r);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h,int m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h,int m,IntPtr w,IntPtr l);
 public delegate bool EnumProc(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct RR{public int L,T,Rt,B;}
}
"@ -ErrorAction SilentlyContinue

function Get-Kids($h) {
    $script:kids = @()
    $cb = [K+EnumProc]{
        param($c, $p)
        $cn = New-Object System.Text.StringBuilder 256; [void][K]::GetClassNameW($c, $cn, 256)
        $tx = New-Object System.Text.StringBuilder 256; [void][K]::GetWindowTextW($c, $tx, 256)
        $r = New-Object K+RR; [void][K]::GetWindowRect($c, [ref]$r)
        $script:kids += [pscustomobject]@{
            H = $c; Class = $cn.ToString(); Text = $tx.ToString()
            X = $r.L; Y = $r.T; W = $r.Rt - $r.L; Ht = $r.B - $r.T
        }
        return $true
    }
    [void][K]::EnumChildWindows($h, $cb, [IntPtr]::Zero)
    return $script:kids
}

# BM_CLICK - goes straight to the control, no cursor needed
function Click-Button($h) { [void][K]::SendMessageW($h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }

function Find-Button($win, $text) {
    (Get-Kids $win | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $text } | Select-Object -First 1)
}

function App-Window($like) {
    $w = Get-AppWindows | Where-Object { $_.Title -like $like }
    if ($w) { return @($w)[0] }
    return $null
}

function Start-App {
    Get-Process MawaqitAdhan -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
    Start-Process -FilePath (Join-Path $PSScriptRoot '../src/MawaqitAdhan/bin/Release/net48/MawaqitAdhan.exe') -WindowStyle Hidden
    Start-Sleep -Seconds 5
}
