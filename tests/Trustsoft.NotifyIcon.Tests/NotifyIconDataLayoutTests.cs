using System.Reflection;
using System.Runtime.InteropServices;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the unmanaged layout of <see cref="NOTIFYICONDATAW"/> against the Windows SDK header
/// <c>shellapi.h</c>.
/// </summary>
/// <remarks>
/// <para>
/// These assertions are the cheapest risk retirement in the slice. A wrong field order, a
/// missing <see cref="CharSet.Unicode"/>, or a packing mistake gives the shell a struct whose
/// <c>cbSize</c> disagrees with what it expects; the shell then ignores the call or reads
/// garbage, and the symptom is "the icon just does not appear" with no exception raised anywhere.
/// </para>
/// <para>
/// <b>Source of truth is the header, not the documentation.</b> The rendered documentation for
/// <c>NOTIFYICONDATAW</c> declares <c>szTip</c> as <c>CHAR[64]</c>, while
/// <c>Windows Kits\10\Include\10.0.26100.0\um\shellapi.h</c> declares <c>WCHAR szTip[128]</c>.
/// </para>
/// <para>
/// Every offset below is for x64 (8-byte pointers, default 8-byte packing: <c>hIcon</c> is pushed
/// to offset 32 by 4 bytes of padding after <c>uCallbackMessage</c>, and <c>hBalloonIcon</c> sits
/// at 968 because the <c>Guid</c> before it occupies 16 bytes). Because the managed struct's
/// size varies with <c>IntPtr.Size</c>, the offset assertions are skipped on a 32-bit host
/// instead of failing for an unrelated reason; on x64 - the only configuration this library
/// targets - they all run.
/// </para>
/// </remarks>
public sealed class NotifyIconDataLayoutTests
{
    /// <summary>
    /// <c>sizeof(NOTIFYICONDATAW)</c> on x64 must be 976 bytes. A different value means a size
    /// mismatch between <c>cbSize</c> and what the shell expects, which is the failure that makes
    /// the icon silently not appear.
    /// </summary>
    [Fact]
    public void SizeOf_is_976_on_x64()
    {
        // shellapi.h (NOTIFYICONDATAW): full v4 size on x64.
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(976, Marshal.SizeOf<NOTIFYICONDATAW>());
        Assert.Equal(976, NOTIFYICONDATAW.SizeOf());
    }

    /// <summary>
    /// <c>guidItem</c> must land at offset 952 - i.e. after <c>dwInfoFlags</c> at 948 and before
    /// the 16-byte GUID, not at the offset a mistaken <c>szInfo</c> length would produce.
    /// </summary>
    [Fact]
    public void GuidItem_offset_is_952()
    {
        // shellapi.h (NOTIFYICONDATAW): GUID guidItem.
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal((IntPtr)952, Marshal.OffsetOf<NOTIFYICONDATAW>(nameof(NOTIFYICONDATAW.guidItem)));
    }

    /// <summary>
    /// <c>hBalloonIcon</c> must land at offset 968, which also proves <c>guidItem</c> is 16 bytes
    /// wide and correctly aligned.
    /// </summary>
    [Fact]
    public void HBalloonIcon_offset_is_968()
    {
        // shellapi.h (NOTIFYICONDATAW): HICON hBalloonIcon.
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal((IntPtr)968, Marshal.OffsetOf<NOTIFYICONDATAW>(nameof(NOTIFYICONDATAW.hBalloonIcon)));
    }

    /// <summary>
    /// <c>szTip</c> must be a 128-character wide field starting at offset 40: 24 bytes of scalar
    /// members (<c>cbSize</c> 4 + <c>hWnd</c> 8 + <c>uID</c> 4 + <c>uFlags</c> 4 +
    /// <c>uCallbackMessage</c> 4), 4 bytes of padding, then <c>hIcon</c> (8).
    /// </summary>
    [Fact]
    public void SzTip_is_a_128_char_wide_field()
    {
        // shellapi.h (NOTIFYICONDATAW): WCHAR szTip[128] - the documentation's CHAR[64] is wrong.
        if (IntPtr.Size != 8)
        {
            return;
        }

        // Derived, not pasted: the five scalar members before hIcon, plus hIcon itself, plus the
        // 4 bytes of padding alignment inserts before hIcon.
        int scalarMembersBeforeHIcon = (sizeof(uint) * 5) + IntPtr.Size;
        int paddingBeforeHIcon = 8 - (scalarMembersBeforeHIcon % 8);
        int expectedOffset = scalarMembersBeforeHIcon + paddingBeforeHIcon + IntPtr.Size;

        Assert.Equal(40, expectedOffset);
        Assert.Equal((IntPtr)expectedOffset, Marshal.OffsetOf<NOTIFYICONDATAW>(nameof(NOTIFYICONDATAW.szTip)));

        MarshalAsAttribute? marshalling = typeof(NOTIFYICONDATAW)
            .GetField(nameof(NOTIFYICONDATAW.szTip))!
            .GetCustomAttribute<MarshalAsAttribute>();

        Assert.NotNull(marshalling);
        Assert.Equal(UnmanagedType.ByValTStr, marshalling!.Value);
        Assert.Equal(128, marshalling.SizeConst);
    }

    /// <summary>
    /// <c>dwState</c> must start immediately after the 256-byte <c>szTip</c> buffer, at 296.
    /// </summary>
    [Fact]
    public void DwState_offset_is_296()
    {
        // shellapi.h (NOTIFYICONDATAW): DWORD dwState, immediately after WCHAR szTip[128].
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal((IntPtr)296, Marshal.OffsetOf<NOTIFYICONDATAW>(nameof(NOTIFYICONDATAW.dwState)));
    }

    /// <summary>
    /// <c>szInfo</c> must be a 256-character wide field at offset 304: 296 for <c>dwState</c>,
    /// 300 for <c>dwStateMask</c>, then the buffer. This is the assertion that catches a reduced
    /// <c>szTip</c> or <c>szInfo</c> size, because either mistake moves this offset.
    /// </summary>
    [Fact]
    public void SzInfo_offset_is_304()
    {
        // shellapi.h (NOTIFYICONDATAW): WCHAR szInfo[256], after dwState and dwStateMask.
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal((IntPtr)304, Marshal.OffsetOf<NOTIFYICONDATAW>(nameof(NOTIFYICONDATAW.szInfo)));

        MarshalAsAttribute? marshalling = typeof(NOTIFYICONDATAW)
            .GetField(nameof(NOTIFYICONDATAW.szInfo))!
            .GetCustomAttribute<MarshalAsAttribute>();

        Assert.NotNull(marshalling);
        Assert.Equal(UnmanagedType.ByValTStr, marshalling!.Value);
        Assert.Equal(256, marshalling.SizeConst);
    }

    /// <summary>
    /// The collapsed union member must sit at the offset the header's
    /// <c>union { uTimeout; uVersion; }</c> occupies: 816, immediately after <c>szInfo</c>.
    /// A second field instead of this single one would push <c>szInfoTitle</c> to 820 and make
    /// the whole 976-byte size impossible.
    /// </summary>
    [Fact]
    public void UTimeoutOrVersion_offset_is_816()
    {
        // shellapi.h (NOTIFYICONDATAW): anonymous union of UINT uTimeout / UINT uVersion.
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(
            (IntPtr)816,
            Marshal.OffsetOf<NOTIFYICONDATAW>(nameof(NOTIFYICONDATAW.uTimeoutOrVersion)));
    }

    /// <summary>
    /// <c>dwInfoFlags</c> must land at 948, which pins the 64-character <c>szInfoTitle</c> buffer
    /// at 820 and, together with <c>guidItem</c> at 952, reproduces the header's
    /// <c>NOTIFYICONDATAW_V2_SIZE</c>.
    /// </summary>
    [Fact]
    public void DwInfoFlags_offset_is_948()
    {
        // shellapi.h (NOTIFYICONDATAW): DWORD dwInfoFlags, after WCHAR szInfoTitle[64].
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal((IntPtr)948, Marshal.OffsetOf<NOTIFYICONDATAW>(nameof(NOTIFYICONDATAW.dwInfoFlags)));
    }

    /// <summary>
    /// The sole factory used to build the struct must produce a marshallable instance: correct
    /// <c>cbSize</c>, the caller's window/id, and - critically - empty strings rather than
    /// <see langword="null"/> for the three <c>ByValTStr</c> fields.
    /// </summary>
    [Fact]
    public void Create_sets_cbSize_and_initialises_string_fields()
    {
        var window = new IntPtr(0x1234);
        NOTIFYICONDATAW data = NOTIFYICONDATAW.Create(window, 7);

        Assert.Equal((uint)NOTIFYICONDATAW.SizeOf(), data.cbSize);
        Assert.Equal(window, data.hWnd);
        Assert.Equal(7u, data.uID);
        Assert.Equal(string.Empty, data.szTip);
        Assert.Equal(string.Empty, data.szInfo);
        Assert.Equal(string.Empty, data.szInfoTitle);
        Assert.Equal(0u, data.uFlags);
        Assert.Equal(0u, data.uCallbackMessage);

        // The real proof that nulls cannot slip through: marshal the instance and read it back.
        IntPtr buffer = Marshal.AllocHGlobal(NOTIFYICONDATAW.SizeOf());
        try
        {
            Marshal.StructureToPtr(data, buffer, fDeleteOld: false);
            NOTIFYICONDATAW roundTripped = Marshal.PtrToStructure<NOTIFYICONDATAW>(buffer);

            Assert.Equal(string.Empty, roundTripped.szTip);
            Assert.Equal(string.Empty, roundTripped.szInfo);
            Assert.Equal(string.Empty, roundTripped.szInfoTitle);
            Assert.Equal(window, roundTripped.hWnd);
            Assert.Equal(7u, roundTripped.uID);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
