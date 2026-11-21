param([Parameter(Mandatory = $true)][string]$ExePath)
$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class EasyColabWindowProbe
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);

    public static string FindVisibleTitle(int pid, string prefix)
    {
        string found = null;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == (uint)pid && IsWindowVisible(h))
            {
                var sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                string title = sb.ToString();
                if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    found = title;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

$crashLog = Join-Path $env:LOCALAPPDATA "EasyColab\crash.log"
$crashBefore = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }

$app = Start-Process -FilePath (Resolve-Path $ExePath).Path -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
        if ($app.HasExited) { throw "EasyColab.exe exited with code $($app.ExitCode)" }
        # Enumerate top-level visible windows directly (Process.MainWindowTitle can pick an IME overlay window on Win11).
        $title = [EasyColabWindowProbe]::FindVisibleTitle($app.Id, "EasyColab")
        if ($title -and $app.Responding) {
            Start-Sleep -Seconds 3
            $app.Refresh()
            if ($app.HasExited) { throw "EasyColab.exe exited shortly after startup (code $($app.ExitCode))" }
            $crashAfter = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }
            if ($crashAfter -gt $crashBefore) {
                Get-Content $crashLog -Tail 40 | Write-Host
                throw "EasyColab logged an unhandled exception during startup"
            }
            Write-Host "Native window opened: $title"
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "EasyColab.exe did not show a main window within 45s"
} finally {
    if (-not $app.HasExited) { taskkill /PID $app.Id /T /F | Out-Null }
}
