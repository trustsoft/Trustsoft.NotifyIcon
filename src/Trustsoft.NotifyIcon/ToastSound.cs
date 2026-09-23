namespace Trustsoft.NotifyIcon;

/// <summary>
/// Whether a toast is accompanied by the notification sound the shell would normally play for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two states, because the platform's useful distinction is silence.</b> The shell already
/// chooses a sound per profile and scenario, so the only decision the library can honestly expose
/// is whether to suppress it. A <c>bool</c> was considered and rejected: an enum can grow to named
/// system sounds additively without changing the meaning of a consumer's existing <c>true</c>,
/// while a boolean cannot.
/// </para>
/// <para>
/// <b><see cref="Default"/> writes nothing.</b> It emits no <c>&lt;audio&gt;</c> element, which
/// leaves the sound entirely to the shell; <see cref="Silent"/> emits
/// <c>&lt;audio silent="true"/&gt;</c>. The distinction matters for the payload contract: "default"
/// is the absence of a request, not a request for a specific sound.
/// </para>
/// <para>
/// <b>Silent suppresses the sound, not the toast.</b> A silent toast is still displayed; only its
/// audio is withheld. The shell decides what "the" notification sound is, and the user's own
/// accessibility and sound settings can override any of this.
/// </para>
/// </remarks>
public enum ToastSound
{
    /// <summary>
    /// The shell's own notification sound for this toast: no <c>&lt;audio&gt;</c> element is
    /// written. This is the default value of <see cref="ToastContent.Sound"/>.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The toast is shown without its notification sound
    /// (<c>&lt;audio silent="true"/&gt;</c>); the toast itself is still displayed.
    /// </summary>
    Silent = 1,
}
