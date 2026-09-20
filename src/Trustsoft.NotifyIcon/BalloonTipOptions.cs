using System;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// Optional behaviours a balloon notification is shown with, combined as a flags value.
/// </summary>
/// <remarks>
/// <para>
/// <b>The members are shell bits, but not all of them are bits of the same field.</b> This is the
/// one mapping fact that is easy to get wrong and impossible to see from the call site:
/// <see cref="NoSound"/> and <see cref="RespectQuietTime"/> are <c>NIIF_*</c> bits and land in the
/// structure's <c>dwInfoFlags</c> member alongside the severity, while <see cref="Realtime"/> is an
/// <c>NIF_*</c> bit and lands in <c>uFlags</c> as <c>NIF_REALTIME</c>. The two fields legitimately
/// share bit numbers - <c>NIIF_NOSOUND</c> and <c>NIF_INFO</c> are both <c>0x10</c> - so a constant
/// that is correct in one field is silently meaningless in the other: writing
/// <see cref="Realtime"/>'s value into <c>dwInfoFlags</c> would ask the shell for a custom user icon
/// instead of a realtime balloon, and nothing would report an error.
/// </para>
/// <para>
/// <b><see cref="RespectQuietTime"/> is opt-in, and the tradeoff is the point (R016).</b> The shell
/// recommends setting it for an application that means to honour quiet time, but a balloon that
/// carries it during quiet time is <em>dismissed unshown</em> rather than queued. Honouring quiet
/// time therefore means deliberately allowing some balloons to be swallowed silently. The library
/// leaves that decision to the caller instead of making it for them: it neither sets the flag
/// behind the caller's back nor withholds every balloon the user asked to see.
/// </para>
/// <para>
/// <b>There is deliberately no timeout member.</b> The structure's <c>uTimeout</c> reading of the
/// <c>uTimeoutOrVersion</c> union is deprecated since Windows Vista, and the display duration is
/// the system accessibility setting. A timeout option here would be a knob that either does nothing
/// or fights the user's own accessibility configuration.
/// </para>
/// </remarks>
[Flags]
public enum BalloonTipOptions
{
    /// <summary>
    /// No optional behaviour: the balloon is shown with sound, is not realtime and does not claim to
    /// honour quiet time.
    /// </summary>
    None = 0,

    /// <summary>
    /// The balloon is shown immediately rather than queued, ignoring quiet time.
    /// <c>NIF_REALTIME</c>, written to the structure's <c>uFlags</c> member.
    /// </summary>
    /// <remarks>
    /// <b>Discarded rather than delayed.</b> If the shell cannot show the balloon immediately, a
    /// realtime balloon is dropped; it is not shown later. That makes realtime the choice for a
    /// notification whose value expires - "the device just disconnected" - and the wrong choice for
    /// one that must eventually be seen. Because the bit belongs to <c>uFlags</c> and never to
    /// <c>dwInfoFlags</c>, a realtime balloon that also sets <see cref="NoSound"/> carries the same
    /// bit number in both fields, each in its own field - which is exactly why the placement rule
    /// matters.
    /// </remarks>
    Realtime = 0x40,

    /// <summary>
    /// The balloon is shown silently, without the notification sound. <c>NIIF_NOSOUND</c>, written
    /// to the structure's <c>dwInfoFlags</c> member.
    /// </summary>
    /// <remarks>
    /// A plain bit rather than part of the severity ordinal, so it is combined with a
    /// <see cref="BalloonTipIcon"/> value rather than replacing one.
    /// </remarks>
    NoSound = 0x10,

    /// <summary>
    /// The balloon is suppressed while Windows quiet time is in effect.
    /// <c>NIIF_RESPECT_QUIET_TIME</c>, written to the structure's <c>dwInfoFlags</c> member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Opt-in, and it suppresses rather than defers.</b> Quiet time is the first hour after a new
    /// account is created or Windows is upgraded. A balloon carrying this flag during that window is
    /// dismissed unshown; outside it the flag has no effect. Setting it is a deliberate promise to
    /// the user's quiet-time setting, at the cost of losing whatever the balloon said.
    /// </para>
    /// <para>
    /// R016 requires this choice to exist rather than to be made silently on the caller's behalf:
    /// the alternatives - always setting the flag, or never setting it - each decide for the caller
    /// what "respect the user's quiet time" means for their application. Only meaningful together
    /// with the balloon operation this library performs.
    /// </para>
    /// </remarks>
    RespectQuietTime = 0x80,
}
