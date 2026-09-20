using System.Reflection;
using System.Runtime.InteropServices;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the marshalled layout of <see cref="ICONINFO"/> against <c>winuser.h</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ground truth is the SDK header (<c>um\winuser.h</c>, <c>_ICONINFO</c>), not the rendered
/// documentation. A layout mistake here does not fail loudly: <c>CreateIconIndirect</c> reads
/// whatever the struct says, produces a malformed icon, and the user-visible symptom is "the
/// icon looks wrong" with nothing thrown anywhere.
/// </para>
/// <para>
/// The checks are x64-specific (a pointer is 8 bytes, so the struct is 32 bytes), so they return
/// early on a 32-bit host instead of failing for an unrelated reason. The suite runs on x64.
/// </para>
/// </remarks>
public sealed class IconInfoLayoutTests
{
    /// <summary>
    /// <c>4 (BOOL fIcon) + 4 (xHotspot) + 4 (yHotspot) + 4 (padding) + 8 (hbmMask) + 8 (hbmColor)
    /// = 32</c> on x64, per <c>winuser.h</c>.
    /// </summary>
    [Fact]
    public void SizeOf_is_32_on_x64()
    {
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(32, Marshal.SizeOf<ICONINFO>());
    }

    /// <summary>
    /// Pins the field offsets and proves that <c>fIcon</c> is a 4-byte Win32 <c>BOOL</c> rather
    /// than a 1-byte value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The offsets come from the header's field order: <c>fIcon</c> 0, <c>xHotspot</c> 4,
    /// <c>yHotspot</c> 8, padding 12, <c>hbmMask</c> 16, <c>hbmColor</c> 24. <c>hbmMask == 16</c>
    /// is the layout evidence: a four-byte <c>fIcon</c> and four bytes of padding after
    /// <c>yHotspot</c> are both required for the first pointer-sized member to land on a
    /// 16-byte boundary.
    /// </para>
    /// <para>
    /// <b>The offsets do not prove the field width, and neither does the size.</b> Measured on
    /// this machine: a variant of this struct carrying
    /// <c>[MarshalAs(UnmanagedType.U1)] bool fIcon</c> still marshals to 32 bytes with
    /// <c>hbmMask</c> at 16, because the padding absorbs the three missing bytes - so a
    /// size-or-offset-only assertion would pass against exactly the mistake it is meant to
    /// catch. Two further checks close that gap:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///     The field carries no <see cref="MarshalAsAttribute"/>, i.e. the declaration asks for
    ///     the default <c>UnmanagedType.Bool</c> marshalling instead of overriding it to one
    ///     byte.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     A native-to-managed read proves the width at runtime: with the field's bytes set to
    ///     <c>00 FF FF FF</c>, a four-byte <c>BOOL</c> read yields the non-zero value
    ///     <c>0xFFFFFF00</c> and marshals to <see langword="true"/>, while a one-byte read would
    ///     see <c>0x00</c> and yield <see langword="false"/>. The all-zero control read proves
    ///     the result is genuinely derived from those bytes.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The write direction cannot be probed with a pre-filled buffer: <c>Marshal.StructureToPtr</c>
    /// zero-initialises the destination region (measured: the padding at offset 12 comes back as
    /// <c>0</c> even when the buffer was filled with <c>0xFF</c>), so a sentinel placed at
    /// offsets 1-3 does not survive. The field width is a single property of the field's
    /// marshalling metadata and is exercised in both directions by the same declaration, so the
    /// read-direction proof plus the absent attribute is the strongest available evidence.
    /// </para>
    /// </remarks>
    [Fact]
    public void FIcon_field_is_a_four_byte_BOOL()
    {
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(0, Marshal.OffsetOf<ICONINFO>(nameof(ICONINFO.fIcon)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<ICONINFO>(nameof(ICONINFO.xHotspot)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<ICONINFO>(nameof(ICONINFO.yHotspot)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<ICONINFO>(nameof(ICONINFO.hbmMask)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<ICONINFO>(nameof(ICONINFO.hbmColor)).ToInt32());

        FieldInfo fIcon = typeof(ICONINFO).GetField(nameof(ICONINFO.fIcon))!;
        Assert.Null(fIcon.GetCustomAttribute<MarshalAsAttribute>());

        int size = Marshal.SizeOf<ICONINFO>();
        IntPtr fourByteField = Marshal.AllocHGlobal(size);
        IntPtr zeroedField = Marshal.AllocHGlobal(size);

        try
        {
            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(fourByteField, i, 0xFF);
                Marshal.WriteByte(zeroedField, i, 0x00);
            }

            // Field bytes 00 FF FF FF: only a four-byte read sees a non-zero BOOL.
            Marshal.WriteByte(fourByteField, 0, 0x00);

            Assert.True(Marshal.PtrToStructure<ICONINFO>(fourByteField).fIcon);
            Assert.False(Marshal.PtrToStructure<ICONINFO>(zeroedField).fIcon);
        }
        finally
        {
            Marshal.FreeHGlobal(fourByteField);
            Marshal.FreeHGlobal(zeroedField);
        }
    }
}
