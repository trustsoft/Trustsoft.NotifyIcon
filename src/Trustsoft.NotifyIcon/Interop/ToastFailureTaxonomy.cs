namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The names of the notification platform's delivery-failure codes: the vocabulary a bare
/// <c>HRESULT</c> on the toast failure path is reported in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal, and deliberately not a public type.</b> The taxonomy is a diagnostic vocabulary, not
/// a contract: it names codes the platform returns, and it is reachable through the Verbose trace
/// line the notifier writes beside the unchanged Error-level line. Giving it a public type would add
/// a member to the exported surface for a table a consumer can read in
/// <c>docs/TOAST-MEASUREMENT.md</c>, and the codes themselves remain on
/// <see cref="ToastErrorEventArgs.ErrorCode"/> where they always were.
/// </para>
/// <para>
/// <b>The codes are quoted from the local SDK header.</b>
/// <c>C:/Program Files (x86)/Windows Kits/10/Include/10.0.26100.0/shared/winerror.h</c>, the
/// notification-platform block (lines 56680-57020), declares <c>WPN_E_*</c> as
/// <c>_HRESULT_TYPEDEF_(0x803E....)</c> values. Each constant below names its header macro so the two
/// can be compared without opening the header; the values are the header's, not this library's.
/// </para>
/// <para>
/// <b>Total, and honest about what it does not know.</b> <see cref="NameOf(int)"/> maps every code it
/// recognises to a stable name and anything else to <see cref="Unknown"/>: the list is
/// deliberately the codes a toast show can plausibly report rather than the whole
/// <c>WPN_E_*</c> block, and an unmapped code is reported as unknown rather than guessed at - the
/// same posture <see cref="ToastDismissalReason.Unknown"/> takes for dismissal reasons.
/// </para>
/// <para>
/// <b>What is deliberately absent.</b> Quiet hours / Do Not Disturb is not part of this vocabulary.
/// The only public API in that area, <c>SHQueryUserNotificationState</c>, reports the legacy
/// logon-quiet-time state rather than Windows 11 Focus Assist, and a suppressed toast is not reported
/// as a delivery failure (measured) - so the taxonomy neither names nor claims a quiet-hours code, and
/// this library does not scrape the registry or query that API to invent one.
/// </para>
/// <para>
/// <b>Value-level failures are not in the table.</b> <see cref="ToastShow.ErrorInvalidArgument"/>
/// (<c>E_INVALIDARG</c>), <see cref="ToastShow.ErrorAlreadyShown"/> (<c>E_UNEXPECTED</c>) and the
/// benign first-use <see cref="ToastShow.ErrorSettingNotFound"/> (<c>E_NOT_FOUND</c>) are produced by
/// the show path itself rather than by the notification platform, so they map to
/// <see cref="Unknown"/>: the table describes the platform's delivery failures, and the internal
/// operation name already says what those three are.
/// </para>
/// </remarks>
internal static class ToastFailureTaxonomy
{
    /// <summary>The name reported for a code the taxonomy does not recognise.</summary>
    internal const string Unknown = "Unknown";

    /// <summary><c>WPN_E_INVALID_APP</c> (<c>0x803E0102</c>): the identity the toast was sent with is not a known application.</summary>
    internal const int InvalidApp = unchecked((int)0x803E0102);

    /// <summary><c>WPN_E_PLATFORM_UNAVAILABLE</c> (<c>0x803E0105</c>): the notification platform is not available in this session.</summary>
    internal const int PlatformUnavailable = unchecked((int)0x803E0105);

    /// <summary>
    /// <c>WPN_E_NOTIFICATION_DISABLED</c> (<c>0x803E0111</c>): notifications are disabled for the
    /// identity - the code measured on the <c>Failed</c> callback for a per-app <c>Enabled=0</c>
    /// value while <c>Show</c> had returned <c>S_OK</c>.
    /// </summary>
    internal const int NotificationsDisabled = unchecked((int)0x803E0111);

    /// <summary><c>WPN_E_NOTIFICATION_INCAPABLE</c> (<c>0x803E0112</c>): this device cannot show the notification.</summary>
    internal const int DeviceIncapable = unchecked((int)0x803E0112);

    /// <summary><c>WPN_E_NOTIFICATION_TYPE_DISABLED</c> (<c>0x803E0114</c>): the notification type is disabled.</summary>
    internal const int TypeDisabled = unchecked((int)0x803E0114);

    /// <summary><c>WPN_E_NOTIFICATION_SIZE</c> (<c>0x803E0115</c>): the payload is too large for the platform.</summary>
    internal const int PayloadTooLarge = unchecked((int)0x803E0115);

    /// <summary><c>WPN_E_TAG_SIZE</c> (<c>0x803E0116</c>): the notification's tag exceeds the platform's limit.</summary>
    internal const int TagTooLong = unchecked((int)0x803E0116);

    /// <summary><c>WPN_E_POWER_SAVE</c> (<c>0x803E0201</c>): the notification was suppressed by power-saving policy.</summary>
    internal const int PowerSave = unchecked((int)0x803E0201);

    /// <summary>
    /// <c>WPN_E_IMAGE_NOT_FOUND_IN_CACHE</c> (<c>0x803E0202</c>): the platform could not resolve the
    /// image the payload named.
    /// </summary>
    /// <remarks>
    /// The name is the cloud-cache one; measured local behaviour is that the shell accepts a payload
    /// whose <c>src</c> names a nonexistent file and does not fire the <c>Failed</c> callback at all,
    /// which is why the library checks the file before it shows (S04/T01).
    /// </remarks>
    internal const int ImageMissing = unchecked((int)0x803E0202);

    /// <summary><c>WPN_E_TOAST_NOTIFICATION_DROPPED</c> (<c>0x803E0207</c>): the toast was dropped without being displayed.</summary>
    internal const int Dropped = unchecked((int)0x803E0207);

    /// <summary><c>WPN_E_GROUP_SIZE</c> (<c>0x803E0209</c>): the notification's group exceeds the platform's limit.</summary>
    internal const int GroupTooLong = unchecked((int)0x803E0209);

    /// <summary><c>WPN_E_GROUP_ALPHANUMERIC</c> (<c>0x803E020A</c>): the notification's group is not alphanumeric.</summary>
    internal const int GroupNotAlphanumeric = unchecked((int)0x803E020A);

    /// <summary>
    /// Names one delivery-failure code in the platform's own vocabulary.
    /// </summary>
    /// <param name="code">The <c>HRESULT</c> a step or the shell reported.</param>
    /// <returns>
    /// The stable name of the code, or <see cref="Unknown"/> for any code this table does not carry -
    /// including <c>0</c>, the value-level codes of the show path, and every <c>WPN_E_*</c> code the
    /// toast show cannot produce.
    /// </returns>
    /// <remarks>
    /// The mapping is deliberately partial and total at once: every recognised code has exactly one
    /// name, and everything else is <see cref="Unknown"/> rather than a guess. A name is a reading of
    /// the header, not new information about the failure - the raw code stays the authoritative
    /// value on <see cref="ToastErrorEventArgs.ErrorCode"/>.
    /// </remarks>
    internal static string NameOf(int code) => code switch
    {
        InvalidApp => nameof(InvalidApp),
        PlatformUnavailable => nameof(PlatformUnavailable),
        NotificationsDisabled => nameof(NotificationsDisabled),
        DeviceIncapable => nameof(DeviceIncapable),
        TypeDisabled => nameof(TypeDisabled),
        PayloadTooLarge => nameof(PayloadTooLarge),
        TagTooLong => nameof(TagTooLong),
        PowerSave => nameof(PowerSave),
        ImageMissing => nameof(ImageMissing),
        Dropped => nameof(Dropped),
        GroupTooLong => nameof(GroupTooLong),
        GroupNotAlphanumeric => nameof(GroupNotAlphanumeric),
        _ => Unknown,
    };
}
