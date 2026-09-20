using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The <c>NOTIFYICONDATAW</c> structure from <c>shellapi.h</c>, in exact header field order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ground truth is the SDK header</b> (<c>um\shellapi.h</c>, <c>_NOTIFYICONDATAW</c>), not the
/// rendered documentation. The documentation is wrong for this struct: it declares
/// <c>szTip</c> as <c>CHAR[64]</c> while the header unambiguously declares
/// <c>WCHAR szTip[128]</c>. Every size and offset below is derived from the header's field
/// order and the platform's default 8-byte packing, and is pinned by
/// <c>NotifyIconDataLayoutTests</c>.
/// </para>
/// <para>
/// Why this matters more than it looks: a wrong field order, a missing
/// <see cref="CharSet.Unicode"/>, or a packing mistake produces a struct whose
/// <see cref="cbSize"/> disagrees with what the shell expects. The shell then either ignores the
/// call or reads garbage, and the user-visible symptom is "the icon just does not appear" with
/// no exception thrown anywhere. The layout assertions retire that entire risk class before a
/// single Win32 call is made.
/// </para>
/// <para>
/// Marshalled layout on x64 (packing 8, <see cref="CharSet.Unicode"/>):
/// </para>
/// <list type="table">
///   <item><term>cbSize</term><description>0 (4)</description></item>
///   <item><term>hWnd</term><description>8 (8)</description></item>
///   <item><term>uID</term><description>16 (4)</description></item>
///   <item><term>uFlags</term><description>20 (4)</description></item>
///   <item><term>uCallbackMessage</term><description>24 (4), 4 bytes of padding follow</description></item>
///   <item><term>hIcon</term><description>32 (8)</description></item>
///   <item><term>szTip</term><description>40 (256 = 128 WCHAR)</description></item>
///   <item><term>dwState</term><description>296 (4)</description></item>
///   <item><term>dwStateMask</term><description>300 (4)</description></item>
///   <item><term>szInfo</term><description>304 (512 = 256 WCHAR)</description></item>
///   <item><term>uTimeoutOrVersion</term><description>816 (4)</description></item>
///   <item><term>szInfoTitle</term><description>820 (128 = 64 WCHAR)</description></item>
///   <item><term>dwInfoFlags</term><description>948 (4)</description></item>
///   <item><term>guidItem</term><description>952 (16)</description></item>
///   <item><term>hBalloonIcon</term><description>968 (8)</description></item>
/// </list>
/// <para>
/// Total marshalled size: <b>976</b> bytes on x64, which is also
/// <c>NOTIFYICONDATAW_V3_SIZE</c> plus the <c>hBalloonIcon</c> member as defined by the header's
/// <c>FIELD_OFFSET</c> macros.
/// </para>
/// <para>
/// The 976-byte size is what the modern Windows 10/11 shells accept for every operation this
/// library performs, so a single <see cref="cbSize"/> value is correct here and no
/// version-dependent size branching is needed.
/// </para>
/// <para>
/// The type is <see langword="internal"/>; the fields are <see langword="public"/> so the layout
/// tests can read them (including their <see cref="MarshalAsAttribute"/>) through reflection.
/// Nothing here is part of the shipping public API.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATAW
{
    /// <summary>
    /// Size of this structure in bytes, written by <see cref="Create"/> as
    /// <see cref="SizeOf"/>. The shell rejects calls whose <c>cbSize</c> it does not recognise.
    /// </summary>
    public uint cbSize;

    /// <summary>The window that receives the notification-area callback message.</summary>
    public IntPtr hWnd;

    /// <summary>The application-defined icon identifier, unique per <see cref="hWnd"/>.</summary>
    public uint uID;

    /// <summary>
    /// A combination of the <c>NIF_*</c> bits from <see cref="ShellConstants"/>, selecting which
    /// of the remaining members are valid for the current call.
    /// </summary>
    public uint uFlags;

    /// <summary>
    /// The application-defined message the shell posts to <see cref="hWnd"/>, derived from
    /// <see cref="ShellConstants.TrayCallbackMessage"/>.
    /// </summary>
    public uint uCallbackMessage;

    /// <summary>The icon handle to display. Owned by the caller, not by the shell.</summary>
    public IntPtr hIcon;

    /// <summary>
    /// The tooltip text, <c>WCHAR szTip[128]</c> in the header.
    /// </summary>
    /// <remarks>
    /// <b>Pass <see cref="string.Empty"/>, never <see langword="null"/>.</b> A
    /// <c>ByValTStr</c> field is marshalled by copying the string into the inline buffer, and a
    /// <see langword="null"/> reference is a marshalling trap rather than an empty tip. Use
    /// <see cref="Create"/> so this cannot be got wrong. Note also that under
    /// <see cref="ShellConstants.NOTIFYICON_VERSION_4"/> the shell suppresses the standard
    /// tooltip unless <see cref="ShellConstants.NIF_SHOWTIP"/> is set.
    /// </remarks>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;

    /// <summary>Icon state bits, e.g. <see cref="ShellConstants.NIS_HIDDEN"/>.</summary>
    public uint dwState;

    /// <summary>Mask selecting which bits of <see cref="dwState"/> are examined.</summary>
    public uint dwStateMask;

    /// <summary>
    /// The balloon notification text, <c>WCHAR szInfo[256]</c> in the header. Initialise to
    /// <see cref="string.Empty"/> for the same reason as <see cref="szTip"/>.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;

    /// <summary>
    /// The header's anonymous union (<c>uTimeout</c> / <c>uVersion</c>) collapsed into the single
    /// field that occupies the union's offset.
    /// </summary>
    /// <remarks>
    /// The header declares <c>union { UINT uTimeout; UINT uVersion; }</c>; both members live at
    /// the same offset, so one field is the faithful C# representation and adding a second field
    /// would silently shift every member after it. Interpretation is decided by the message:
    /// written as the protocol version for <see cref="ShellConstants.NIM_SETVERSION"/> (this
    /// library writes <see cref="ShellConstants.NOTIFYICON_VERSION_4"/>) and as the balloon
    /// timeout in milliseconds for <see cref="ShellConstants.NIF_INFO"/> (consumed by S04).
    /// </remarks>
    public uint uTimeoutOrVersion;

    /// <summary>
    /// The balloon notification title, <c>WCHAR szInfoTitle[64]</c> in the header. Initialise to
    /// <see cref="string.Empty"/> for the same reason as <see cref="szTip"/>.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;

    /// <summary>The balloon icon flags (<c>NIIF_*</c>). Consumed by S04.</summary>
    public uint dwInfoFlags;

    /// <summary>The icon's GUID, used instead of <see cref="uID"/> when <c>NIF_GUID</c> is set. Not used by S01.</summary>
    public Guid guidItem;

    /// <summary>The balloon's custom icon handle. Consumed by S04.</summary>
    public IntPtr hBalloonIcon;

    /// <summary>
    /// Returns the marshalled size of this structure in bytes (976 on x64), ready to be assigned
    /// to <see cref="cbSize"/>.
    /// </summary>
    /// <returns>The value the shell must see in <c>cbSize</c>.</returns>
    internal static int SizeOf() => Marshal.SizeOf<NOTIFYICONDATAW>();

    /// <summary>
    /// Creates a fully initialised structure for the given host window and icon id.
    /// </summary>
    /// <param name="hWnd">The host window that receives the callback message.</param>
    /// <param name="uID">The icon id, unique per <paramref name="hWnd"/>.</param>
    /// <returns>
    /// A structure whose <c>cbSize</c> is already correct, whose three <c>ByValTStr</c> fields are
    /// empty strings (never <see langword="null"/>) and whose flags/message members are zero so
    /// the caller sets only what the current operation needs.
    /// </returns>
    internal static NOTIFYICONDATAW Create(IntPtr hWnd, uint uID) => new()
    {
        // cbSize is a DWORD; SizeOf() returns the CLR int that Marshal.SizeOf produces.
        cbSize = (uint)SizeOf(),
        hWnd = hWnd,
        uID = uID,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };
}
