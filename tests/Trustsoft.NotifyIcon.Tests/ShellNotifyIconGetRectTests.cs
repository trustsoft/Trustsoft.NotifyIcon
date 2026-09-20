using System.Reflection;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the seam contract of <see cref="IShellApi.ShellNotifyIconGetRect"/>: the raw
/// <c>HRESULT</c> polarity, the <c>ref</c>/<c>out</c> aliasing of its two parameters, and the
/// policy boundary the placement code will consume (a failure is a normal outcome that carries no
/// rectangle, not an exception).
/// </summary>
/// <remarks>
/// <para>
/// These tests are fake-backed and touch no live shell: the real export is probed separately in
/// <c>RealShellApiSignatureProbeTests</c>, which is the established home for
/// signature-against-the-real-DLL evidence. What is asserted here is the shape every consumer of
/// the seam programs against, including the one detail that is easy to get wrong by mimicry -
/// this member's success value is <c>0</c> while every <see langword="bool"/>-returning member on
/// the seam reports success as <see langword="true"/>.
/// </para>
/// <para>
/// <b>No icon is involved anywhere in this file.</b> The fake never talks to the shell, so these
/// assertions cannot disturb a notification area; the identifiers used are arbitrary
/// window/id pairs that were never registered.
/// </para>
/// </remarks>
public sealed class ShellNotifyIconGetRectTests
{
    /// <summary>A non-zero handle that names no window, used so nothing real can be addressed.</summary>
    private static readonly IntPtr BogusWindowHandle = new(0x00000000_DEADBEEF);

    /// <summary>The icon id combined with <see cref="BogusWindowHandle"/>; also never registered.</summary>
    private const uint BogusIconId = 0x0000BEEF;

    /// <summary>
    /// <c>E_FAIL</c>: the failure code a caller has to survive, and the fake's default result.
    /// </summary>
    private const int EFail = unchecked((int)0x80004005);

    /// <summary>
    /// The fake records the identifier it was handed and returns the scripted <c>HRESULT</c> and
    /// rectangle, which is the success path the placement code consumes.
    /// </summary>
    [Fact]
    public void Fake_records_the_identifier_and_returns_the_scripted_result_and_rectangle()
    {
        var shell = new FakeShellApi
        {
            GetRectResult = 0,
            GetRectRectangle = new NativeRect { left = 1760, top = 16, right = 1784, bottom = 40 },
        };

        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(BogusWindowHandle, BogusIconId);

        int result = shell.ShellNotifyIconGetRect(ref identifier, out NativeRect rectangle);

        Assert.Equal(0, result);
        Assert.Equal(1760, rectangle.left);
        Assert.Equal(16, rectangle.top);
        Assert.Equal(1784, rectangle.right);
        Assert.Equal(40, rectangle.bottom);
        Assert.False(rectangle.IsEmpty);

        // The call is in the log under the seam member's own name, so the placement path is
        // observable at the seam rather than only through its effect on the menu.
        Assert.Contains(
            shell.Calls,
            call => call.Operation == nameof(IShellApi.ShellNotifyIconGetRect));

        // The identifier the shell is asked about is the caller's window/id pair, and the GUID is
        // GUID_NULL so the pair is what identifies the icon.
        NOTIFYICONIDENTIFIER recorded = Assert.Single(shell.ShellNotifyIconGetRectIdentifiers);
        Assert.Equal(BogusWindowHandle, recorded.hWnd);
        Assert.Equal(BogusIconId, recorded.uID);
        Assert.Equal(NOTIFYICONIDENTIFIER.SizeOf(), recorded.cbSize);
        Assert.Equal(Guid.Empty, recorded.guidItem);
    }

    /// <summary>
    /// A failing <c>HRESULT</c> is a normal outcome: it does not throw, it is reported as a
    /// non-zero value, and it hands back no rectangle to place a menu with.
    /// </summary>
    /// <remarks>
    /// The live case this protects is an icon in the overflow flyout or a hidden icon: the shell
    /// cannot report a screen rectangle for something that is not displayed, and the library's
    /// documented answer is a fallback anchor, not an exception and not a menu positioned from
    /// whatever numbers happened to be in the output variable.
    /// </remarks>
    [Fact]
    public void Fake_reports_a_failing_HRESULT_without_throwing_and_without_a_rectangle()
    {
        var shell = new FakeShellApi();
        Assert.Equal(EFail, shell.GetRectResult);

        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(BogusWindowHandle, BogusIconId);

        int result = shell.ShellNotifyIconGetRect(ref identifier, out NativeRect rectangle);

        Assert.NotEqual(0, result);
        Assert.Equal(EFail, result);
        Assert.True(rectangle.IsEmpty);

        // Locating an icon is not registering or modifying one: no Shell_NotifyIcon call is made
        // alongside it, so the placement path cannot be the reason an icon appears or changes.
        Assert.DoesNotContain(
            shell.Calls,
            call => call.Operation == nameof(IShellApi.ShellNotifyIcon));
    }

    /// <summary>
    /// <c>S_OK</c> is <b>0</b>, which is the opposite of the convention every
    /// <see langword="bool"/>-returning member on this seam follows: reading the result as a
    /// <see langword="bool"/>/truthiness would call success a failure.
    /// </summary>
    /// <remarks>
    /// This is the one interop detail in the slice that is easy to get wrong by mimicry, so the
    /// relationship is pinned rather than described: success is exactly zero, failure is exactly
    /// non-zero, and the two are never conflated with <see langword="true"/>/<see langword="false"/>.
    /// </remarks>
    [Fact]
    public void Success_is_zero_and_failure_is_non_zero_the_opposite_of_the_bool_returning_members()
    {
        var shell = new FakeShellApi { GetRectResult = 0 };
        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(BogusWindowHandle, BogusIconId);

        int success = shell.ShellNotifyIconGetRect(ref identifier, out _);

        // S_OK. A naive `if (result)` treats 0 as false and would report every success as a
        // failure - the trap the XML remarks on the interface member name explicitly.
        Assert.Equal(0, success);
        Assert.False(success != 0);

        // ...and the same argument the bool-returning members use would invert this call.
        Assert.True(0 == success);

        shell.GetRectResult = EFail;

        int failure = shell.ShellNotifyIconGetRect(ref identifier, out _);
        Assert.NotEqual(0, failure);
    }

    /// <summary>
    /// The identifier is passed <b>by reference</b>: a write the implementation makes lands on the
    /// caller's own instance, while the recorded snapshot keeps the value the caller sent.
    /// </summary>
    /// <remarks>
    /// A by-value signature would compile, would record the same call and would return the same
    /// <c>HRESULT</c>; only a write-back distinguishes it. The contract mirrors the one
    /// <see cref="IShellApi.ShellNotifyIcon"/> documents for its input/output structure - the
    /// implementation must hand the caller's own instance through, never a copy.
    /// </remarks>
    [Fact]
    public void Identifier_is_passed_by_reference_so_a_write_is_visible_to_the_caller()
    {
        var shell = new FakeShellApi { GetRectResult = 0, IdentifierIdWriteBack = 0x0000FEED };

        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(BogusWindowHandle, BogusIconId);
        Assert.Equal(BogusIconId, identifier.uID);

        shell.ShellNotifyIconGetRect(ref identifier, out _);

        // Visible on the caller's own variable: this can only happen through the ref parameter.
        Assert.Equal(0x0000FEEDu, identifier.uID);

        // The log is a snapshot taken at call time (the value the caller actually sent), which is
        // a logging property and deliberately not a view of the caller's variable.
        Assert.Equal(BogusIconId, Assert.Single(shell.ShellNotifyIconGetRectIdentifiers).uID);
    }

    /// <summary>
    /// The output rectangle reaches the caller's own variable: the value the implementation wrote
    /// is the value the caller reads.
    /// </summary>
    [Fact]
    public void Rectangle_is_an_out_parameter_the_caller_observes_directly()
    {
        var shell = new FakeShellApi
        {
            GetRectResult = 0,
            GetRectRectangle = new NativeRect { left = -1928, top = -8, right = -1920, bottom = 0 },
        };

        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(BogusWindowHandle, BogusIconId);
        NativeRect rectangle = default;

        shell.ShellNotifyIconGetRect(ref identifier, out rectangle);

        Assert.Equal(-1928, rectangle.left);
        Assert.Equal(-8, rectangle.top);
        Assert.Equal(-1920, rectangle.right);
        Assert.Equal(0, rectangle.bottom);
        Assert.False(rectangle.IsEmpty);
    }

    /// <summary>
    /// The seam signature is <c>(ref NOTIFYICONIDENTIFIER, out NativeRect) -&gt; int</c>: both
    /// structs go by reference and the status is a sign-carrying 32-bit <c>HRESULT</c>, so a caller
    /// can see which field produced which value and can read a negative failure code.
    /// </summary>
    /// <remarks>
    /// Pinned by reflection because the signature <em>is</em> the contract here: dropping the
    /// <c>ref</c> would silently copy the identifier (and would make the write-back test above
    /// impossible to express), and narrowing the status to <see langword="bool"/> or
    /// <see cref="uint"/> would misreport failures whose <c>HRESULT</c> has the severity bit set.
    /// </remarks>
    [Fact]
    public void Seam_signature_takes_both_structs_by_reference_and_returns_a_signed_status()
    {
        MethodInfo member = typeof(IShellApi).GetMethod(nameof(IShellApi.ShellNotifyIconGetRect))!;

        Assert.Equal(typeof(int), member.ReturnType);

        ParameterInfo[] parameters = member.GetParameters();
        Assert.Equal(2, parameters.Length);

        Assert.Equal(typeof(NOTIFYICONIDENTIFIER).MakeByRefType(), parameters[0].ParameterType);
        Assert.False(parameters[0].IsOut);
        Assert.True(parameters[0].ParameterType.IsByRef);

        // The rectangle is an out parameter: it is written by the callee, and a caller cannot be
        // asked to supply a meaningful value for it.
        Assert.Equal(typeof(NativeRect).MakeByRefType(), parameters[1].ParameterType);
        Assert.True(parameters[1].IsOut);
        Assert.True(parameters[1].ParameterType.IsByRef);
    }
}
