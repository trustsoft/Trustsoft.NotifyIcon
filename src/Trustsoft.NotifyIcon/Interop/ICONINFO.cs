using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The <c>ICONINFO</c> structure from <c>winuser.h</c>, used by
/// <c>CreateIconIndirect</c> to build an <c>HICON</c> from two bitmaps.
/// </summary>
/// <remarks>
/// <para>
/// Header definition (<c>winuser.h</c>), which is the ground truth:
/// </para>
/// <code>
/// typedef struct _ICONINFO {
///     BOOL     fIcon;     // 4 bytes: TRUE = icon, FALSE = cursor
///     DWORD    xHotspot;  // 4 bytes: cursor hotspot x (unused for icons)
///     DWORD    yHotspot;  // 4 bytes: cursor hotspot y (unused for icons)
///     HBITMAP  hbmMask;   // and-mask bitmap
///     HBITMAP  hbmColor;  // colour bitmap
/// } ICONINFO, *PICONINFO;
/// </code>
/// <para>
/// Marshalled layout on x64 (default sequential packing): <c>fIcon</c> 0 (4),
/// <c>xHotspot</c> 4 (4), <c>yHotspot</c> 8 (4), 4 bytes of padding, <c>hbmMask</c> 16 (8),
/// <c>hbmColor</c> 24 (8) - <b>32 bytes in total</b>.
/// </para>
/// <para>
/// <see cref="fIcon"/> is a Win32 <c>BOOL</c>, which is 4 bytes, so it is declared as
/// <see cref="bool"/> with the default <c>UnmanagedType.Bool</c> marshalling. Declaring it as a
/// 1-byte value (<c>[MarshalAs(UnmanagedType.U1)]</c>) would make the marshaller write one byte
/// where the shell reads four - a silent, hard-to-see corruption of the field the shell
/// dispatches on. <c>IconInfoLayoutTests</c> pins both the size and the byte width of the field.
/// </para>
/// <para>
/// <b>Ownership:</b> <c>CreateIconIndirect</c> copies <see cref="hbmMask"/> and
/// <see cref="hbmColor"/>, so the caller keeps ownership of both bitmaps and must release them
/// with <c>DeleteObject</c>; the returned <c>HICON</c> is a separate object that must be
/// released with <c>DestroyIcon</c>.
/// </para>
/// <para>
/// <c>internal</c> by design: nothing in this file is part of the shipping public API, and the
/// struct never appears in a public signature.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    /// <summary>
    /// <see langword="true"/> for an icon, <see langword="false"/> for a cursor. Marshalled as a
    /// 4-byte Win32 <c>BOOL</c>.
    /// </summary>
    public bool fIcon;

    /// <summary>Cursor hotspot x coordinate. Unused for icons; set to 0.</summary>
    public int xHotspot;

    /// <summary>Cursor hotspot y coordinate. Unused for icons; set to 0.</summary>
    public int yHotspot;

    /// <summary>
    /// The AND mask bitmap. One bit per pixel, transparent pixels masked (bit set = transparent).
    /// Owned by the caller.
    /// </summary>
    public IntPtr hbmMask;

    /// <summary>The colour bitmap, 32bpp <c>BGRA</c>. Owned by the caller.</summary>
    public IntPtr hbmColor;
}
