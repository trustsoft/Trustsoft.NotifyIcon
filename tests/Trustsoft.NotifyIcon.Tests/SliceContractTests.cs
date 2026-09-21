using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S01 boundary contracts that S02, S03 and S06 consume, asserted as contracts rather than as
/// incidental values, so a later slice that needs a member S01 did not provide finds out now.
/// </summary>
/// <remarks>
/// <para>
/// Each test names the edge it protects: the dependency properties (S01 to S06), the routed event
/// shape the click events must follow (S01 to S02), the host handle a popup menu can be owned by
/// (S01 to S03), the raw message stream S02 decodes, and the message-id uniqueness that keeps that
/// decoding from misrouting.
/// </para>
/// <para>
/// These run against the delivered code, which is the point: if a member is missing or shaped
/// differently than the roadmap assumes, the failure belongs to S01 rather than to whichever later
/// slice discovers it in the middle of its own work.
/// </para>
/// <para>
/// <b>Why this class joined the serial-tail collection in S03.</b> The dismissal contract added
/// below opens a real menu popup and injects a real outside click, and it asserts that this
/// process's visible top-level window set is exactly the popup. That measurement needs the process
/// to be otherwise quiet, so the class shares <see cref="TrayMenuDismissalCollection"/> with the
/// proof that performs the same measurement (and, like it, is deliberately not the GDI collection,
/// since nothing here reads <c>GdiHandles.Count()</c>).
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class SliceContractTests
{
    /// <summary>The icon edge length used by the tests that register an icon.</summary>
    private const int IconSize = 16;

    /// <summary>
    /// <see cref="TrayIcon.IconSource"/>, <see cref="TrayIcon.ToolTipText"/> and
    /// <see cref="TrayIcon.Visible"/> are real dependency properties owned by
    /// <see cref="TrayIcon"/>, with the documented defaults.
    /// </summary>
    /// <remarks>
    /// This is the S01 to S06 edge. A <see cref="FrameworkElement"/> is what D002 chose over a bare
    /// <see cref="DependencyObject"/> so that markup, styles and bindings work against these
    /// properties; a descriptor that does not resolve, or a property registered on a different
    /// owner type, would compile and work from C# while making declarative usage impossible.
    /// </remarks>
    [StaFact]
    public void TrayIcon_exposes_the_three_dependency_properties()
    {
        (string Name, DependencyProperty Property, object? Default)[] properties =
        [
            (nameof(TrayIcon.IconSource), TrayIcon.IconSourceProperty, null),
            (nameof(TrayIcon.ToolTipText), TrayIcon.ToolTipTextProperty, string.Empty),
            (nameof(TrayIcon.Visible), TrayIcon.VisibleProperty, false),
        ];

        foreach ((string name, DependencyProperty property, object? defaultValue) in properties)
        {
            Assert.Equal(name, property.Name);
            Assert.Equal(typeof(TrayIcon), property.OwnerType);
            Assert.False(property.ReadOnly);
            Assert.Equal(defaultValue, property.DefaultMetadata.DefaultValue);

            // The metadata is registered on TrayIcon itself, not inherited or attached: markup
            // resolves the property through this metadata.
            Assert.NotNull(property.GetMetadata(typeof(TrayIcon)));

            // What a markup consumer actually looks through: a descriptor resolvable for this
            // property on this type.
            DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(TrayIcon));

            Assert.NotNull(descriptor);
            Assert.Equal(name, descriptor!.Name);
            Assert.Equal(property, descriptor.DependencyProperty);
        }
    }

    /// <summary>
    /// The failure event is a bubbling routed event named <c>TrayError</c> whose handler type is
    /// <see cref="EventHandler{TrayErrorEventArgs}"/> - the shape S02's click events must follow.
    /// </summary>
    /// <remarks>
    /// The name is asserted against both the registered event and the public constant, because a
    /// consumer that writes <c>TrayIcon.TrayErrorEvent.Name</c> in a log or a filter, and one that
    /// writes the constant, must agree.
    /// </remarks>
    [StaFact]
    public void TrayIcon_exposes_a_routed_TrayError_event_with_the_documented_name()
    {
        Assert.Equal(TrayIcon.TrayErrorEventName, TrayIcon.TrayErrorEvent.Name);
        Assert.Equal("TrayError", TrayIcon.TrayErrorEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, TrayIcon.TrayErrorEvent.RoutingStrategy);
        Assert.Equal(typeof(EventHandler<TrayErrorEventArgs>), TrayIcon.TrayErrorEvent.HandlerType);
        Assert.Equal(typeof(TrayIcon), TrayIcon.TrayErrorEvent.OwnerType);
    }

    /// <summary>
    /// After registration the host window handle is non-zero, still a live window, top-level (so it
    /// is a legal popup owner) and the very handle the shell calls carried.
    /// </summary>
    /// <remarks>
    /// This is the S01 to S03 edge: S03 will use the handle as the <c>ContextMenu</c> popup owner,
    /// and a popup owned by anything other than the icon's own top-level host window is dismissed
    /// as soon as the user interacts with it. Asserting it as a contract - rather than reading it
    /// incidentally inside a lifecycle test - is what makes the edge verified.
    /// </remarks>
    [StaFact]
    public void Host_window_handle_is_available_as_a_popup_owner_after_registration()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(),
            ToolTipText = "popup owner",
        };

        // Lazy: nothing is created until the icon is actually shown.
        Assert.Equal(IntPtr.Zero, trayIcon.HostHandle);

        trayIcon.Visible = true;

        IntPtr host = trayIcon.HostHandle;

        Assert.NotEqual(IntPtr.Zero, host);
        Assert.True(Win32.IsWindow(host));
        Assert.Equal(host, Win32.GetAncestor(host, Win32.GA_ROOT));
        Assert.False(Win32.IsWindowVisible(host));

        // Every shell call for this icon named that handle, so the window the shell posts to and
        // the window S03 will own the menu with cannot drift apart.
        Assert.NotEmpty(shell.ShellNotifyIconDataSnapshots);
        Assert.All(shell.ShellNotifyIconDataSnapshots, data => Assert.Equal(host, data.hWnd));
    }

    /// <summary>
    /// The host forwards the raw message stream - message id, <c>wParam</c> and <c>lParam</c>
    /// unchanged - including messages nothing consumes yet.
    /// </summary>
    /// <remarks>
    /// This is the S01 to S02 edge, proven at the point S02 will consume it. S02 decodes the
    /// notification event from <c>LOWORD(lParam)</c> and the icon id from <c>HIWORD(lParam)</c>, so a
    /// host that filtered, remapped or dropped messages would silently break the decoding while
    /// this slice's own tests still passed. Because the policy-free forwarding is verified here,
    /// S02 must not need to modify or subclass the host window.
    /// </remarks>
    [StaFact]
    public void Raw_message_stream_reaches_the_consumer_callback()
    {
        var shell = new FakeShellApi();
        var received = new List<(uint Message, IntPtr WParam, IntPtr LParam)>();

        using var host = new TrayMessageWindow(shell, (message, wParam, lParam) => received.Add((message, wParam, lParam)));

        // A WM_USER-family id the host knows nothing about, to prove nothing is filtered.
        uint message = ShellConstants.WM_USER + 42;
        IntPtr wParam = new(0x1234);
        IntPtr lParam = new(0x00AB_CDEF);

        // A same-thread SendMessage runs the window procedure synchronously, so the callback has
        // already run when the call returns.
        Win32.SendMessage(host.Handle, message, wParam, lParam);

        (uint Message, IntPtr WParam, IntPtr LParam) single = Assert.Single(received);

        Assert.Equal(message, single.Message);
        Assert.Equal(wParam, single.WParam);
        Assert.Equal(lParam, single.LParam);
    }

    /// <summary>
    /// The callback message id is non-zero and distinct from <c>WM_USER</c> and from the real
    /// <c>TaskbarCreated</c> broadcast id, so S02 and S05 cannot misroute a message.
    /// </summary>
    /// <remarks>
    /// A collision is the kind of bug that reads as "the icon sometimes does not respond": the
    /// shell's notification callback would be handed to the Explorer-restart recovery path, or a
    /// broadcast would be decoded as a click event. The real registration is used for the second
    /// half of the assertion because a session atom is the id that will actually arrive.
    /// </remarks>
    [StaFact]
    public void Callback_message_id_is_distinct_from_TaskbarCreated_and_from_WM_USER()
    {
        var shell = new FakeShellApi();

        using var host = new TrayMessageWindow(shell, (_, _, _) => { });

        Assert.Equal(ShellConstants.TrayCallbackMessage, host.CallbackMessageId);
        Assert.NotEqual(0u, host.CallbackMessageId);
        Assert.NotEqual(ShellConstants.WM_USER, host.CallbackMessageId);
        Assert.NotEqual(FakeShellApi.DefaultRegisteredMessageId, host.CallbackMessageId);

        // The host does not mistake its own callback id for the broadcast it registered for.
        Assert.False(host.IsTaskbarCreatedMessage(host.CallbackMessageId));

        // And the real, session-wide id cannot collide with it either.
        uint realTaskbarCreated = new ShellApi().RegisterWindowMessage("TaskbarCreated");

        Assert.NotEqual(0u, realTaskbarCreated);
        Assert.NotEqual(realTaskbarCreated, host.CallbackMessageId);
    }

    /// <summary>
    /// The dismissal contract that sits beside the popup-owner assertion above: a popup that really
    /// dismisses on an outside click is anchored to a dedicated anchor window, and explicitly not to
    /// the registration host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the S01 to S03 edge in its measured form, and it is the boundary record of the S03
    /// finding: the assertion above is still true and still useful - the host handle is a legal
    /// <c>HWND</c> for <c>NOTIFYICONIDENTIFIER.hWnd</c> and is what the shell posts to - but it is
    /// <b>not</b> what owns a dismissable menu. A menu opened from the host shape with no placement
    /// target gets an owner of <see cref="IntPtr.Zero"/> and never closes; a menu attached to
    /// <see cref="TrayMenuAnchorWindow"/>'s 1x1 laid-out visual dismisses on an outside click.
    /// </para>
    /// <para>
    /// The behavioural half is proven in full, together with the failing constructions pinned as
    /// negative cases, in <c>TrayMenuDismissalTests</c>; here the same measurement is asserted once,
    /// as a contract, so a future slice that replaces the anchor finds the boundary red instead of
    /// discovering the loss mid-implementation.
    /// </para>
    /// <para>
    /// <b>Why the owner is not asserted as an equality here (finding F5).</b> The popup's
    /// <c>GW_OWNER</c> is assigned inside WPF's <c>Popup.BuildWindow</c>, and only when the placement
    /// target resolves to an <c>HwndSource</c> that is connected to the foreground window at that
    /// instant. Measured while this contract failed in a full-suite run
    /// (<c>docs/UAT-S03.md</c>, F5): the anchor was the foreground window and the thread's active
    /// window before the open and again after it, its visual was laid out, the popup carried the
    /// expected rectangle - and its owner was still <c>0x0</c>, with the outside click dismissing it
    /// anyway. So the boundary asserts the two outcomes WPF's own code can produce (the anchor, or
    /// nothing) and pins the one window it can never be, the registration host, while the dismissal
    /// clause stays unconditional.
    /// </para>
    /// </remarks>
    [StaFact]
    public void A_dismissable_popup_is_owned_by_the_anchor_window_not_by_the_registration_host()
    {
        TrayMenuScenarioResult result = TrayMenuScenario.Run(MenuPlacementTargetStrategy.AnchorWindow);

        Assert.Single(result.PopupWindows);

        // Two distinct windows with two different jobs: the host the shell knows, and the anchor a
        // popup can be dismissed through.
        Assert.NotEqual(IntPtr.Zero, result.AnchorHandle);
        Assert.NotEqual(result.HostHandle, result.AnchorHandle);

        // The anchor or nothing, never the host - see the remarks for the WPF code path and the
        // measurement that put this boundary where it is.
        Assert.True(
            result.PopupOwnerHandle == result.AnchorHandle || result.PopupOwnerHandle == IntPtr.Zero,
            $"The popup's owner must be the anchor window or absent, never another window. {result.Describe()}");
        Assert.NotEqual(result.HostHandle, result.PopupOwnerHandle);

        // The unconditional clause, and the one the consumer can see: the anchored popup closes on
        // an outside click and leaves no window behind.
        Assert.True(result.IsOpenBeforeOutsideClick, $"The menu should have opened. {result.Describe()}");
        Assert.False(result.IsOpenAfterOutsideClick,
            $"A popup owned by the anchor window must dismiss on an outside click. {result.Describe()}");
        Assert.Empty(result.PopupWindowsAfterOutsideClick);
    }

    /// <summary>
    /// Explorer-restart recovery re-registers by <c>NIM_ADD</c> plus <c>NIM_SETVERSION</c> with the
    /// retained handle, and does nothing else at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the S04 and S03 to S05 edge asserted as a contract. Recovery runs inside a window
    /// procedure the shell invoked, so every clause here is a structural property of the recorded
    /// call sequence rather than a value a caller could notice late:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The pair is an add plus a version call, never a <c>NIM_MODIFY</c>-only update - a
    /// modification cannot create a registration that a restarted shell no longer holds.
    /// </description></item>
    /// <item><description>
    /// The re-add carries no <c>NIF_INFO</c> and no balloon text, so recovery cannot resurrect a
    /// balloon the consumer showed before the restart (S04's contract check 7).
    /// </description></item>
    /// <item><description>
    /// No icon handle is created or destroyed, so a recovered icon is the same icon - which is what
    /// keeps the process GDI count flat across a restart (R007) and the click pipeline's identity
    /// intact.
    /// </description></item>
    /// <item><description>
    /// The protocol version is re-selected on the new registration, because the shell does not
    /// persist it across an add.
    /// </description></item>
    /// </list>
    /// </remarks>
    [StaFact]
    public void Explorer_restart_recovery_re_adds_and_touches_nothing_else()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(),
            ToolTipText = "recovery contract",
        };

        trayIcon.Visible = true;

        IntPtr handleBefore = trayIcon.RegisteredIconHandle;
        int createdBefore = shell.CreatedIcons;
        int callsBefore = shell.ShellNotifyIconCalls.Count;
        int seamCallsBefore = shell.Calls.Count;

        Win32.SendMessage(trayIcon.HostHandle, FakeShellApi.DefaultRegisteredMessageId, IntPtr.Zero, IntPtr.Zero);

        IReadOnlyList<ShellCall> recovery = [.. shell.ShellNotifyIconCalls.Skip(callsBefore)];
        IReadOnlyList<ShellCall> recoveryWindow = [.. shell.Calls.Skip(seamCallsBefore)];

        Assert.Equal(2, recovery.Count);
        Assert.Equal(ShellConstants.NIM_ADD, recovery[0].Message);
        Assert.Equal(ShellConstants.NIM_SETVERSION, recovery[1].Message);
        Assert.DoesNotContain(recovery, call => call.Message == ShellConstants.NIM_MODIFY);
        Assert.DoesNotContain(recovery, call => (call.Flags & ShellConstants.NIF_INFO) != 0);

        // Scoped to the recovery itself: the initial registration legitimately converts the icon,
        // so only what happened after the broadcast may show a handle being created or released.
        Assert.DoesNotContain(recoveryWindow, call => call.Operation == nameof(IShellApi.CreateIconIndirect));
        Assert.DoesNotContain(recoveryWindow, call => call.Operation == nameof(IShellApi.DestroyIcon));

        Assert.Equal(createdBefore, shell.CreatedIcons);
        Assert.Equal(handleBefore, trayIcon.RegisteredIconHandle);
        Assert.Equal(ShellConstants.NOTIFYICON_VERSION_4, shell.ShellNotifyIconDataSnapshots[callsBefore + 1].uTimeoutOrVersion);
    }

    /// <summary>Builds a square, single-colour, fully opaque image.</summary>
    /// <returns>The image.</returns>
    private static BitmapSource CreateSolid()
    {
        var pixels = new byte[IconSize * IconSize * 4];

        for (int i = 0; i < IconSize * IconSize; i++)
        {
            pixels[(i * 4) + 0] = 0x30;
            pixels[(i * 4) + 1] = 0x60;
            pixels[(i * 4) + 2] = 0x90;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return BitmapSource.Create(IconSize, IconSize, 96, 96, PixelFormats.Bgra32, null, pixels, IconSize * 4);
    }
}
