using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Serialises the tests that measure a process-wide GDI handle count.
/// </summary>
/// <remarks>
/// <see cref="GdiHandles.Count"/> reads a counter owned by the whole process, so a test that
/// asserts a delta on it has to be the only thing allocating and releasing GDI objects while it
/// runs. xunit runs test collections in parallel by default; this definition is what keeps the
/// measurement honest instead of flaky.
/// </remarks>
[CollectionDefinition(GdiCountCollection.Name, DisableParallelization = true)]
public sealed class GdiCountCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "GDI count measurement";
}

/// <summary>
/// Proves the icon lifecycle end to end: the shell call <em>sequence</em> of a registration, the
/// flags that make the tooltip work at all, the retain-and-destroy ownership of every
/// <c>HICON</c>, the two failure policies (startup throws, runtime retries once and surfaces) and
/// the fact that disposal really takes the resources away - including the real GDI handle count
/// across fifty replacement cycles.
/// </summary>
/// <remarks>
/// <para>
/// Every test runs on an STA thread that owns a dispatcher (<see cref="DispatcherFactAttribute"/>),
/// because registering an icon creates a real hidden <c>HwndSource</c> host window through the
/// genuine WPF path; only the shell calls and the failure injection come from
/// <see cref="FakeShellApi"/>.
/// </para>
/// <para>
/// <b>Sequence is the contract.</b> An icon registered without the immediately following
/// <c>NIM_SETVERSION(NOTIFYICON_VERSION_4)</c> speaks the legacy protocol, and an add without
/// <c>NIF_SHOWTIP</c> produces an icon whose tooltip silently does nothing - both are invisible
/// failures that no exception would ever reveal, which is why they are asserted as call sequences
/// rather than described in a comment (D012).
/// </para>
/// <para>
/// <b>Requirements proven here:</b> R001 (an element with no window can register a real icon on a
/// hidden top-level host and remove it again), R007 (fifty real icon replacements leave the process
/// GDI handle count flat, and a failed replacement keeps the previous icon registered), R013 (a
/// refused add throws with the operation and the code and leaves no half-registration, while a
/// refused modify retries exactly once, traces and raises the routed event without throwing) and
/// R015's property-state half (a refused registration leaves <see cref="TrayIcon.Visible"/>
/// <see langword="false"/> rather than claiming an icon that does not exist).
/// </para>
/// </remarks>
[Collection(GdiCountCollection.Name)]
public sealed class TrayIconLifecycleTests
{
    /// <summary>The icon edge length used by the tests that build real icons.</summary>
    private const int IconSize = 16;

    /// <summary>
    /// The registration sequence: <c>NIM_ADD</c> followed immediately by
    /// <c>NIM_SETVERSION(4)</c>, on the hidden host window, with an id the shell can report back.
    /// </summary>
    [DispatcherFact]
    public void First_visible_registration_issues_Add_then_SetVersion4()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;

        IReadOnlyList<ShellCall> calls = shell.ShellNotifyIconCalls;

        // The order is the contract: v4 must be selected on the registration that just succeeded.
        Assert.Equal(2, calls.Count);
        Assert.Equal(ShellConstants.NIM_ADD, calls[0].Message);
        Assert.Equal(ShellConstants.NIM_SETVERSION, calls[1].Message);

        IReadOnlyList<NOTIFYICONDATAW> data = shell.ShellNotifyIconDataSnapshots;

        Assert.Equal(ShellConstants.NOTIFYICON_VERSION_4, data[1].uTimeoutOrVersion);

        // The icon is anchored to the host window and reports through the callback message the host
        // was built for; a mismatch here would make every notification arrive unmatched in S02.
        Assert.Equal(trayIcon.HostHandle, data[0].hWnd);
        Assert.Equal(ShellConstants.TrayCallbackMessage, data[0].uCallbackMessage);

        // The id travels in HIWORD(lParam) under v4, so it must fit in 16 bits and must not be 0.
        Assert.InRange(data[0].uID, 1u, 0xFFFFu);

        Assert.True(trayIcon.IsRegistered);
        Assert.NotEqual(IntPtr.Zero, trayIcon.RegisteredIconHandle);

        // A second assignment of the same value is not a second registration.
        trayIcon.Visible = true;

        Assert.Equal(2, shell.ShellNotifyIconCalls.Count);
    }

    /// <summary>
    /// The add flags must include <c>NIF_MESSAGE</c>, <c>NIF_ICON</c>, <c>NIF_TIP</c> and - the
    /// one that decides whether the tooltip works at all under version 4 - <c>NIF_SHOWTIP</c>.
    /// </summary>
    [DispatcherFact]
    public void Add_flags_include_message_icon_tip_and_showtip()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
            ToolTipText = "Trustsoft.NotifyIcon",
        };

        trayIcon.Visible = true;

        uint flags = shell.ShellNotifyIconCalls[0].Flags;

        Assert.True((flags & ShellConstants.NIF_MESSAGE) != 0, $"NIF_MESSAGE missing; measured 0x{flags:X}.");
        Assert.True((flags & ShellConstants.NIF_ICON) != 0, $"NIF_ICON missing; measured 0x{flags:X}.");
        Assert.True((flags & ShellConstants.NIF_TIP) != 0, $"NIF_TIP missing; measured 0x{flags:X}.");
        Assert.True((flags & ShellConstants.NIF_SHOWTIP) != 0, $"NIF_SHOWTIP missing; measured 0x{flags:X}.");
    }

    /// <summary>
    /// <c>NIF_SHOWTIP</c> is present even when no icon image is set: the flag gates the tooltip,
    /// not the image, and its absence is exactly the silent-tooltip bug this contract exists to
    /// prevent. <c>NIF_ICON</c> is absent in that case, because a zero <c>hIcon</c> with the flag
    /// set asks the shell to display nothing.
    /// </summary>
    [DispatcherFact]
    public void Add_flags_include_showtip_even_without_an_icon_source()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { ToolTipText = "No image yet" };

        trayIcon.Visible = true;

        uint flags = shell.ShellNotifyIconCalls[0].Flags;

        Assert.True((flags & ShellConstants.NIF_SHOWTIP) != 0, $"NIF_SHOWTIP missing; measured 0x{flags:X}.");
        Assert.True((flags & ShellConstants.NIF_ICON) == 0, $"NIF_ICON must not be set without an icon; measured 0x{flags:X}.");
        Assert.Equal(IntPtr.Zero, trayIcon.RegisteredIconHandle);
        Assert.True(trayIcon.IsRegistered);
    }

    /// <summary>
    /// Every shell call carries the marshalled size of the structure, on every operation: a
    /// <c>cbSize</c> the shell does not recognise makes it ignore the call, which is
    /// indistinguishable from success at the call site.
    /// </summary>
    [DispatcherFact]
    public void cbSize_on_every_call_is_the_struct_size()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
            ToolTipText = "tip",
        };

        trayIcon.Visible = true;
        trayIcon.ToolTipText = "updated";
        trayIcon.IconSource = CreateSolid(IconSize, 0x90, 0x30, 0x10);
        trayIcon.Visible = false;

        uint expected = (uint)NOTIFYICONDATAW.SizeOf();
        IReadOnlyList<NOTIFYICONDATAW> data = shell.ShellNotifyIconDataSnapshots;

        Assert.Equal(5, data.Count);
        Assert.All(data, snapshot => Assert.Equal(expected, snapshot.cbSize));
    }

    /// <summary>
    /// Replacing the icon destroys the <em>previous</em> <c>HICON</c> and only after the shell
    /// accepted the new one, so the replacement is never a window in which no icon handle is
    /// registered (R007).
    /// </summary>
    [DispatcherFact]
    public void IconSource_change_issues_Modify_and_destroys_the_previous_icon()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;
        IntPtr firstIcon = trayIcon.RegisteredIconHandle;

        trayIcon.IconSource = CreateSolid(IconSize, 0x10, 0x90, 0x40);

        ShellCall modify = Assert.Single(
            shell.ShellNotifyIconCalls.Where(call => call.Message == ShellConstants.NIM_MODIFY));

        Assert.True((modify.Flags & ShellConstants.NIF_ICON) != 0, $"NIF_ICON missing; measured 0x{modify.Flags:X}.");

        // The old handle is released, and it is released exactly once.
        Assert.Equal(1, shell.DestroyedIcons);
        Assert.Contains(firstIcon, shell.DestroyedIconHandles);

        // ... and the icon registered with the shell is now the new one, not the released one.
        Assert.NotEqual(firstIcon, trayIcon.RegisteredIconHandle);
        Assert.NotEqual(IntPtr.Zero, trayIcon.RegisteredIconHandle);

        // Invariant: one handle alive at a time - the one the shell holds.
        Assert.Equal(1, shell.OutstandingIcons);
    }

    /// <summary>
    /// A tooltip change is a <c>NIM_MODIFY</c> carrying both <c>NIF_TIP</c> and <c>NIF_SHOWTIP</c>.
    /// </summary>
    [DispatcherFact]
    public void ToolTipText_change_issues_Modify_with_TIP_and_SHOWTIP()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { ToolTipText = "first" };

        trayIcon.Visible = true;

        int before = shell.ShellNotifyIconCalls.Count;

        trayIcon.ToolTipText = "second";

        ShellCall modify = Assert.Single(shell.ShellNotifyIconCalls.Skip(before));

        Assert.Equal(ShellConstants.NIM_MODIFY, modify.Message);
        Assert.True((modify.Flags & ShellConstants.NIF_TIP) != 0, $"NIF_TIP missing; measured 0x{modify.Flags:X}.");
        Assert.True((modify.Flags & ShellConstants.NIF_SHOWTIP) != 0, $"NIF_SHOWTIP missing; measured 0x{modify.Flags:X}.");
        Assert.Equal("second", shell.ShellNotifyIconDataSnapshots[^1].szTip);
    }

    /// <summary>
    /// Tooltip text longer than <c>szTip</c> can hold is truncated deliberately to 127 characters,
    /// rather than left to be cut at an arbitrary point by the marshaller.
    /// </summary>
    [DispatcherFact]
    public void ToolTipText_is_truncated_to_127_characters()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { ToolTipText = new string('x', 200) };

        trayIcon.Visible = true;

        Assert.Equal(127, shell.ShellNotifyIconDataSnapshots[0].szTip.Length);
        Assert.Equal(new string('x', 127), shell.ShellNotifyIconDataSnapshots[0].szTip);

        // The same truncation applies to an update, not only to the registration.
        trayIcon.ToolTipText = new string('y', 300);

        Assert.Equal(new string('y', 127), shell.ShellNotifyIconDataSnapshots[^1].szTip);
    }

    /// <summary>
    /// The truncation never splits a surrogate pair: half a pair would be an invalid string the
    /// shell renders as a replacement character.
    /// </summary>
    [DispatcherFact]
    public void ToolTipText_truncation_does_not_split_a_surrogate_pair()
    {
        // 126 units, then a two-unit emoji straddling the 127-character boundary.
        string text = new string('x', 126) + "\uD83D\uDE00" + "tail";

        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { ToolTipText = text };

        trayIcon.Visible = true;

        string tip = shell.ShellNotifyIconDataSnapshots[0].szTip;

        Assert.Equal(126, tip.Length);
        Assert.False(char.IsHighSurrogate(tip[^1]), "The truncated tooltip ends with a lone high surrogate.");
    }

    /// <summary>
    /// Unregistering issues exactly one <c>NIM_DELETE</c> and releases the retained
    /// <c>HICON</c>; a second <c>Visible = false</c> does nothing at all.
    /// </summary>
    [DispatcherFact]
    public void Visible_false_issues_Delete_and_destroys_the_icon()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;
        IntPtr icon = trayIcon.RegisteredIconHandle;

        trayIcon.Visible = false;

        Assert.Equal(1, shell.ShellNotifyIconCalls.Count(call => call.Message == ShellConstants.NIM_DELETE));
        Assert.Equal(1, shell.DestroyedIcons);
        Assert.Contains(icon, shell.DestroyedIconHandles);
        Assert.False(trayIcon.IsRegistered);
        Assert.Equal(IntPtr.Zero, trayIcon.RegisteredIconHandle);
        Assert.Equal(0, shell.OutstandingIcons);

        trayIcon.Visible = false;

        Assert.Equal(1, shell.ShellNotifyIconCalls.Count(call => call.Message == ShellConstants.NIM_DELETE));
        Assert.Equal(1, shell.DestroyedIcons);
    }

    /// <summary>
    /// Disposal removes the registration, releases the handle and destroys the hidden host window,
    /// and repeating it changes nothing.
    /// </summary>
    [DispatcherFact]
    public void Dispose_is_idempotent_and_removes_the_icon()
    {
        var shell = new FakeShellApi();
        var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;
        IntPtr hostHandle = trayIcon.HostHandle;

        Assert.NotEqual(IntPtr.Zero, hostHandle);
        Assert.True(Win32.IsWindow(hostHandle), "The host window must exist while the icon is registered.");

        trayIcon.Dispose();
        trayIcon.Dispose();

        Assert.Equal(1, shell.ShellNotifyIconCalls.Count(call => call.Message == ShellConstants.NIM_DELETE));
        Assert.Equal(1, shell.DestroyedIcons);
        Assert.Equal(0, shell.OutstandingIcons);
        Assert.False(trayIcon.IsRegistered);
        Assert.Equal(IntPtr.Zero, trayIcon.RegisteredIconHandle);

        // The host window is really gone, not merely unreferenced.
        Assert.False(Win32.IsWindow(hostHandle), "Disposal must destroy the hidden host window.");
    }

    /// <summary>
    /// The R007 headline evidence: fifty real icon replacements on a real GDI path leave the
    /// process GDI handle count flat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asserted quantity is a <em>delta</em> against a measured baseline, because a fresh
    /// process legitimately owns zero GDI objects: "the count is positive" would prove nothing.
    /// A missing <c>DestroyIcon</c> on the replacement path would move the count by roughly 150
    /// (three objects per leaked icon), so the two-handle allowance is wide enough only for the
    /// one retained icon and a transient.
    /// </para>
    /// <para>
    /// The seam counters are asserted as well, and they are not redundant: the GDI counter proves
    /// real OS objects came back, while the identity counters prove that the icon created for a
    /// refused replacement was released rather than leaked on a path a handle count cannot
    /// attribute.
    /// </para>
    /// </remarks>
    [DispatcherFact]
    public void No_gdi_leak_across_50_icon_replacement_cycles()
    {
        const int replacements = 50;

        var shell = new GdiShellApi();
        BitmapSource source = CreateSolid(IconSize, 0x20, 0x60, 0xA0);

        // Warm-up: the first hidden window a process creates brings a few long-lived GDI objects
        // with it (WPF's window and theme caches). Measuring the baseline before it would fold that
        // one-time cost into the delta and turn the assertion into a claim about WPF.
        using (var warmUp = new TrayIcon(shell) { IconSource = source })
        {
            warmUp.Visible = true;
            warmUp.Visible = false;
        }

        int before = GdiHandles.Count();

        var trayIcon = new TrayIcon(shell) { IconSource = source };

        trayIcon.Visible = true;

        for (int i = 0; i < replacements; i++)
        {
            trayIcon.IconSource = CreateSolid(IconSize, (byte)i, 0x60, 0xA0);
        }

        trayIcon.Visible = false;
        trayIcon.Dispose();

        int after = GdiHandles.Count();

        Assert.True(
            after - before <= 2,
            $"Replacing the icon {replacements} times must not leak GDI handles; measured {before} before and {after} after.");

        // Every real icon created - warm-up, initial and one per replacement - was destroyed again.
        Assert.Equal(replacements + 2, shell.CreatedIcons);
        Assert.Equal(shell.CreatedIcons, shell.DestroyedIcons);
    }

    /// <summary>
    /// A refused <c>NIM_ADD</c> is a startup failure: it throws a named exception carrying the
    /// operation and the Win32 code, attempts no <c>NIM_SETVERSION</c>, releases the icon handle it
    /// built for the attempt, and leaves <see cref="TrayIcon.Visible"/> <see langword="false"/>.
    /// </summary>
    [DispatcherFact]
    public void Failed_Add_throws_TrayIconException_and_registers_nothing()
    {
        var shell = new FakeShellApi { LastErrorToReport = 5 };

        shell.FailAlways(ShellOperation.ShellNotifyIcon);

        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        var exception = Assert.Throws<TrayIconException>(() => trayIcon.Visible = true);

        Assert.Equal(TrayIconException.OperationAdd, exception.Operation);
        Assert.Equal(5, exception.Win32ErrorCode);

        ShellCall attempt = Assert.Single(shell.ShellNotifyIconCalls);

        Assert.Equal(ShellConstants.NIM_ADD, attempt.Message);

        // The handle built for the refused add is not registered anywhere, so it must be released.
        Assert.Equal(1, shell.DestroyedIcons);
        Assert.Equal(0, shell.OutstandingIcons);

        Assert.False(trayIcon.Visible);
        Assert.False(trayIcon.IsRegistered);
    }

    /// <summary>
    /// A refused <c>NIM_SETVERSION</c> rolls the half-registration back before throwing, so the
    /// shell is never left displaying an icon that speaks the wrong protocol.
    /// </summary>
    [DispatcherFact]
    public void Failed_SetVersion_deletes_the_half_registration_and_throws()
    {
        var shell = new FakeShellApi { LastErrorToReport = 87 };

        // The second Shell_NotifyIcon call is the NIM_SETVERSION that follows the add.
        shell.FailCallNumber(ShellOperation.ShellNotifyIcon, 2);

        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        var exception = Assert.Throws<TrayIconException>(() => trayIcon.Visible = true);

        Assert.Equal(TrayIconException.OperationSetVersion, exception.Operation);
        Assert.Equal(87, exception.Win32ErrorCode);

        uint[] messages = [.. shell.ShellNotifyIconCalls.Select(call => call.Message)];

        Assert.Equal(
            new[] { ShellConstants.NIM_ADD, ShellConstants.NIM_SETVERSION, ShellConstants.NIM_DELETE },
            messages);

        Assert.Equal(0, shell.OutstandingIcons);
        Assert.False(trayIcon.Visible);
        Assert.False(trayIcon.IsRegistered);
    }

    /// <summary>
    /// A refused runtime modification is retried exactly once, then traced and raised through the
    /// routed event - without throwing, and without losing the icon that is already displayed.
    /// </summary>
    [DispatcherFact]
    public void Failed_Modify_retries_once_then_raises_TrayError_and_traces()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;

        IntPtr registered = trayIcon.RegisteredIconHandle;
        int modificationsBefore = shell.ShellNotifyIconCalls.Count;

        var observed = new List<TrayErrorEventArgs>();
        trayIcon.TrayError += (_, args) => observed.Add(args);

        shell.FailAlways(ShellOperation.ShellNotifyIcon);
        shell.LastErrorToReport = 5;

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var listener = new TextWriterTraceListener(writer);
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(listener);

            // No exception may escape: losing an icon update is better than losing the application.
            trayIcon.IconSource = CreateSolid(IconSize, 0x90, 0x30, 0x10);

            source.Flush();
        }
        finally
        {
            source.Listeners.Remove(listener);
            listener.Dispose();
        }

        // Exactly two attempts at the same call: the original and the single retry.
        int modifications = shell.ShellNotifyIconCalls
            .Skip(modificationsBefore)
            .Count(call => call.Message == ShellConstants.NIM_MODIFY);

        Assert.Equal(2, modifications);

        TrayErrorEventArgs error = Assert.Single(observed);

        Assert.True(error.Retried);
        Assert.Equal(TrayIconException.OperationModify, error.Operation);
        Assert.Equal(5, error.Win32ErrorCode);
        Assert.IsType<TrayIconException>(error.Exception);

        Assert.Contains("TrayIcon Modify failed (Win32 error 5); retried=True.", writer.ToString(), StringComparison.Ordinal);

        // R007: the previously valid icon is still the registered one and was not destroyed.
        Assert.Equal(registered, trayIcon.RegisteredIconHandle);
        Assert.DoesNotContain(registered, shell.DestroyedIconHandles);

        // The handle built for the refused replacement is not registered anywhere, so it was freed.
        Assert.Equal(1, shell.DestroyedIcons);
    }

    /// <summary>
    /// The routed event is registered under a constant name and bubbles, which is what the markup
    /// contract in S06 attaches to.
    /// </summary>
    [StaFact]
    public void TrayError_is_registered_as_a_bubbling_routed_event()
    {
        Assert.Equal(TrayIcon.TrayErrorEventName, TrayIcon.TrayErrorEvent.Name);
        Assert.Equal(typeof(TrayIcon), TrayIcon.TrayErrorEvent.OwnerType);
        Assert.Equal(RoutingStrategy.Bubble, TrayIcon.TrayErrorEvent.RoutingStrategy);
    }

    /// <summary>
    /// Creates a solid straight-alpha <c>Bgra32</c> bitmap of the given size.
    /// </summary>
    /// <param name="size">The edge length in pixels.</param>
    /// <param name="b">The blue value.</param>
    /// <param name="g">The green value.</param>
    /// <param name="r">The red value.</param>
    /// <returns>A bitmap source usable as an icon source.</returns>
    private static BitmapSource CreateSolid(int size, byte b, byte g, byte r)
    {
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }
}
