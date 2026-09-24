namespace Trustsoft.NotifyIcon;

/// <summary>
/// How emphatically a toast is shown: the public name for the native
/// <c>Windows.UI.Notifications.ToastScenario</c> that reaches the shell in the toast document's
/// <c>scenario</c> attribute.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a cosmetic icon.</b> The WinRT toast platform has no <c>Info</c>/<c>Warning</c>/
/// <c>Error</c> severity concept and no severity icon to set; what it has is a scenario, a single
/// value that changes how the toast persists, which sound it plays and when it is displayed. D053
/// maps the content model's severity onto that native lever rather than inventing a second,
/// visual-only severity that the shell would not honour, so a value here is a request the platform
/// acts on rather than an attribute the library keeps to itself.
/// </para>
/// <para>
/// <b>The visible effect is the shell's to give, and it can withhold it.</b> A reminder toast is
/// <em>silently ignored unless the same toast carries an action that activates in the background</em>,
/// and an alarm toast loops the alarm audio. Both are shell rules stated by the toast schema, and
/// neither is something a library can force. Setting a non-<see cref="Default"/> value therefore
/// never guarantees a visible difference: it asks the platform for a scenario, and the platform
/// decides. This is why the payload contract covers the attribute and its acceptance by
/// <c>IXmlDocumentIO.LoadXml</c>, not a promise that the toast looks or behaves differently.
/// </para>
/// <para>
/// <b>The values are the library's own ordinals, not the platform's.</b> Unlike
/// <see cref="BalloonTipIcon"/>, whose members are written into the shell's <c>dwInfoFlags</c> as a
/// cast, a <see cref="ToastSeverity"/> is rendered as a lower-case name (<c>reminder</c>,
/// <c>alarm</c>, <c>urgent</c>) by the payload builder. There is therefore no numeric wire
/// identity to preserve and the members are numbered from zero in declaration order. The native
/// vocabulary is wider than this one: <c>ToastScenario</c> also carries <c>IncomingCall</c>, which
/// D053 deliberately leaves out of the v1 model.
/// </para>
/// <para>
/// <b>A missing scenario is the default one.</b> <see cref="Default"/> emits no <c>scenario</c>
/// attribute at all rather than the literal <c>default</c>: the schema has no such value, and an
/// absent attribute is how a toast says "default".
/// </para>
/// </remarks>
public enum ToastSeverity
{
    /// <summary>
    /// The ordinary toast: no <c>scenario</c> attribute is written, so the shell applies its own
    /// default persistence, sound and display behaviour. This is the default value of
    /// <see cref="ToastContent.Severity"/>.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The reminder scenario (<c>scenario="reminder"</c>): the shell treats the toast as a
    /// persistent reminder rather than a transient message.
    /// </summary>
    /// <remarks>
    /// The shelf effect depends on a background-activation action, which v1 does not emit (D054
    /// scopes activation to the running application), so the shell will typically ignore the
    /// scenario. The contract here is the attribute the platform receives, not a visible result.
    /// </remarks>
    Reminder = 1,

    /// <summary>
    /// The alarm scenario (<c>scenario="alarm"</c>): the shell shows an alarm toast and loops the
    /// alarm audio until the user dismisses it.
    /// </summary>
    /// <remarks>
    /// The looping audio is the shell's behaviour for this scenario, not something the library
    /// starts or stops; it is the reason an alarm toast is the wrong choice for a notification the
    /// user did not explicitly ask to be interrupted for.
    /// </remarks>
    Alarm = 2,

    /// <summary>
    /// The urgent scenario (<c>scenario="urgent"</c>): the shell gives the toast elevated
    /// persistence and display priority.
    /// </summary>
    Urgent = 3,
}
