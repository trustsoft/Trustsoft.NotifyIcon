using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The <c>RECT</c> structure from <c>windef.h</c>, in exact header field order. It is the shape
/// the shell writes an icon's screen rectangle into.
/// </summary>
/// <remarks>
/// <para>
/// Header definition (<c>windef.h</c>, <c>_RECT</c>), which is the ground truth:
/// </para>
/// <code>
/// typedef struct tagRECT {
///     LONG left;
///     LONG top;
///     LONG right;
///     LONG bottom;
/// } RECT;
/// </code>
/// <para>
/// Marshalled layout: <c>left</c> 0, <c>top</c> 4, <c>right</c> 8, <c>bottom</c> 12, all 4-byte
/// signed values, <b>16 bytes in total</b>.
/// </para>
/// <para>
/// <b>Why a local type instead of reusing a probe's <c>RECT</c>.</b> The interactive probes under
/// <c>.gsd/</c> are diagnostics, not product code, and a type shared with them would drag a probe
/// declaration into the shipping assembly's layout surface. This is the declaration the library
/// marshals through, and <c>NotifyIconIdentifierLayoutTests</c> pins its offsets the same way the
/// identifier's are pinned.
/// </para>
/// <para>
/// <b>The right/bottom edge is exclusive.</b> A <c>RECT</c> is described by two corners, so width is
/// <c>right - left</c> and a rectangle whose <c>right</c> equals its <c>left</c> encloses nothing.
/// <see cref="IsEmpty"/> is the named form of that state, and it is what makes "the icon could not
/// be located" observable after a failed <c>Shell_NotifyIconGetRect</c> call instead of a guess
/// about uninitialised numbers.
/// </para>
/// <para>
/// The type is <see langword="internal"/> and is not part of the shipping public API (D002/D015);
/// the fields are <see langword="public"/> so the layout tests can read them by reflection, exactly
/// as <see cref="NOTIFYICONDATAW"/> does.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    /// <summary>The x coordinate of the rectangle's left edge, in physical screen pixels.</summary>
    public int left;

    /// <summary>The y coordinate of the rectangle's top edge, in physical screen pixels.</summary>
    public int top;

    /// <summary>The x coordinate of the rectangle's right edge (exclusive).</summary>
    public int right;

    /// <summary>The y coordinate of the rectangle's bottom edge (exclusive).</summary>
    public int bottom;

    /// <summary>
    /// Gets a value indicating whether this rectangle encloses no area, which is the state a
    /// caller must treat as "no rectangle was produced".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>right &lt;= left</c> or <c>bottom &lt;= top</c>, the same negative-or-zero-extent rule the
    /// Win32 <c>IsRectEmpty</c> macro applies, and <c>true</c> for an all-zero rectangle - which is
    /// exactly what a default-constructed <see cref="NativeRect"/> is.
    /// </para>
    /// <para>
    /// The predicate exists because the outcome of <c>Shell_NotifyIconGetRect</c> is an
    /// <c>HRESULT</c> that legitimately fails (an icon in the overflow flyout, or hidden), and an
    /// implementation that ignored the result and used the output rectangle anyway would compute a
    /// menu position from whatever numbers happened to be there. A named empty state makes that
    /// misuse visible in a test instead of being a silent placement bug.
    /// </para>
    /// </remarks>
    internal readonly bool IsEmpty => right <= left || bottom <= top;
}
