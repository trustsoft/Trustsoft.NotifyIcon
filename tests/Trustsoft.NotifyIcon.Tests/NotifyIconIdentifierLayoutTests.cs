using System.Runtime.InteropServices;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the unmanaged layout of <see cref="NOTIFYICONIDENTIFIER"/> and
/// <see cref="NativeRect"/> against the Windows SDK headers (<c>shellapi.h</c> and
/// <c>windef.h</c>).
/// </summary>
/// <remarks>
/// <para>
/// These two structs agree with the shell about where every field lives, or the placement call
/// fails in a way that is hard to attribute: <c>Shell_NotifyIconGetRect</c> validates
/// <c>cbSize</c> and reads the window/id pair through this layout, so a field that lands four
/// bytes late is not an exception but a failed lookup, and a rectangle read at the wrong offsets
/// is a menu positioned from noise.
/// </para>
/// <para>
/// <b>The offsets are derived from the header, and the marshaller's answer is the only evidence
/// that counts.</b> For <c>NOTIFYICONIDENTIFIER</c> the derived x64 layout is
/// <c>cbSize</c> 0 (4), 4 bytes of padding, <c>hWnd</c> 8 (8), <c>uID</c> 16 (4),
/// <c>guidItem</c> 20 (16), total 36 bytes padded to the structure's 8-byte alignment = <b>40</b>.
/// There is deliberately <em>no</em> padding before <c>guidItem</c>: a <c>GUID</c> is four 4-byte
/// chunks plus eight bytes and therefore aligns to 4, so the plausible assumption that it aligns
/// to 8 (and starts at 24) is exactly the mistake these assertions exist to catch.
/// </para>
/// <para>
/// <b>Size alone cannot catch that mistake</b> (the same gap <c>NotifyIconDataLayoutTests</c> and
/// <c>IconInfoLayoutTests</c> close, MEM022), so every field is also proven in both marshalling
/// directions: populated managed instance to native bytes, and native bytes back to a managed
/// instance. A declaration whose order or alignment disagrees with the header fails one of those
/// round trips even when <see cref="Marshal.SizeOf{T}()"/> is unchanged.
/// </para>
/// <para>
/// The offsets are x64-specific because <see cref="IntPtr"/> is 8 bytes there; the assertions
/// return early on a 32-bit host instead of failing for an unrelated reason.
/// </para>
/// </remarks>
public sealed class NotifyIconIdentifierLayoutTests
{
    /// <summary><c>sizeof(NOTIFYICONIDENTIFIER)</c> on x64.</summary>
    private const int IdentifierSize = 40;

    /// <summary><c>offsetof(NOTIFYICONIDENTIFIER, guidItem)</c> on x64: <c>uID</c> ends at 20 and a GUID aligns to 4.</summary>
    private const int GuidItemOffset = 20;

    /// <summary>
    /// The marshalled identifier is 40 bytes on x64 and <see cref="NOTIFYICONIDENTIFIER.SizeOf"/>
    /// reports the same number, so <c>cbSize</c> is never a value recalled by hand.
    /// </summary>
    [Fact]
    public void Identifier_size_is_40_on_x64()
    {
        // shellapi.h (NOTIFYICONIDENTIFIER): DWORD + HWND + UINT + GUID, padded to 8-byte alignment.
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(IdentifierSize, Marshal.SizeOf<NOTIFYICONIDENTIFIER>());
        Assert.Equal((uint)IdentifierSize, NOTIFYICONIDENTIFIER.SizeOf());
    }

    /// <summary>
    /// The four fields sit at the header's offsets: <c>cbSize</c> 0, <c>hWnd</c> 8, <c>uID</c> 16,
    /// <c>guidItem</c> 20.
    /// </summary>
    /// <remarks>
    /// <c>hWnd</c> at 8 is the padding evidence (the 4-byte <c>cbSize</c> cannot be followed
    /// directly by an 8-byte pointer); <c>guidItem</c> at 20 is the alignment evidence (a GUID
    /// needs 4-byte alignment only, so nothing is inserted before it). A declaration that inserted
    /// padding before the GUID - <c>guidItem</c> at 24 - would pass the size assertion and fail
    /// this one.
    /// </remarks>
    [Fact]
    public void Identifier_field_offsets_match_the_header()
    {
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(0, Marshal.OffsetOf<NOTIFYICONIDENTIFIER>(nameof(NOTIFYICONIDENTIFIER.cbSize)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<NOTIFYICONIDENTIFIER>(nameof(NOTIFYICONIDENTIFIER.hWnd)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<NOTIFYICONIDENTIFIER>(nameof(NOTIFYICONIDENTIFIER.uID)).ToInt32());
        Assert.Equal(GuidItemOffset, Marshal.OffsetOf<NOTIFYICONIDENTIFIER>(nameof(NOTIFYICONIDENTIFIER.guidItem)).ToInt32());

        // The window handle is pointer-sized: it starts at 8 and the id immediately follows it,
        // which is only true when the handle occupies exactly IntPtr.Size bytes.
        Assert.Equal(8 + IntPtr.Size, Marshal.OffsetOf<NOTIFYICONIDENTIFIER>(nameof(NOTIFYICONIDENTIFIER.uID)).ToInt32());

        // The GUID immediately follows the id, with no padding in between.
        Assert.Equal(16 + sizeof(uint), GuidItemOffset);
    }

    /// <summary>
    /// Writes a fully populated identifier to native memory and reads every field back at its
    /// header offset with the primitive readers, which is what proves the field order rather than
    /// the total size.
    /// </summary>
    [Fact]
    public void Identifier_fields_land_at_the_header_offsets_when_marshalled()
    {
        if (IntPtr.Size != 8)
        {
            return;
        }

        var handle = new IntPtr(0x00007FF6_1234ABCD);
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = 0x00000028,
            hWnd = handle,
            uID = 0x0000BEEF,
            guidItem = guid,
        };

        IntPtr buffer = Marshal.AllocHGlobal(IdentifierSize);

        try
        {
            Marshal.StructureToPtr(identifier, buffer, fDeleteOld: false);

            // Offset 0: cbSize, a 4-byte DWORD.
            Assert.Equal(0x00000028, Marshal.ReadInt32(buffer, 0));

            // Offset 8: the pointer. Read as a pointer and not as a truncated 32-bit value.
            Assert.Equal(handle, Marshal.ReadIntPtr(buffer, 8));

            // Offset 16: uID.
            Assert.Equal(0x0000BEEF, Marshal.ReadInt32(buffer, 16));

            // Offset 20: the GUID's sixteen bytes, in the order a GUID is laid out natively.
            var guidBytes = new byte[16];
            Marshal.Copy(buffer + GuidItemOffset, guidBytes, 0, guidBytes.Length);
            Assert.Equal(guid.ToByteArray(), guidBytes);

            // The whole instance survives the round trip, so no field is a write-only artifact of
            // where the allocation happens to sit.
            NOTIFYICONIDENTIFIER roundTripped = Marshal.PtrToStructure<NOTIFYICONIDENTIFIER>(buffer);
            Assert.Equal(identifier.cbSize, roundTripped.cbSize);
            Assert.Equal(identifier.hWnd, roundTripped.hWnd);
            Assert.Equal(identifier.uID, roundTripped.uID);
            Assert.Equal(identifier.guidItem, roundTripped.guidItem);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Populates native memory field by field at the header's offsets and reads it back as a
    /// managed identifier, which is the direction that catches a declaration whose order differs
    /// from the header's while its size is right.
    /// </summary>
    [Fact]
    public void Identifier_fields_are_read_back_from_the_header_offsets()
    {
        if (IntPtr.Size != 8)
        {
            return;
        }

        var handle = new IntPtr(0x00000000_00C0FFEE);
        var guid = new Guid("ffeeddcc-bbaa-9988-7766-554433221100");

        IntPtr buffer = Marshal.AllocHGlobal(IdentifierSize);

        try
        {
            for (int i = 0; i < IdentifierSize; i++)
            {
                Marshal.WriteByte(buffer, i, 0xEE);
            }

            Marshal.WriteInt32(buffer, 0, 0x00000028);
            Marshal.WriteIntPtr(buffer, 8, handle);
            Marshal.WriteInt32(buffer, 16, 0x0000CAFE);
            Marshal.Copy(guid.ToByteArray(), 0, buffer + GuidItemOffset, 16);

            NOTIFYICONIDENTIFIER identifier = Marshal.PtrToStructure<NOTIFYICONIDENTIFIER>(buffer);

            Assert.Equal(0x00000028u, identifier.cbSize);
            Assert.Equal(handle, identifier.hWnd);
            Assert.Equal(0x0000CAFEu, identifier.uID);
            Assert.Equal(guid, identifier.guidItem);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// <see cref="NOTIFYICONIDENTIFIER.Create"/> writes the marshalled size, the caller's window
    /// and id, and the GUID_NULL rule, and never a non-null GUID (which would make the shell ignore
    /// both the window and the id and look the icon up by GUID instead).
    /// </summary>
    [Fact]
    public void Create_sets_cbSize_window_id_and_leaves_guid_null()
    {
        var handle = new IntPtr(0x00000000_1234ABCD);

        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(handle, 0x0000BEEF);

        Assert.Equal(NOTIFYICONIDENTIFIER.SizeOf(), identifier.cbSize);
        Assert.Equal((uint)IdentifierSize, identifier.cbSize);
        Assert.NotEqual(0u, identifier.cbSize);
        Assert.Equal(handle, identifier.hWnd);
        Assert.Equal(0x0000BEEFu, identifier.uID);

        // The GUID_NULL rule, asserted through both the named constant and the raw value: a
        // non-null GUID here would silently change which icon the shell is asked about.
        Assert.Equal(Guid.Empty, NOTIFYICONIDENTIFIER.GuidNull);
        Assert.Equal(Guid.Empty, identifier.guidItem);

        // The built identifier is marshallable as-is: the value the shell reads is the value the
        // test asserted above, not a number that only exists in managed memory.
        IntPtr buffer = Marshal.AllocHGlobal(IdentifierSize);

        try
        {
            Marshal.StructureToPtr(identifier, buffer, fDeleteOld: false);

            Assert.Equal((uint)IdentifierSize, (uint)Marshal.ReadInt32(buffer, 0));
            Assert.Equal(handle, Marshal.ReadIntPtr(buffer, 8));
            Assert.Equal(0x0000BEEF, Marshal.ReadInt32(buffer, 16));

            var guidBytes = new byte[16];
            Marshal.Copy(buffer + GuidItemOffset, guidBytes, 0, guidBytes.Length);
            Assert.Equal(new byte[16], guidBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// <see cref="NOTIFYICONIDENTIFIER.Create"/> is a construction helper and does not reject a
    /// null window handle: the shell is what refuses an identifier for a window that never
    /// registered the icon, and it refuses it by returning a failing <c>HRESULT</c> - not by
    /// throwing out of managed code and not by failing at construction time.
    /// </summary>
    /// <remarks>
    /// Pinned here because "does <c>Create</c> validate?" is a question a caller has to be able to
    /// answer from the API: a helper that raised on a null handle would move a caller's bad-handle
    /// mistake into an unrelated layer and would make the failure untestable through the seam.
    /// </remarks>
    [Fact]
    public void Create_does_not_reject_a_null_window_handle()
    {
        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(IntPtr.Zero, 3);

        Assert.Equal(IntPtr.Zero, identifier.hWnd);
        Assert.Equal(3u, identifier.uID);
        Assert.Equal(NOTIFYICONIDENTIFIER.SizeOf(), identifier.cbSize);
        Assert.Equal(Guid.Empty, identifier.guidItem);
    }

    /// <summary>
    /// <c>RECT</c> is four 4-byte signed coordinates at 0, 4, 8 and 12 - 16 bytes in total - and
    /// the values survive both marshalling directions.
    /// </summary>
    [Fact]
    public void NativeRect_layout_and_round_trip_match_the_header()
    {
        Assert.Equal(16, Marshal.SizeOf<NativeRect>());
        Assert.Equal(0, Marshal.OffsetOf<NativeRect>(nameof(NativeRect.left)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<NativeRect>(nameof(NativeRect.top)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<NativeRect>(nameof(NativeRect.right)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<NativeRect>(nameof(NativeRect.bottom)).ToInt32());

        // Negative coordinates are legitimate (a monitor left of or above the primary one), so the
        // round trip uses them: an unsigned declaration would turn -1920 into a huge positive.
        var rectangle = new NativeRect { left = -1920, top = -120, right = -960, bottom = 960 };

        IntPtr buffer = Marshal.AllocHGlobal(16);

        try
        {
            Marshal.StructureToPtr(rectangle, buffer, fDeleteOld: false);

            Assert.Equal(-1920, Marshal.ReadInt32(buffer, 0));
            Assert.Equal(-120, Marshal.ReadInt32(buffer, 4));
            Assert.Equal(-960, Marshal.ReadInt32(buffer, 8));
            Assert.Equal(960, Marshal.ReadInt32(buffer, 12));

            NativeRect roundTripped = Marshal.PtrToStructure<NativeRect>(buffer);
            Assert.Equal(rectangle.left, roundTripped.left);
            Assert.Equal(rectangle.top, roundTripped.top);
            Assert.Equal(rectangle.right, roundTripped.right);
            Assert.Equal(rectangle.bottom, roundTripped.bottom);
            Assert.False(roundTripped.IsEmpty);

            // And the read direction: bytes written straight into native memory become the fields.
            Marshal.WriteInt32(buffer, 0, 100);
            Marshal.WriteInt32(buffer, 4, 200);
            Marshal.WriteInt32(buffer, 8, 116);
            Marshal.WriteInt32(buffer, 12, 216);

            NativeRect fromBytes = Marshal.PtrToStructure<NativeRect>(buffer);
            Assert.Equal(100, fromBytes.left);
            Assert.Equal(200, fromBytes.top);
            Assert.Equal(116, fromBytes.right);
            Assert.Equal(216, fromBytes.bottom);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// <see cref="NativeRect.IsEmpty"/> is true exactly when the rectangle encloses nothing: a
    /// zero-width or zero-height extent, either sign of inverted extent, and the default value.
    /// </summary>
    /// <remarks>
    /// This predicate is what makes "the shell could not locate the icon" a named state instead of
    /// a guess, so its boundary cases are pinned rather than assumed: <c>right == left</c> is empty
    /// (a zero-width rectangle encloses nothing, exactly as <c>IsRectEmpty</c> says) and so is the
    /// all-zero default that a failed call leaves behind.
    /// </remarks>
    [Fact]
    public void NativeRect_empty_predicate_covers_every_boundary()
    {
        Assert.True(default(NativeRect).IsEmpty);
        Assert.True(new NativeRect { left = 0, top = 0, right = 0, bottom = 0 }.IsEmpty);

        // Zero width and zero height, with non-zero origins.
        Assert.True(new NativeRect { left = 10, top = 20, right = 10, bottom = 40 }.IsEmpty);
        Assert.True(new NativeRect { left = 10, top = 20, right = 40, bottom = 20 }.IsEmpty);

        // Inverted extents, the state a bogus identifier or a partially written structure leaves.
        Assert.True(new NativeRect { left = 40, top = 20, right = 10, bottom = 40 }.IsEmpty);
        Assert.True(new NativeRect { left = 10, top = 40, right = 40, bottom = 20 }.IsEmpty);

        // One pixel wide or tall is not empty.
        Assert.False(new NativeRect { left = 10, top = 20, right = 11, bottom = 40 }.IsEmpty);
        Assert.False(new NativeRect { left = 10, top = 20, right = 40, bottom = 21 }.IsEmpty);

        // A tray icon's real rectangle, in negative virtual-screen coordinates.
        Assert.False(new NativeRect { left = -1928, top = -8, right = -1920, bottom = 0 }.IsEmpty);
    }
}
