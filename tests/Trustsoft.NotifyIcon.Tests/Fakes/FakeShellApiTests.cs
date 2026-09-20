using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the observable behaviour of <see cref="FakeShellApi"/>: the call-recording shape the
/// lifecycle tests assert against, the failure-injection contract and the ownership counters.
/// </summary>
/// <remarks>
/// <para>
/// The fake is test infrastructure that later tasks depend on for their evidence, so its own
/// contract is verified here rather than assumed. If the scripted-failure semantics or the
/// counters drift, the lifecycle and leak tests would still pass while proving nothing - the
/// worst possible failure mode for a suite whose whole job is to prove the shell was called
/// correctly and nothing leaked.
/// </para>
/// <para>
/// None of these tests touch the real shell: the fake exists precisely so a failure can be
/// injected, which a live notification area cannot do.
/// </para>
/// </remarks>
public sealed class FakeShellApiTests
{
    /// <summary>
    /// The sequence the lifecycle task asserts: <c>NIM_ADD</c> first, then
    /// <c>NIM_SETVERSION</c>, with the flags of each call recorded as they were at that moment.
    /// </summary>
    [Fact]
    public void Records_shell_notify_icon_calls_in_order_with_message_and_flags()
    {
        var fake = new FakeShellApi();
        var data = NOTIFYICONDATAW.Create(new IntPtr(0x1234), 1);
        data.uFlags = ShellConstants.NIF_MESSAGE | ShellConstants.NIF_ICON | ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;

        Assert.True(fake.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data));

        data.uFlags = 0;
        data.uTimeoutOrVersion = ShellConstants.NOTIFYICON_VERSION_4;

        Assert.True(fake.ShellNotifyIcon(ShellConstants.NIM_SETVERSION, ref data));

        IReadOnlyList<ShellCall> calls = fake.ShellNotifyIconCalls;

        Assert.Equal(2, calls.Count);
        Assert.Equal(nameof(IShellApi.ShellNotifyIcon), calls[0].Operation);
        Assert.Equal(ShellConstants.NIM_ADD, calls[0].Message);
        Assert.Equal(
            ShellConstants.NIF_MESSAGE | ShellConstants.NIF_ICON | ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP,
            calls[0].Flags);
        Assert.Equal(ShellConstants.NIM_SETVERSION, calls[1].Message);
        Assert.Equal(0u, calls[1].Flags);

        // The version is not a flag and does not fit the flat record, so the snapshot list is
        // the surface for it - and that is exactly the "SETVERSION(4)" half of the contract.
        Assert.Equal(2, fake.ShellNotifyIconDataSnapshots.Count);
        Assert.Equal(ShellConstants.NOTIFYICON_VERSION_4, fake.ShellNotifyIconDataSnapshots[1].uTimeoutOrVersion);
    }

    /// <summary>
    /// Each recorded call carries the structure as it was at call time, not a view that changes
    /// when the caller mutates its local instance again.
    /// </summary>
    /// <remarks>
    /// The lifecycle code reuses one <see cref="NOTIFYICONDATAW"/> for the add and the
    /// setversion, so a fake that stored a reference rather than a copy would make every
    /// assertion about the earlier call silently wrong.
    /// </remarks>
    [Fact]
    public void Snapshots_the_structure_at_call_time_and_keeps_later_mutations_out()
    {
        var fake = new FakeShellApi();
        var data = NOTIFYICONDATAW.Create(new IntPtr(0xABCD), 9);
        data.uFlags = ShellConstants.NIF_ICON;

        fake.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data);

        data.uFlags = ShellConstants.NIF_TIP;
        data.szTip = "changed afterwards";

        fake.ShellNotifyIcon(ShellConstants.NIM_MODIFY, ref data);

        Assert.Equal(ShellConstants.NIF_ICON, fake.ShellNotifyIconDataSnapshots[0].uFlags);
        Assert.Equal(string.Empty, fake.ShellNotifyIconDataSnapshots[0].szTip);
        Assert.Equal(ShellConstants.NIF_TIP, fake.ShellNotifyIconDataSnapshots[1].uFlags);
        Assert.Equal("changed afterwards", fake.ShellNotifyIconDataSnapshots[1].szTip);
        Assert.Equal(ShellConstants.NIF_ICON, fake.ShellNotifyIconCalls[0].Flags);

        // The host window and icon id are identical across both calls: only the mutated fields
        // differ, which is what makes the earlier snapshot a real record of the earlier call.
        Assert.Equal(new IntPtr(0xABCD), fake.ShellNotifyIconDataSnapshots[0].hWnd);
        Assert.Equal(new IntPtr(0xABCD), fake.ShellNotifyIconDataSnapshots[1].hWnd);
        Assert.Equal(9u, fake.ShellNotifyIconDataSnapshots[1].uID);
    }

    /// <summary>
    /// <c>FailNext</c> fails exactly one call of the named operation and leaves calls to other
    /// operations alone.
    /// </summary>
    [Fact]
    public void FailNext_fails_exactly_one_call_of_that_operation_only()
    {
        var fake = new FakeShellApi();
        var data = NOTIFYICONDATAW.Create(new IntPtr(0x1), 1);

        fake.FailNext(ShellOperation.ShellNotifyIcon);

        // A different operation must not consume the scheduled failure.
        Assert.Equal(FakeShellApi.DefaultRegisteredMessageId, fake.RegisterWindowMessage("TaskbarCreated"));

        Assert.False(fake.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data));
        Assert.True(fake.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data));

        // Both attempts are visible: a failed call must never be invisible in the log.
        Assert.Equal(2, fake.ShellNotifyIconCalls.Count);
    }

    /// <summary>
    /// <c>FailAlways</c> keeps failing across a retry - the "the shell will not cooperate" case
    /// that the retry-once-then-surface policy needs - until it is stopped or cleared.
    /// </summary>
    [Fact]
    public void FailAlways_fails_every_call_until_stopped_or_cleared()
    {
        var fake = new FakeShellApi();
        var data = NOTIFYICONDATAW.Create(new IntPtr(0x1), 1);

        fake.FailAlways(ShellOperation.ShellNotifyIcon);

        Assert.False(fake.ShellNotifyIcon(ShellConstants.NIM_MODIFY, ref data));
        Assert.False(fake.ShellNotifyIcon(ShellConstants.NIM_MODIFY, ref data));
        Assert.False(fake.ShellNotifyIcon(ShellConstants.NIM_MODIFY, ref data));

        fake.StopFailingAlways(ShellOperation.ShellNotifyIcon);
        Assert.True(fake.ShellNotifyIcon(ShellConstants.NIM_MODIFY, ref data));

        fake.FailAlways(ShellOperation.ShellNotifyIcon);
        fake.FailNext(ShellOperation.ShellNotifyIcon);
        fake.ClearScriptedFailures();
        Assert.True(fake.ShellNotifyIcon(ShellConstants.NIM_MODIFY, ref data));
    }

    /// <summary>
    /// A scripted failure reports the configured Win32 code, and a success clears it so a test
    /// cannot accidentally assert on a stale error.
    /// </summary>
    [Fact]
    public void Failed_call_reports_the_configured_error_and_success_clears_it()
    {
        var fake = new FakeShellApi { LastErrorToReport = 5 };
        var data = NOTIFYICONDATAW.Create(IntPtr.Zero, 1);

        fake.FailNext(ShellOperation.ShellNotifyIcon);

        Assert.False(fake.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data));
        Assert.Equal(5, fake.GetLastError());

        Assert.True(fake.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data));
        Assert.Equal(0, fake.GetLastError());

        // Reading the error is itself a seam call, so the lifecycle's "capture the error
        // immediately" step is observable in the same log as the call it belongs to.
        Assert.Contains(fake.Calls, call => call.Operation == nameof(IShellApi.GetLastError));
    }

    /// <summary>
    /// The failure values match what the real APIs return: <see cref="IntPtr.Zero"/> for a
    /// failed creation, <see langword="false"/> for a failed destroy or delete.
    /// </summary>
    [Fact]
    public void Failed_calls_return_the_real_failure_values_and_are_not_counted()
    {
        var fake = new FakeShellApi();
        var iconInfo = new ICONINFO { fIcon = true, hbmMask = new IntPtr(0xAA), hbmColor = new IntPtr(0xBB) };

        fake.FailAlways(ShellOperation.CreateIconIndirect);

        Assert.Equal(IntPtr.Zero, fake.CreateIconIndirect(ref iconInfo));
        Assert.Equal(fake.LastErrorToReport, fake.GetLastError());
        Assert.Equal(0, fake.CreatedIcons);
        Assert.Equal(0, fake.OutstandingIcons);

        fake.StopFailingAlways(ShellOperation.CreateIconIndirect);
        IntPtr icon = fake.CreateIconIndirect(ref iconInfo);
        Assert.Equal(FakeShellApi.DefaultIconHandle, icon);

        fake.FailNext(ShellOperation.DestroyIcon);
        Assert.False(fake.DestroyIcon(icon));
        Assert.Equal(0, fake.DestroyedIcons);

        Assert.True(fake.DestroyIcon(icon));
        Assert.Equal(1, fake.DestroyedIcons);

        fake.FailNext(ShellOperation.DeleteObject);
        Assert.False(fake.DeleteObject(new IntPtr(0xAA)));
        Assert.Equal(0, fake.DeletedObjects);

        Assert.True(fake.DeleteObject(new IntPtr(0xAA)));
        Assert.Equal(1, fake.DeletedObjects);
    }

    /// <summary>
    /// Registered message names are recorded verbatim and a failure returns <c>0</c>, which is
    /// the value a consumer must treat as a hard failure.
    /// </summary>
    [Fact]
    public void RegisterWindowMessage_records_names_and_returns_zero_when_failed()
    {
        var fake = new FakeShellApi();

        Assert.Equal(FakeShellApi.DefaultRegisteredMessageId, fake.RegisterWindowMessage("TaskbarCreated"));
        Assert.Equal(new[] { "TaskbarCreated" }, fake.RegisteredMessages);

        fake.FailAlways(ShellOperation.RegisterWindowMessage);

        Assert.Equal(0u, fake.RegisterWindowMessage("TaskbarCreated"));
        Assert.Equal(2, fake.RegisteredMessages.Count);
        Assert.Equal(2, fake.Calls.Count(call => call.Operation == nameof(IShellApi.RegisterWindowMessage)));
    }

    /// <summary>
    /// Ownership counters are measurable and each created icon gets a distinct handle, which is
    /// what makes "the previously retained icon was not destroyed" assertable.
    /// </summary>
    [Fact]
    public void Counters_track_created_destroyed_and_deleted_objects_with_distinct_handles()
    {
        var fake = new FakeShellApi();
        var iconInfo = new ICONINFO { fIcon = true, hbmMask = new IntPtr(0xA1), hbmColor = new IntPtr(0xB1) };

        IntPtr first = fake.CreateIconIndirect(ref iconInfo);
        IntPtr second = fake.CreateIconIndirect(ref iconInfo);

        Assert.NotEqual(first, second);
        Assert.Equal(new[] { first, second }, fake.CreatedIconHandles);
        Assert.Equal(2, fake.CreatedIcons);
        Assert.Equal(0, fake.DestroyedIcons);
        Assert.Equal(2, fake.OutstandingIcons);

        Assert.True(fake.DestroyIcon(first));

        Assert.Equal(1, fake.DestroyedIcons);
        Assert.Equal(1, fake.OutstandingIcons);
        Assert.Equal(0, fake.DeletedObjects);
    }

    /// <summary>
    /// The GDI counter is scriptable, so a test can drive the seam path without a real process
    /// measurement, and a failure reports the count as <c>0</c> the way the real call does.
    /// </summary>
    [Fact]
    public void GetGuiResources_returns_the_scripted_count_and_records_the_selector()
    {
        var fake = new FakeShellApi { GdiObjectCount = 42 };

        Assert.Equal(42u, fake.GetGuiResources(Win32.GetCurrentProcess(), ShellConstants.GR_GDIOBJECTS));

        ShellCall call = Assert.Single(fake.Calls);
        Assert.Equal(nameof(IShellApi.GetGuiResources), call.Operation);
        Assert.Equal(ShellConstants.GR_GDIOBJECTS, call.Flags);

        fake.FailAlways(ShellOperation.GetGuiResources);
        Assert.Equal(0u, fake.GetGuiResources(Win32.GetCurrentProcess(), ShellConstants.GR_GDIOBJECTS));
    }
}
