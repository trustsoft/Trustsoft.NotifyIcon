using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The <c>NOTIFYICONIDENTIFIER</c> structure from <c>shellapi.h</c>, in exact header field order.
/// It names one notification-area icon for <c>Shell_NotifyIconGetRect</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ground truth is the SDK header</b> (<c>um\shellapi.h</c>, <c>_NOTIFYICONIDENTIFIER</c>), not
/// the rendered documentation, which is the same rule <see cref="NOTIFYICONDATAW"/> follows:
/// </para>
/// <code>
/// typedef struct _NOTIFYICONIDENTIFIER {
///     DWORD cbSize;   // 4
///     HWND  hWnd;     // 8 on x64
///     UINT  uID;      // 4
///     GUID  guidItem; // 16
/// } NOTIFYICONIDENTIFIER, *PNOTIFYICONIDENTIFIER;
/// </code>
/// <para>
/// Marshalled layout on x64 (default 8-byte packing):
/// </para>
/// <list type="table">
///   <item><term>cbSize</term><description>0 (4), then 4 bytes of padding</description></item>
///   <item><term>hWnd</term><description>8 (8), required 8-byte alignment for a pointer</description></item>
///   <item><term>uID</term><description>16 (4)</description></item>
///   <item><term>guidItem</term><description>20 (16)</description></item>
/// </list>
/// <para>
/// Total marshalled size: <b>40</b> bytes on x64 (36 bytes of members, padded to the structure's
/// 8-byte alignment). This is the value <see cref="Create"/> writes into <see cref="cbSize"/> and
/// the shell validates: a wrong <c>cbSize</c> makes the call fail outright.
/// </para>
/// <para>
/// <b>There is no padding between <c>uID</c> and <c>guidItem</c>.</b> A <c>GUID</c> is four 4-byte
/// chunks followed by eight bytes, so its alignment requirement is 4, not 8; <c>uID</c> ends at
/// 20 and the GUID starts exactly there. Assuming an 8-byte-aligned GUID (and therefore a
/// <c>guidItem</c> at 24) is the plausible-looking mistake this layout invites, and it is why the
/// offsets are pinned by <c>NotifyIconIdentifierLayoutTests</c> in a read-back direction rather
/// than recalled here: only the marshaller's answer counts.
/// </para>
/// <para>
/// <b>Which fields identify the icon.</b> The documented rule is that when
/// <see cref="guidItem"/> is <c>GUID_NULL</c>, the icon is identified by
/// <see cref="hWnd"/> plus <see cref="uID"/>; when it is not <c>GUID_NULL</c> the shell ignores
/// both of those and identifies the icon by the GUID alone. This library registers its icons with
/// <see cref="NOTIFYICONDATAW.uID"/> and never with a GUID, so <see cref="Create"/> always writes
/// <see cref="GuidNull"/> and the window/id pair is the identity.
/// </para>
/// <para>
/// The type is <see langword="internal"/> and is not part of the shipping API (D002/D015); the
/// fields are <see langword="public"/> so the layout tests can read them by reflection, exactly as
/// <see cref="NOTIFYICONDATAW"/> does.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NOTIFYICONIDENTIFIER
{
    /// <summary>
    /// <c>GUID_NULL</c> (all zeros): the value that makes the shell identify the icon by
    /// <see cref="hWnd"/> and <see cref="uID"/> instead of by a GUID.
    /// </summary>
    /// <remarks>
    /// Declared as a field rather than a <see langword="const"/> because <see cref="Guid"/> has no
    /// compile-time constant form. It exists so the GUID_NULL rule has one named home instead of a
    /// bare <c>default</c> at each construction site.
    /// </remarks>
    internal static readonly Guid GuidNull = Guid.Empty;

    /// <summary>
    /// Size of this structure in bytes, written by <see cref="Create"/> as
    /// <see cref="SizeOf"/>. The shell rejects a call whose <c>cbSize</c> it does not recognise.
    /// </summary>
    public uint cbSize;

    /// <summary>
    /// The window that registered the icon - for this library, the tray host window. Ignored by
    /// the shell when <see cref="guidItem"/> is not <see cref="GuidNull"/>.
    /// </summary>
    public IntPtr hWnd;

    /// <summary>
    /// The application-defined icon id that was registered for <see cref="hWnd"/>. Ignored by the
    /// shell when <see cref="guidItem"/> is not <see cref="GuidNull"/>.
    /// </summary>
    public uint uID;

    /// <summary>
    /// The icon's GUID, <c>GUID_NULL</c> in this library. A non-null value makes the shell ignore
    /// <see cref="hWnd"/> and <see cref="uID"/> entirely, which would silently address a different
    /// icon than the caller named.
    /// </summary>
    public Guid guidItem;

    /// <summary>
    /// Returns the marshalled size of this structure in bytes (40 on x64), ready to be assigned to
    /// <see cref="cbSize"/>.
    /// </summary>
    /// <returns>The value the shell must see in <c>cbSize</c>.</returns>
    internal static uint SizeOf() => (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>();

    /// <summary>
    /// Creates a fully initialised identifier for the given host window and icon id.
    /// </summary>
    /// <param name="hWnd">The window that registered the icon.</param>
    /// <param name="iconId">The icon id registered for <paramref name="hWnd"/>.</param>
    /// <returns>
    /// A structure whose <c>cbSize</c> is already <see cref="SizeOf"/> and whose
    /// <see cref="guidItem"/> is <see cref="GuidNull"/>, so that <paramref name="hWnd"/> and
    /// <paramref name="iconId"/> are what the shell uses to find the icon.
    /// </returns>
    /// <remarks>
    /// <b>This is a construction helper, not a validator.</b> A null <paramref name="hWnd"/> is
    /// marshalled as-is; rejecting an unregistered window is the shell's business, and doing it
    /// here would turn a caller's bad handle into a managed exception at the wrong layer. The
    /// shell call for such an identifier fails with a non-zero <c>HRESULT</c> instead, which is
    /// what <c>ShellNotifyIconGetRectTests</c> pins.
    /// </remarks>
    internal static NOTIFYICONIDENTIFIER Create(IntPtr hWnd, uint iconId) => new()
    {
        cbSize = SizeOf(),
        hWnd = hWnd,
        uID = iconId,

        // GUID_NULL is not decoration: with a non-null GUID the shell ignores hWnd and uID and
        // looks the icon up by GUID, which this library never registers.
        guidItem = GuidNull,
    };
}
