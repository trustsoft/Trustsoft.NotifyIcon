# M002/S01/T05 instrument: click the toast banner's body at its measured position, with attribution.
#
# The earlier pixel-region instrument detected an 800x273 change that was not the banner (its click
# produced no activation), so this one takes the opposite approach: it clicks where Windows actually
# puts a banner - measured from the work area, not from a diff - and proves each click's effect by
# capturing the banner's own rectangle immediately before and after the click. A click that lands on
# the banner makes the shell activate the toast, which removes the banner: the post-click capture
# differs from the pre-click one. A click that misses changes nothing.
#
# Nothing here is assumed about the banner's size: the rectangle is a search window (600x280 above the
# taskbar at the right edge), and the click point is its lower middle, where the body line is.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/click-toast-body.ps1 -DelaySeconds 8 -IntervalSeconds 8 -Clicks 3
param(
    [double]$DelaySeconds = 8,
    [double]$IntervalSeconds = 8,
    [int]$Clicks = 3
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public class BodyClick {
    [StructLayout(LayoutKind.Sequential)] public struct DiffResult {
        public int Changed; public int Left; public int Top; public int Right; public int Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {
        public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint param, out RECT rect, uint flags);

    public static string MakeDpiAware() {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return "PerMonitorV2"; } catch (Exception) { }
        try { if (SetProcessDPIAware()) return "SystemAware (fallback)"; } catch (Exception) { }
        return "unknown";
    }

    public static RECT WorkArea() {
        RECT r;
        // SPI_GETWORKAREA = 0x0030
        SystemParametersInfo(0x0030, 0, out r, 0);
        return r;
    }

    public static uint ClickAt(int x, int y) {
        SetCursorPos(x, y);
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = 0; inputs[0].mi.dx = x; inputs[0].mi.dy = y; inputs[0].mi.dwFlags = 0x0001;
        inputs[1].type = 0; inputs[1].mi.dwFlags = 0x0002 | 0x0004;
        return SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    public static byte[] Capture(int x, int y, int w, int h) {
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb)) {
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] buffer = new byte[Math.Abs(data.Stride) * h];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            bmp.UnlockBits(data);
            return buffer;
        }
    }

    public static DiffResult Diff(byte[] a, byte[] b, int w, int h, int tolerance, int stride) {
        DiffResult r = new DiffResult();
        r.Left = int.MaxValue; r.Top = int.MaxValue; r.Right = int.MinValue; r.Bottom = int.MinValue;
        for (int row = 0; row < h; row++) {
            int offset = row * stride;
            for (int col = 0; col < w; col++) {
                int i = offset + col * 4;
                if (Math.Abs(a[i] - b[i]) > tolerance || Math.Abs(a[i + 1] - b[i + 1]) > tolerance || Math.Abs(a[i + 2] - b[i + 2]) > tolerance) {
                    r.Changed++;
                    if (col < r.Left) r.Left = col;
                    if (col > r.Right) r.Right = col;
                    if (row < r.Top) r.Top = row;
                    if (row > r.Bottom) r.Bottom = row;
                }
            }
        }
        return r;
    }
}
'@

$awareness = [BodyClick]::MakeDpiAware()
$work = [BodyClick]::WorkArea()
Write-Output ("body-click: start {0:HH:mm:ss.fff} dpiAwareness={1} workArea={2},{3},{4},{5}" -f (Get-Date), $awareness, $work.Left, $work.Top, $work.Right, $work.Bottom)

# The banner's search window: the right-hand corner above the taskbar. A Windows 11 banner is drawn
# against the right edge with a small margin, just above the work area's bottom.
$windowWidth = [Math]::Min(700, $work.Right)
$windowHeight = 300
$windowX = $work.Right - $windowWidth
$windowY = $work.Bottom - $windowHeight - 8
$stride = $windowWidth * 4

$clickX = $windowX + [int]($windowWidth * 0.62)
$clickY = $windowY + [int]($windowHeight * 0.62)
Write-Output ("body-click: banner search window={0},{1} {2}x{3}; body click point={4},{5}" -f $windowX, $windowY, $windowWidth, $windowHeight, $clickX, $clickY)

Start-Sleep -Milliseconds ([int]($DelaySeconds * 1000))

for ($i = 1; $i -le $Clicks; $i++) {
    $before = [BodyClick]::Capture($windowX, $windowY, $windowWidth, $windowHeight)
    $stamp = Get-Date
    $events = [BodyClick]::ClickAt($clickX, $clickY)
    Start-Sleep -Milliseconds 700
    $after = [BodyClick]::Capture($windowX, $windowY, $windowWidth, $windowHeight)
    $diff = [BodyClick]::Diff($before, $after, $windowWidth, $windowHeight, 24, $stride)

    Write-Output ("body-click: click #{0} at {1:HH:mm:ss.fff} point={2},{3} events={4} prePostChangedPixels={5}" -f $i, $stamp, $clickX, $clickY, $events, $diff.Changed)

    if ($i -lt $Clicks) { Start-Sleep -Milliseconds ([int](($IntervalSeconds * 1000) - 1400)) }
}

Write-Output ("body-click: complete at {0:HH:mm:ss.fff}" -f (Get-Date))
