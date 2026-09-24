using System.IO;
using System.Reflection;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for <see cref="ToastIdentity"/>, the AppUserModelID registration service built on
/// top of the <see cref="IToastApi"/> seam. They prove the four things T03 is responsible for: the
/// measured write sequence is produced in order, the read-back check turns a silent non-registration
/// into a named error, an explicit override id is used instead of the derived default, and removal
/// surfaces the Win32 error as data - all against the recording fake, never against the real shell.
/// </summary>
/// <remarks>
/// <para>
/// None of these tests touch the real shell or the real file system: they drive
/// <see cref="FakeToastApi"/>, which is what lets the read-back disagreement (the exact silent
/// failure the slice exists to rule out) be scripted deterministically. A real machine cannot be
/// forced to produce a shortcut that carries no property on demand; the fake can. The live
/// read-back on this machine is a separate measurement, <see cref="ToastIdentityLiveProbeTests"/>.
/// </para>
/// </remarks>
public sealed class ToastIdentityTests
{
    /// <summary>
    /// The measured identity write sequence, in the seam's canonical order (docs/TOAST-MEASUREMENT.md,
    /// and <c>FakeToastApiTests.IdentityWriteSequence</c>).
    /// </summary>
    private static readonly string[] IdentityWriteSequence =
    [
        nameof(IToastApi.CreateShellLink),
        nameof(IToastApi.ConfigureShortcut),
        nameof(IToastApi.GetShortcutPropertyStore),
        nameof(IToastApi.GetShortcutPersistFile),
        nameof(IToastApi.SetAppUserModelId),
        nameof(IToastApi.CommitPropertyStore),
        nameof(IToastApi.SaveShortcut),
    ];

    private const string ShortcutPath = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Test.App.lnk";

    /// <summary>
    /// A successful register writes the measured sequence, reads the id back from a fresh shell
    /// link, and releases every handle it acquired - five in all (three from the write, two from
    /// the read-back).
    /// </summary>
    [Fact]
    public void Register_writes_the_measured_sequence_reads_back_and_releases_every_handle()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = "Test.App" };
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.Register("Test.App", ShortcutPath);

        Assert.True(result.Success);
        Assert.Equal("Test.App", result.AppUserModelId);
        Assert.Equal(ShortcutPath, result.ShortcutPath);
        Assert.Equal(string.Empty, result.Operation);
        Assert.Equal(0, result.Code);

        // The write sequence is produced first, exactly as measured, with the commit before the
        // save (the ordering trap the slice's contract is built around).
        Assert.Equal(IdentityWriteSequence, fake.Operations.Take(IdentityWriteSequence.Length).ToArray());

        int commit = IndexOf(fake, nameof(IToastApi.CommitPropertyStore));
        int save = IndexOf(fake, nameof(IToastApi.SaveShortcut));
        Assert.True(commit < save, "the AppUserModelID must be committed before the shortcut is saved");

        // The read-back runs after the write, on a fresh shell link, never on the writer's object.
        int writeEnd = IndexOf(fake, nameof(IToastApi.SaveShortcut));
        int open = IndexOf(fake, nameof(IToastApi.OpenShellLink));
        int read = IndexOf(fake, nameof(IToastApi.GetAppUserModelId));
        Assert.True(writeEnd < open, "the read-back must open a fresh shell link after the write");
        Assert.True(open < read, "the read-back must read the property after opening the shortcut");

        // Three handles from the write (persist file, property store, shell link) and two from the
        // read-back (property store, shell link) - the disposal contract's observable half.
        Assert.Equal(5, fake.CallCount(ToastOperation.ReleaseHandle));
    }

    /// <summary>
    /// An explicit override id is written through <see cref="IToastApi.SetAppUserModelId"/> instead
    /// of the entry-assembly-derived default, and it is the value the read-back is checked against.
    /// </summary>
    [Fact]
    public void Register_uses_the_explicit_override_id_instead_of_the_derived_default()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = "Vendor.Custom.App" };
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.Register("Vendor.Custom.App", ShortcutPath);

        Assert.True(result.Success);
        Assert.Equal("Vendor.Custom.App", result.AppUserModelId);

        // The id handed to the property store is the override, not whatever the entry assembly is.
        ToastCall set = Assert.Single(fake.Calls.Where(call => call.Operation == nameof(IToastApi.SetAppUserModelId)));
        Assert.Contains("Vendor.Custom.App", set.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// When the shortcut exists but carries no property (read-back <see langword="null"/>), the
    /// registration fails with the named <see cref="ToastIdentity.OperationReadBackMismatch"/>
    /// rather than reporting success - the exact silent failure the read-back exists to catch.
    /// </summary>
    [Fact]
    public void Register_fails_with_a_named_error_when_the_shortcut_carries_no_property()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = null };
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.Register("Test.App", ShortcutPath);

        Assert.False(result.Success);
        Assert.Equal(ToastIdentity.OperationReadBackMismatch, result.Operation);
        Assert.Equal(0, result.Code);
        Assert.Null(result.AppUserModelId);
    }

    /// <summary>
    /// When the read-back carries a different value than was written, the registration fails with
    /// the named mismatch and the actual value is reported for diagnosis.
    /// </summary>
    [Fact]
    public void Register_fails_with_a_named_error_when_the_read_back_disagrees()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = "Some.Other.App" };
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.Register("Test.App", ShortcutPath);

        Assert.False(result.Success);
        Assert.Equal(ToastIdentity.OperationReadBackMismatch, result.Operation);
        Assert.Equal("Some.Other.App", result.AppUserModelId);
    }

    /// <summary>
    /// A failing write step surfaces as data - the seam member name plus its <c>HRESULT</c> - not as
    /// an exception and not as a swallowed success.
    /// </summary>
    [Fact]
    public void Register_surfaces_a_failing_write_step_as_the_member_name_and_hresult()
    {
        var fake = new FakeToastApi();
        fake.FailNext(ToastOperation.CommitPropertyStore);
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.Register("Test.App", ShortcutPath);

        Assert.False(result.Success);
        Assert.Equal(nameof(IToastApi.CommitPropertyStore), result.Operation);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, result.Code);
    }

    /// <summary>
    /// A failing read-back open (the shortcut cannot be opened) surfaces as the failing seam member
    /// name plus its <c>HRESULT</c>.
    /// </summary>
    [Fact]
    public void ReadBack_surfaces_an_open_failure_as_the_member_name_and_hresult()
    {
        var fake = new FakeToastApi();
        fake.FailNext(ToastOperation.OpenShellLink);
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.ReadBack(ShortcutPath);

        Assert.False(result.Success);
        Assert.Equal(nameof(IToastApi.OpenShellLink), result.Operation);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, result.Code);
    }

    /// <summary>
    /// A successful read of a shortcut with no property reports the read as successful with a
    /// <see langword="null"/> value - the caller (Register) is the layer that treats null as a
    /// mismatch, never the read itself.
    /// </summary>
    [Fact]
    public void ReadBack_reports_a_missing_property_as_a_successful_read_with_a_null_value()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = null };
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.ReadBack(ShortcutPath);

        Assert.True(result.Success);
        Assert.Null(result.AppUserModelId);
    }

    /// <summary>
    /// Removal deletes the shortcut through the seam and reports success.
    /// </summary>
    [Fact]
    public void Remove_deletes_the_shortcut_through_the_seam()
    {
        var fake = new FakeToastApi();
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.Remove(ShortcutPath);

        Assert.True(result.Success);
        Assert.Equal(1, fake.CallCount(ToastOperation.DeleteShortcut));

        ToastCall delete = Assert.Single(fake.Calls.Where(call => call.Operation == nameof(IToastApi.DeleteShortcut)));
        Assert.Contains(ShortcutPath, delete.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failing delete surfaces the Win32 error as data - the <c>DeleteShortcut</c> member name
    /// plus the last-error code - exactly as the seam's Win32 failure channel requires.
    /// </summary>
    [Fact]
    public void Remove_surfaces_the_win32_error_when_the_delete_fails()
    {
        var fake = new FakeToastApi { LastErrorToReport = 5 };
        fake.FailAlways(ToastOperation.DeleteShortcut);
        var identity = new ToastIdentity(fake);

        ToastIdentityResult result = identity.Remove(ShortcutPath);

        Assert.False(result.Success);
        Assert.Equal(nameof(IToastApi.DeleteShortcut), result.Operation);
        Assert.Equal(5, result.Code);
    }

    /// <summary>
    /// The default id is the entry assembly's simple name, falling back to the library's own name
    /// only when there is no entry assembly to derive from.
    /// </summary>
    [Fact]
    public void DeriveDefaultAppUserModelId_returns_the_entry_assembly_name()
    {
        string derived = ToastIdentity.DeriveDefaultAppUserModelId();

        Assert.False(string.IsNullOrEmpty(derived));

        string? entryName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrEmpty(entryName))
        {
            Assert.Equal(entryName, derived);
        }
    }

    /// <summary>
    /// The default shortcut path lands the <c>.lnk</c> in the per-user Start menu, named after the
    /// identity it carries.
    /// </summary>
    [Fact]
    public void DefaultShortcutPath_puts_the_lnk_in_the_per_user_start_menu()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string expected = Path.Combine(programs, "Vendor.App.lnk");

        Assert.Equal(expected, ToastIdentity.DefaultShortcutPath("Vendor.App"));
    }

    private static int IndexOf(FakeToastApi fake, string operation) =>
        fake.Operations.ToList().IndexOf(operation);
}
