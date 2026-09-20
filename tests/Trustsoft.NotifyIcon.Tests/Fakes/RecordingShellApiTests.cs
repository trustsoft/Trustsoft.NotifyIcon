using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves <see cref="RecordingShellApi"/> actually records and actually delegates.
/// </summary>
/// <remarks>
/// <para>
/// A wrapper that forgot to forward its calls would still satisfy every fake-based test in the
/// suite, so the delegation path is worth one cheap live check here. The deeper probe - all four
/// real-signature assertions, including <c>GetGuiResources</c>, <c>CreateIconIndirect</c> and a
/// deliberately invalid <c>Shell_NotifyIconW</c> call - belongs to T09
/// (<c>RealShellApiSignatureProbeTests</c>), which is written against the delivered seam.
/// </para>
/// <para>
/// The calls made here are side-effect-free: registering a message name allocates nothing in the
/// shell and no icon is ever added, so this test cannot put anything in a developer's
/// notification area.
/// </para>
/// </remarks>
public sealed class RecordingShellApiTests
{
    /// <summary>
    /// A real <c>RegisterWindowMessageW</c> call through the recording wrapper returns a
    /// non-zero id and leaves exactly one record - which is simultaneously proof that the entry
    /// point name in <c>user32.dll</c> is right and that the delegation is not a no-op.
    /// </summary>
    [Fact]
    public void RegisterWindowMessage_is_recorded_and_delegated_to_the_real_api()
    {
        var recording = new RecordingShellApi();

        // A unique name per run: registering a message name is harmless, but a stable name would
        // make the returned id indistinguishable from a hard-coded value.
        string message = $"Trustsoft.NotifyIcon.Tests.SignatureProbe.{Guid.NewGuid():N}";

        uint id = recording.RegisterWindowMessage(message);

        Assert.NotEqual(0u, id);

        ShellCall call = Assert.Single(recording.Calls);
        Assert.Equal(nameof(Interop.IShellApi.RegisterWindowMessage), call.Operation);
        Assert.Equal($"message=\"{message}\"", call.Detail);
    }
}
