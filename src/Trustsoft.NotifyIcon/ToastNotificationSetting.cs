namespace Trustsoft.NotifyIcon;

/// <summary>
/// The public name for the notification setting Windows reports for the identity a toast is shown
/// through: the outcome of the last <see cref="ToastNotifier.Show"/> rather than a failure.
/// </summary>
/// <remarks>
/// <para>
/// <b>An outcome, not a failure.</b> The platform reads this value from the notification state of
/// the AppUserModelID (the per-app <c>Enabled</c> value under
/// <c>HKCU\...\Notifications\Settings\&lt;aumid&gt;</c>, the per-user switches and any policy or
/// manifest that forbids notifications). A non-<see cref="Enabled"/> value is a routine Windows
/// state that the user or an administrator chose, so it is never raised on
/// <see cref="ToastNotifier.ToastError"/> and never turns a <see cref="ToastNotifier.Show"/> into a
/// failure: the show was accepted, the setting says what Windows will do with it.
/// </para>
/// <para>
/// <b>Disabled is what the shell reports asynchronously, not what <c>Show</c> returns.</b> Measured
/// with a per-app <c>Enabled=0</c> value present: the shell still returns <c>S_OK</c> from
/// <c>Show</c> and reports the delivery failure out of band through the notification's
/// <c>Failed</c> callback, carrying <c>WPN_E_NOTIFICATION_DISABLED</c> (<c>0x803E0111</c>). That
/// failure does reach <see cref="ToastNotifier.ToastError"/> as
/// <see cref="ToastException.OperationNotificationFailed"/> with the raw code, while this setting is
/// the value the notifier read before the show. A host that wants to explain itself should report
/// the setting and let the shell's callback be the failure.
/// </para>
/// <para>
/// <b>The largest scope wins.</b> When notifications are disabled at several levels the platform
/// reports one value, and it reflects the widest scope, in this order:
/// <see cref="DisabledByManifest"/> over <see cref="DisabledByGroupPolicy"/> over
/// <see cref="DisabledForUser"/> over <see cref="DisabledForApplication"/>. A value here is
/// therefore the effective state, not the most specific one.
/// </para>
/// <para>
/// <b>The ordinals are the platform's own.</b> Unlike <see cref="ToastSeverity"/>, whose members
/// are the library's own numbering, these numbers are the values
/// <c>IToastNotifier.GetSetting</c> hands back, and they are declared as such so a value read from
/// the platform needs no translation table of ours. A renumbering would be a wire-format change.
/// </para>
/// <para>
/// <b>It can lag a registry change by one process.</b> Measured: after writing the per-app
/// <c>Enabled=0</c> value, a process that had already read the setting kept reporting the old value
/// and a newly started process reported <see cref="DisabledForApplication"/>. The value is what the
/// platform reported at the moment it was read, which is why
/// <see cref="ToastNotifier.NotificationSetting"/> is documented as the last read rather than as a
/// live view of the registry.
/// </para>
/// </remarks>
public enum ToastNotificationSetting
{
    /// <summary>
    /// Notifications are enabled for the identity: the ordinary state, and the value a first use
    /// reports when the platform has no setting entry yet.
    /// </summary>
    Enabled = 0,

    /// <summary>
    /// Notifications are disabled for this application only (the per-app <c>Enabled=0</c> value):
    /// other applications still show theirs.
    /// </summary>
    /// <remarks>
    /// This is the state the measured notifications-disabled recipe produces, and the one whose
    /// delivery failure arrives later as <c>WPN_E_NOTIFICATION_DISABLED</c> on the
    /// <c>Failed</c> callback while <c>Show</c> still returned <c>S_OK</c>.
    /// </remarks>
    DisabledForApplication = 1,

    /// <summary>
    /// Notifications are disabled for the current user: every application that shows through this
    /// user's notification state is affected.
    /// </summary>
    DisabledForUser = 2,

    /// <summary>
    /// Notifications are disabled by group policy: an administrator turned them off for the machine
    /// or the user's organization.
    /// </summary>
    /// <remarks>
    /// Reported in preference to <see cref="DisabledForUser"/> and
    /// <see cref="DisabledForApplication"/> because policy is the widest scope of the three.
    /// </remarks>
    DisabledByGroupPolicy = 3,

    /// <summary>
    /// Notifications are disabled by the application's manifest: the declaration that asks not to be
    /// interrupted wins over every registry setting.
    /// </summary>
    /// <remarks>
    /// The widest scope of all, and therefore the value the platform reports when this and any other
    /// disable are present at the same time.
    /// </remarks>
    DisabledByManifest = 4,
}
