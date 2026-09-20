using System.Windows;
using System.Windows.Input;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// A notification-area click decoded from the shell's
/// <see cref="ShellConstants.TrayCallbackMessage"/> callback payload.
/// </summary>
/// <param name="Button">The mouse button the shell reported.</param>
/// <param name="ClickCount">The number of clicks: <c>1</c>, or <c>2</c> for a double click.</param>
/// <param name="ScreenAnchor">
/// The anchor point the shell reported, in screen device pixels. Its <c>x</c> is negative on a
/// monitor left of the primary one.
/// </param>
/// <param name="RawEvent">
/// The raw <c>LOWORD(lParam)</c> event code this value was decoded from, kept so callers can trace
/// or log the exact shell event without re-deriving it.
/// </param>
/// <remarks>
/// <para>
/// This is an interop-level value, not public API: it is the decoder's output and the raiser's
/// input, and it exists so the decoding and the raising can be tested apart. The public
/// vocabulary - the args type and the routed events - is built from it in
/// <c>TrayIcon</c>, which is why the type stays <see langword="internal"/>.
/// </para>
/// <para>
/// <see cref="ClickCount"/> is carried rather than implied by the routed event, so a consumer that
/// holds a single handler for both left-click events can still tell a double click from a single
/// one without inspecting which event fired.
/// </para>
/// <para>
/// <see cref="ScreenAnchor"/> is in device pixels on purpose: it is what the shell sent, and any
/// conversion to WPF device-independent units is a presentation decision that belongs to the
/// consumer (menu placement, DPI handling), not to the decoder.
/// </para>
/// </remarks>
internal readonly record struct TrayMouseEvent(
    MouseButton Button,
    int ClickCount,
    Point ScreenAnchor,
    uint RawEvent);
