using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.ProbeLive;

/// <summary>
/// External observer for the live notification-area checks of S05 (R005 recovery and R006 teardown).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A tray icon's presence is a fact about the shell's process, not about the
/// application's, so no unit test and no in-process assertion can prove that an icon came back by
/// itself after Explorer restarted or that no stale icon survived a hard kill. This program is the
/// instrument that asks the shell.
/// </para>
/// <para>
/// <b>Why <c>Shell_NotifyIconGetRect</c> is the oracle.</b> It takes a
/// <c>NOTIFYICONIDENTIFIER</c> of <c>(hWnd, uID)</c> and returns the icon's screen rectangle if and
/// only if the shell currently holds that icon. That makes presence a documented OS answer rather
/// than a screenshot of the notification area, which on Windows 11 would mean finding an icon inside
/// the overflow flyout and reading pixels. The identity is the icon's own: the probe enumerates the
/// sample's top-level windows and scans the small icon-id range until the shell answers, so a wrong
/// identity cannot produce a false pass - it produces no reading at all.
/// </para>
/// <para>
/// <b>It is an independent implementation on purpose.</b> The P/Invoke declarations, the structure
/// layout and the identifier are declared here rather than taken from the library under test, so a
/// bug in the library's own interop cannot make the probe agree with it.
/// </para>
/// <para>
/// <b>Usage.</b>
/// <code>
/// probe-live &lt;sampleExe&gt; &lt;observeSeconds&gt; [--kill-after &lt;seconds&gt;] [--keep-sample-alive] [--sample-arg &lt;arg&gt;]...
/// </code>
/// Everything after the first two arguments is passed through to the sample, which owns its own
/// demonstration switches (<c>--run-seconds</c>, <c>--show-balloon-after</c>,
/// <c>--open-menu-after</c>). The sample's own output is echoed with a <c>sample</c> prefix so one
/// captured stream holds both sides of the observation.
/// </para>
/// <para>
/// <b>Verdicts, not impressions.</b> The observation is a once-per-second series. The program exits
/// non-zero when the icon was never observed present, and it refuses to report
/// <c>icon-after-exit: gone</c> in that case, because "never seen" and "disappeared" are different
/// facts and only the second one is evidence about teardown.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>The window title <c>TrayMessageWindow</c> gives the host it creates.</summary>
    /// <remarks>
    /// Diagnostic only: the probe finds the host by enumerating the sample's top-level windows, so a
    /// renamed window changes what is printed and nothing else.
    /// </remarks>
    private const string HostWindowTitleHint = "Trustsoft.NotifyIcon.TrayMessageWindow";

    /// <summary>The highest icon id the identity scan tries.</summary>
    /// <remarks>
    /// Icon ids are allocated from a per-process counter starting at one, and the sample hosts a
    /// single icon, so the true id is almost always 1. The scan is bounded and cheap (each attempt is
    /// one shell call), and a scan that found nothing is reported as "no reading", never as absence.
    /// </remarks>
    private const uint MaxIconIdScan = 32;

    /// <summary>How long the teardown verdict waits for the shell to drop a dead icon.</summary>
    /// <remarks>
    /// The shell drops an icon when the window that registered it is destroyed, and a hard kill
    /// destroys the window as part of process teardown, so the drop is not necessarily instantaneous.
    /// A stale icon, by contrast, stays forever; the settle window therefore cannot hide one.
    /// </remarks>
    private static readonly TimeSpan AfterExitSettle = TimeSpan.FromSeconds(5);

    /// <summary>Runs the probe.</summary>
    /// <param name="args">See the class remarks for the command line.</param>
    /// <returns><c>0</c> when the icon was observed present, <c>1</c> otherwise.</returns>
    private static int Main(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], CultureInfo.InvariantCulture, out int observeSeconds))
        {
            Console.Error.WriteLine("usage: probe-live <sampleExe> <observeSeconds> [--kill-after <seconds>] [--sample-arg <arg>]...");
            return 2;
        }

        string sampleExe = args[0];
        TimeSpan? killAfter = null;
        bool keepSampleAlive = false;
        var sampleArgs = new List<string>();

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--keep-sample-alive")
            {
                // Positive control for the teardown verdict: with the sample still running, the
                // shell must keep answering that the icon is present, which is what proves the
                // verdict column can say both things rather than being stuck on "gone".
                keepSampleAlive = true;
                continue;
            }

            if (args[i] == "--kill-after" && i + 1 < args.Length
                && double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out double killSeconds))
            {
                killAfter = TimeSpan.FromSeconds(killSeconds);
                i++;
                continue;
            }

            if (args[i] == "--sample-arg" && i + 1 < args.Length)
            {
                sampleArgs.Add(args[i + 1]);
                i++;
                continue;
            }

            sampleArgs.Add(args[i]);
        }

        Console.WriteLine($"[probe] probe-live start {DateTime.Now:yyyy-MM-dd HH:mm:ss}; os={Environment.OSVersion.VersionString}; machine={Environment.MachineName}");
        Console.WriteLine($"[probe] sample exe: {sampleExe}");
        Console.WriteLine($"[probe] observe: {observeSeconds}s; kill-after: {(killAfter is null ? "no" : $"{killAfter.Value.TotalSeconds:0}s")}; sample args: {string.Join(' ', sampleArgs)}");

        using var sample = StartSample(sampleExe, sampleArgs);

        Console.WriteLine($"[probe] launched pid={sample.Id}");

        IntPtr hostWindow = IntPtr.Zero;
        uint iconId = 0;
        bool identityKnown = false;
        bool everPresent = false;
        var stopwatch = Stopwatch.StartNew();
        bool killIssued = false;

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(observeSeconds))
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));

            if (sample.HasExited)
            {
                Console.WriteLine($"[probe] sample-exited at t={stopwatch.Elapsed.TotalSeconds:0}s with exit code {sample.ExitCode}");
                break;
            }

            if (killAfter is TimeSpan killDeadline && !killIssued && stopwatch.Elapsed >= killDeadline)
            {
                killIssued = true;
                KillSample(sample.Id);
            }

            if (!identityKnown && !TryResolveIdentity(sample.Id, out hostWindow, out iconId, out string scanDetail))
            {
                Console.WriteLine($"[probe] t={stopwatch.Elapsed.TotalSeconds:0}s pid={sample.Id} icon=no-reading ({scanDetail}) gdi={ReadGdi(sample)}");
                continue;
            }

            identityKnown = true;

            if (TryGetIconRect(hostWindow, iconId, out NativeRect rect, out int hr))
            {
                everPresent = true;
                Console.WriteLine($"[probe] t={stopwatch.Elapsed.TotalSeconds:0}s pid={sample.Id} icon=present rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) gdi={ReadGdi(sample)}");
            }
            else
            {
                Console.WriteLine($"[probe] t={stopwatch.Elapsed.TotalSeconds:0}s pid={sample.Id} icon=absent hr=0x{hr:X8} gdi={ReadGdi(sample)}");
            }
        }

        Console.WriteLine($"[probe] identity: hwnd=0x{hostWindow.ToInt64():X} uID={iconId} title=\"{DescribeWindow(hostWindow)}\" ({HostWindowTitleHint} is the expected title)");
        Console.WriteLine($"[probe] observed-present: {(everPresent ? "yes" : "no")}");
        Console.WriteLine($"[probe] sample-alive-at-end: {!sample.HasExited}; exit-code: {(sample.HasExited ? sample.ExitCode.ToString(CultureInfo.InvariantCulture) : "(running)")}");

        // The teardown verdict. The sample is killed if it is still alive, because the claim under
        // test is "a process that died without disposing leaves no icon" - not "an app that is still
        // running leaves none".
        if (!sample.HasExited && !keepSampleAlive)
        {
            Console.WriteLine("[probe] the sample is still running at the end of the observation; killing it so the teardown verdict is about a dead process");
            KillSample(sample.Id);
            sample.WaitForExit(10_000);
        }
        else if (keepSampleAlive)
        {
            Console.WriteLine("[probe] note: --keep-sample-alive, so the next verdict is the oracle's positive control - a live icon must be reported as still present");
        }

        ReportAfterExit(everPresent, identityKnown, hostWindow, iconId);

        return everPresent ? 0 : 1;
    }

    /// <summary>
    /// Resolves the sample's icon identity by asking the shell: for each top-level window the sample
    /// owns, and each plausible icon id, the first successful <c>Shell_NotifyIconGetRect</c> names an
    /// icon the shell really holds.
    /// </summary>
    /// <param name="pid">The sample process id.</param>
    /// <param name="hostWindow">Receives the window that registered the icon.</param>
    /// <param name="iconId">Receives the icon id.</param>
    /// <param name="detail">Receives a human-readable account of what was scanned.</param>
    /// <returns><see langword="true"/> when an identity was found.</returns>
    /// <remarks>
    /// Enumerating windows rather than looking up a known title keeps the probe independent of how
    /// the library names its host, and a failed scan is reported as "no reading": it can never be
    /// mistaken for the icon being absent, which is the difference between a broken instrument and a
    /// real finding.
    /// </remarks>
    private static bool TryResolveIdentity(int pid, out IntPtr hostWindow, out uint iconId, out string detail)
    {
        hostWindow = IntPtr.Zero;
        iconId = 0;

        IReadOnlyList<IntPtr> windows = TopLevelWindowsOf(pid);

        foreach (IntPtr window in windows)
        {
            for (uint candidate = 1; candidate <= MaxIconIdScan; candidate++)
            {
                if (TryGetIconRect(window, candidate, out _, out _))
                {
                    hostWindow = window;
                    iconId = candidate;
                    detail = $"found on window 0x{window.ToInt64():X}";
                    return true;
                }
            }
        }

        detail = windows.Count == 0
            ? "the sample owns no top-level window yet"
            : $"scanned {windows.Count} window(s) x {MaxIconIdScan} icon id(s)";
        return false;
    }

    /// <summary>
    /// Calls <c>Shell_NotifyIconGetRect</c> for one identity.
    /// </summary>
    /// <param name="hostWindow">The window that registered the icon.</param>
    /// <param name="iconId">The icon id.</param>
    /// <param name="rectangle">Receives the located rectangle on success.</param>
    /// <param name="hr">Receives the <c>HRESULT</c> in every case.</param>
    /// <returns><see langword="true"/> when the shell located the icon.</returns>
    private static bool TryGetIconRect(IntPtr hostWindow, uint iconId, out NativeRect rectangle, out int hr)
    {
        var identifier = new NotifyIconIdentifier
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            hWnd = hostWindow,
            uID = iconId,
            guidItem = Guid.Empty,
        };

        hr = ShellNotifyIconGetRect(ref identifier, out rectangle);
        return hr == 0;
    }

    /// <summary>
    /// Reports whether a stale icon outlived the sample, waiting for the shell to settle first.
    /// </summary>
    /// <param name="everPresent">Whether the icon was observed present at least once.</param>
    /// <param name="identityKnown">Whether an identity was resolved at all.</param>
    /// <param name="hostWindow">The resolved window.</param>
    /// <param name="iconId">The resolved icon id.</param>
    /// <remarks>
    /// The guard is the point: without an earlier positive reading, "the shell does not have it now"
    /// says nothing about teardown, so the verdict is <c>NOT OBSERVED</c> rather than <c>gone</c>.
    /// </remarks>
    private static void ReportAfterExit(bool everPresent, bool identityKnown, IntPtr hostWindow, uint iconId)
    {
        if (!everPresent || !identityKnown)
        {
            Console.WriteLine("[probe] icon-after-exit: NOT OBSERVED (the icon was never observed present, so its current absence proves nothing)");
            return;
        }

        var deadline = Stopwatch.StartNew();

        while (true)
        {
            if (!TryGetIconRect(hostWindow, iconId, out NativeRect rect, out int hr))
            {
                Console.WriteLine($"[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x{hr:X8} for hwnd=0x{hostWindow.ToInt64():X} uID={iconId})");
                return;
            }

            if (deadline.Elapsed >= AfterExitSettle)
            {
                Console.WriteLine($"[probe] icon-after-exit: still present rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) after {AfterExitSettle.TotalSeconds:0}s - the shell still holds the icon for hwnd=0x{hostWindow.ToInt64():X} uID={iconId}");
                return;
            }

            Console.WriteLine($"[probe] after-exit check pending: still present at t+{deadline.Elapsed.TotalSeconds:0.0}s (settling)");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Starts the sample as a child process with its output relayed.</summary>
    /// <param name="sampleExe">The sample executable path.</param>
    /// <param name="sampleArgs">The arguments to hand over to the sample.</param>
    /// <returns>The started process.</returns>
    private static Process StartSample(string sampleExe, IReadOnlyList<string> sampleArgs)
    {
        var startInfo = new ProcessStartInfo(sampleExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in sampleArgs)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.WriteLine($"sample| {e.Data}");
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.WriteLine($"sample! {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>
    /// Hard-kills a process with <c>taskkill /f</c>, which is the same instrument the UAT records.
    /// </summary>
    /// <param name="pid">The process id to kill.</param>
    /// <remarks>
    /// Deliberately not <see cref="Process.Kill()"/>: the claim under test is about a process that got
    /// no chance to run managed shutdown code, and <c>taskkill /f</c> is both that and reproducible
    /// from a terminal, so the evidence in the UAT matches the command a reader can re-run.
    /// </remarks>
    private static void KillSample(int pid)
    {
        using var taskkill = Process.Start(new ProcessStartInfo("taskkill", $"/f /pid {pid}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (taskkill is null)
        {
            Console.WriteLine($"[probe] taskkill /f /pid {pid} could not be started");
            return;
        }

        string output = taskkill.StandardOutput.ReadToEnd().Trim();
        string error = taskkill.StandardError.ReadToEnd().Trim();

        taskkill.WaitForExit(10_000);

        Console.WriteLine($"[probe] taskkill /f /pid {pid} -> exit {taskkill.ExitCode}; {output} {error}".TrimEnd());
    }

    /// <summary>Reads the sample's GDI object count.</summary>
    /// <param name="sample">The sample process.</param>
    /// <returns>The count, or <c>-1</c> when the reading is unavailable.</returns>
    /// <remarks>
    /// The sample rotates its icon every second by design, so this series is expected to oscillate by
    /// design; what a leak would look like is a <em>trend</em>. It is reported as a column rather than
    /// judged here, because the judgement belongs with the rest of the evidence.
    /// </remarks>
    private static string ReadGdi(Process sample)
    {
        try
        {
            uint count = GetGuiResources(sample.Handle, GdiObjectsSelector);
            return count.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return "n/a";
        }
    }

    /// <summary>Lists the sample's top-level windows.</summary>
    /// <param name="pid">The sample process id.</param>
    /// <returns>The window handles in enumeration order.</returns>
    private static IReadOnlyList<IntPtr> TopLevelWindowsOf(int pid)
    {
        var windows = new List<IntPtr>();

        EnumWindows(
            (window, parameter) =>
            {
                GetWindowThreadProcessId(window, out uint windowPid);

                if (windowPid == (uint)pid)
                {
                    windows.Add(window);
                }

                return true;
            },
            IntPtr.Zero);

        return windows;
    }

    /// <summary>Reads a window's title, for diagnostics only.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>The title, or an empty string.</returns>
    private static string DescribeWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "(none resolved)";
        }

        var buffer = new char[256];
        int length = GetWindowText(window, buffer, buffer.Length);

        return length > 0 ? new string(buffer, 0, length) : "(no title)";
    }

    /// <summary>The <c>GR_GDIOBJECTS</c> selector of <c>GetGuiResources</c>.</summary>
    private const uint GdiObjectsSelector = 0;

    /// <summary><c>NOTIFYICONIDENTIFIER</c>, in shellapi.h field order.</summary>
    /// <remarks>
    /// <c>cbSize</c> is written from <see cref="Marshal.SizeOf{T}()"/> rather than a literal: the
    /// header's <c>DWORD</c> followed by a pointer produces 4 bytes of padding on x64, and the shell
    /// rejects a call whose <c>cbSize</c> it does not recognise.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        /// <summary>The structure size the shell validates.</summary>
        public uint cbSize;

        /// <summary>The window that registered the icon.</summary>
        public IntPtr hWnd;

        /// <summary>The icon id.</summary>
        public uint uID;

        /// <summary><c>GUID_NULL</c>, so the window and id pair identifies the icon.</summary>
        public Guid guidItem;
    }

    /// <summary>A Win32 <c>RECT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        /// <summary>The left edge.</summary>
        public int Left;

        /// <summary>The top edge.</summary>
        public int Top;

        /// <summary>The right edge.</summary>
        public int Right;

        /// <summary>The bottom edge.</summary>
        public int Bottom;
    }

    /// <summary>The <c>EnumWindows</c> callback shape.</summary>
    /// <param name="window">The window handle.</param>
    /// <param name="parameter">The caller's parameter.</param>
    /// <returns>Whether to continue enumerating.</returns>
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    /// <summary>Retrieves the bounding rectangle of a notification icon.</summary>
    /// <param name="identifier">The icon identity.</param>
    /// <param name="iconLocation">Receives the rectangle.</param>
    /// <returns><c>S_OK</c> when the shell located the icon.</returns>
    /// <remarks>
    /// The entry point is named explicitly: <c>shell32</c> exports
    /// <c>Shell_NotifyIconGetRect</c> with underscores, while the C# method name cannot carry the
    /// leading <c>Shell_</c> convention except through an explicit point.
    /// </remarks>
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect", SetLastError = true, ExactSpelling = true)]
    private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect iconLocation);

    /// <summary>Enumerates the desktop's top-level windows, including invisible ones.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="parameter">A value passed through to the callback.</param>
    /// <returns>Whether the enumeration ran to completion.</returns>
    /// <remarks>
    /// Invisible and tool windows are included, which is what makes the hidden tray host findable: it
    /// is a top-level window with no <c>WS_VISIBLE</c>.
    /// </remarks>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    /// <summary>Retrieves the process id that created a window.</summary>
    /// <param name="window">The window handle.</param>
    /// <param name="pid">Receives the process id.</param>
    /// <returns>The thread id that created the window.</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    /// <summary>Reads a window's title.</summary>
    /// <param name="window">The window handle.</param>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="maxCount">The buffer length.</param>
    /// <returns>The number of characters copied, excluding the terminator.</returns>
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowText(IntPtr window, [Out] char[] buffer, int maxCount);

    /// <summary>Reads a process's GDI or USER object count.</summary>
    /// <param name="processHandle">The process handle.</param>
    /// <param name="flags">The counter selector; <c>0</c> is GDI objects.</param>
    /// <returns>The count, or <c>0</c> when the reading fails.</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetGuiResources(IntPtr processHandle, uint flags);
}
