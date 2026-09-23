# M002/S01/T05 instrument: observe the sample process from outside while it runs the toast demo.
#
# The slice's exit condition includes "the process stays alive with no window". That is a claim about
# another process, so it is measured from here: every second this enumerates the matching processes,
# their top-level windows (total and visible) and their thread/handle counts, and prints one line.
# A run whose sample has zero visible top-level windows for its whole lifetime is a windowless
# process; one that dies early shows up as the process disappearing.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/observe-sample.ps1 -Seconds 32
param(
    [int]$Seconds = 32,
    [string]$ProcessName = 'Trustsoft.NotifyIcon.Sample'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class SampleWindows {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static string Describe(uint targetPid) {
        int total = 0; int visible = 0; string visibleClasses = "";
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid) return true;
            total++;
            if (IsWindowVisible(h)) {
                visible++;
                StringBuilder c = new StringBuilder(256);
                GetClassName(h, c, 256);
                RECT r; GetWindowRect(h, out r);
                visibleClasses += "[" + c.ToString() + " " + (r.R - r.L) + "x" + (r.B - r.T) + "]";
            }
            return true;
        }, IntPtr.Zero);
        return "windows=" + total + " visible=" + visible + (visibleClasses.Length > 0 ? " " + visibleClasses : "");
    }
}
'@

$start = Get-Date
Write-Output ("observer: start {0:HH:mm:ss.fff} watching process name '{1}' for {2}s" -f $start, $ProcessName, $Seconds)

while (((Get-Date) - $start).TotalSeconds -lt $Seconds) {
    $procs = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    $elapsed = [int]((Get-Date) - $start).TotalSeconds

    if ($procs.Count -eq 0) {
        Write-Output ("observer: t+{0}s no process named '{1}' is running" -f $elapsed, $ProcessName)
    } else {
        foreach ($p in $procs) {
            Write-Output ("observer: t+{0}s pid={1} alive=True workingSet={2}MB {3}" -f $elapsed, $p.Id, [int]($p.WorkingSet64 / 1MB), [SampleWindows]::Describe([uint32]$p.Id))
        }
    }

    Start-Sleep -Milliseconds 1000
}

Write-Output ("observer: complete at {0:HH:mm:ss.fff}" -f (Get-Date))
