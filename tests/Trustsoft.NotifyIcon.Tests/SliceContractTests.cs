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
/// </remarks>
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
