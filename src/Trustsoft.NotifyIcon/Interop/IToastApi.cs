namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// Receives the argument string Windows reports for a toast activation (a body click or a button
/// press), or <see langword="null"/> when the activation carried none.
/// </summary>
/// <param name="arguments">
/// The <c>launch</c>/button argument the activated toast was created with, exactly as the shell
/// delivered it.
/// </param>
/// <remarks>
/// Measured (M002/S01): the activation arrives on the thread that created the notification while
/// that thread pumps its message queue, and the argument string is the only payload an unpackaged
/// process receives - the toast's own <c>Arguments</c> value, not a title or tag.
/// </remarks>
internal delegate void ToastActivatedHandler(string? arguments);

/// <summary>Receives the reason Windows reports for a toast dismissal.</summary>
/// <param name="reason">
/// The raw <c>ToastDismissedReason</c> value (<c>0</c> = user canceled, <c>1</c> = application
/// hidden, <c>2</c> = timed out), passed as <see cref="int"/> so no WinRT type crosses this seam.
/// </param>
internal delegate void ToastDismissedHandler(int reason);

/// <summary>Receives the error code Windows reports when a toast cannot be delivered.</summary>
/// <param name="errorCode">The raw <c>ToastFailedError</c>-derived code.</param>
internal delegate void ToastFailedHandler(int errorCode);

/// <summary>
/// The single seam through which the library talks to the Windows toast stack:
/// <c>Windows.UI.Notifications</c> and <c>Windows.Data.Xml.Dom</c> for the toast itself, and the
/// per-user Start-menu shortcut (<c>IShellLink</c> + <c>IPropertyStore</c>) that carries the
/// AppUserModelID an unpackaged process needs before any toast is delivered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> As with <see cref="IShellApi"/>, it is required rather than convenient.
/// The WinRT activation-factory sequence and the shortcut's property write are the two contracts
/// the whole toast subsystem rests on, and neither can be exercised against a live shell in a unit
/// test: the machine may have notifications disabled, and a real click on a banner cannot be
/// produced from an automated host. The seam makes the sequence observable (a recording fake) and
/// every failure injectable, which is what the contract tests assert against: the shortcut's
/// <c>IPropertyStore.Commit</c> immediately before <c>IPersistFile.Save</c>, the activation-factory
/// acquisition order, the XML handed to <c>IXmlDocumentIO.LoadXml</c>, and the subscribe then
/// unsubscribe then release teardown that must leave no handler behind.
/// </para>
/// <para>
/// <b>Shaped by measurement, not by intent.</b> Every member below corresponds to a call the
/// M002/S01 probe made live on the target machine and recorded in
/// <c>docs/TOAST-MEASUREMENT.md</c>. The two ordering traps that document recorded are part of this
/// seam's contract and the tests assert them: the AppUserModelID must be committed <em>before</em>
/// the shortcut is saved (a property written after <c>Save</c> stays in memory and never reaches
/// the file), and the three event handlers must be subscribed before <c>Show</c> and removed before
/// the notification is released (a handler left subscribed is the leak the disposal contract
/// forbids).
/// </para>
/// <para>
/// <b>Raw results, no policy.</b> Every status-returning member returns exactly what the underlying
/// COM or WinRT call returned; nothing is reported as success unless Windows confirmed it. Two
/// failure channels are in use, exactly as they are in the measured sequence: an <c>HRESULT</c>
/// (<c>0</c> = <c>S_OK</c>, negative = failure) for every COM and WinRT call, and the Win32
/// last-error value read through <see cref="GetLastError"/> for the one file-system call
/// (<see cref="DeleteShortcut"/>). Interpreting a failure - naming the operation, raising the
/// library's <c>ToastException</c>, tracing - belongs to the caller.
/// </para>
/// <para>
/// <b>Failure is data: an operation plus a code.</b> A caller builds a failure report by pairing
/// the member it called with the code that member returned - for example the failing
/// <c>nameof(LoadXml)</c> together with a negative <c>HRESULT</c>, or <c>nameof(DeleteShortcut)</c>
/// together with the <see cref="GetLastError"/> value - which is the shape the library's toast
/// failure channel (D055) carries. The seam deliberately does not raise those failures itself, so a
/// test can script one and observe the caller's handling rather than the seam's.
/// </para>
/// <para>
/// <b>No WinRT type appears in this seam.</b> The runtime classes, the notifier, the notification
/// and the document are all opaque <see cref="IntPtr"/> handles here, and the event payloads are
/// primitives (<see cref="string"/>, <see cref="int"/>) carried by CLR delegates
/// (<see cref="ToastActivatedHandler"/>, <see cref="ToastDismissedHandler"/>,
/// <see cref="ToastFailedHandler"/>). The one implementation that declares the raw vtables
/// (<c>ToastApi</c>, T04) is therefore the only place the WinRT ABI appears, and it has exactly one
/// home to inspect for a wrong vtable slot or a wrong interface GUID.
/// </para>
/// <para>
/// <b>Handles are opaque and caller-owned.</b> Every <see cref="IntPtr"/> this seam hands out is
/// released through <see cref="ReleaseHandle"/> by the caller; measuring the release calls is how
/// the disposal contract is asserted without a live shell.
/// </para>
/// </remarks>
internal interface IToastApi
{
    // ---------------------------------------------------------------------------------------------
    // Contract 1 - the AppUserModelID identity (the per-user Start-menu shortcut).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Calls <c>CoCreateInstance(CLSID_ShellLink)</c> to obtain a new <c>IShellLinkW</c>.
    /// </summary>
    /// <param name="shellLink">Receives the shell-link handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>CoCreateInstance</c> (<c>0</c> = <c>S_OK</c>, negative = failure).</returns>
    /// <remarks>
    /// The first step of the identity write. <b>Consumed by T03</b> (<c>ToastIdentity</c>/
    /// <c>ShortcutLink</c>).
    /// </remarks>
    int CreateShellLink(out IntPtr shellLink);

    /// <summary>
    /// Configures the shortcut's target, description, icon location and arguments through the
    /// <c>IShellLinkW</c> setters.
    /// </summary>
    /// <param name="shellLink">The shell-link handle from <see cref="CreateShellLink"/>.</param>
    /// <param name="targetPath">The executable the shortcut points at.</param>
    /// <param name="description">The shortcut description.</param>
    /// <param name="iconPath">The icon location (may be the executable itself).</param>
    /// <param name="arguments">The command-line arguments the shortcut launches with.</param>
    /// <returns>
    /// The <c>HRESULT</c> of the first setter that failed, or <c>0</c> when all four succeeded.
    /// </returns>
    /// <remarks>
    /// The four <c>IShellLinkW</c> setters the measured sequence performed
    /// (<c>SetPath</c>/<c>SetDescription</c>/<c>SetIconLocation</c>/<c>SetArguments</c>) are one
    /// member because they are one configuration step: none of them is individually interesting to
    /// the identity contract, and folding them keeps the seam's granularity at the operations the
    /// caller reasons about. The fake still records the individual values. <b>Consumed by T03</b>.
    /// </remarks>
    int ConfigureShortcut(IntPtr shellLink, string targetPath, string description, string iconPath, string arguments);

    /// <summary>
    /// Queries <c>IPropertyStore</c> from the shell link (the interface
    /// <c>System.AppUserModelID</c> is written through).
    /// </summary>
    /// <param name="shellLink">The shell-link handle.</param>
    /// <param name="propertyStore">Receives the property-store handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of the <c>QueryInterface</c>.</returns>
    /// <remarks>
    /// <b>Consumed by T03</b> for both the write and the read-back paths.
    /// </remarks>
    int GetShortcutPropertyStore(IntPtr shellLink, out IntPtr propertyStore);

    /// <summary>
    /// Queries <c>IPersistFile</c> from the shell link (the interface the shortcut is serialized
    /// through).
    /// </summary>
    /// <param name="shellLink">The shell-link handle.</param>
    /// <param name="persistFile">Receives the persist-file handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of the <c>QueryInterface</c>.</returns>
    /// <remarks>
    /// <b>Consumed by T03</b> for the write path's <c>Save</c>.
    /// </remarks>
    int GetShortcutPersistFile(IntPtr shellLink, out IntPtr persistFile);

    /// <summary>
    /// Writes the <c>System.AppUserModelID</c> property (<c>PKEY_AppUserModel_ID</c>) into the
    /// property store as a <c>VT_LPWSTR</c>.
    /// </summary>
    /// <param name="propertyStore">The property-store handle from <see cref="GetShortcutPropertyStore"/>.</param>
    /// <param name="appUserModelId">The identity to write (the default or the caller's override).</param>
    /// <returns>The raw <c>HRESULT</c> of <c>IPropertyStore.SetValue</c>.</returns>
    /// <remarks>
    /// <b>Must be followed by <see cref="CommitPropertyStore"/> and only then by
    /// <see cref="SaveShortcut"/>.</b> Measured (M002/S01): saving before the commit serializes a
    /// property store with no <c>AppUserModelID</c> in it, and the shortcut then exists but carries
    /// no identity - the exact silent failure the read-back exists to catch. <b>Consumed by T03</b>.
    /// </remarks>
    int SetAppUserModelId(IntPtr propertyStore, string appUserModelId);

    /// <summary>
    /// Commits the pending property-store writes through <c>IPropertyStore.Commit</c>.
    /// </summary>
    /// <param name="propertyStore">The property-store handle.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>IPropertyStore.Commit</c>.</returns>
    /// <remarks>
    /// <b>Consumed by T03.</b> The commit is a distinct seam member (rather than being folded into
    /// <see cref="SetAppUserModelId"/>) precisely so a test can assert the measured ordering trap:
    /// the commit call is recorded between the set and the save.
    /// </remarks>
    int CommitPropertyStore(IntPtr propertyStore);

    /// <summary>
    /// Serializes the shortcut to disk through <c>IPersistFile.Save</c>.
    /// </summary>
    /// <param name="persistFile">The persist-file handle from <see cref="GetShortcutPersistFile"/>.</param>
    /// <param name="shortcutPath">The <c>.lnk</c> path to write.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>IPersistFile.Save</c>.</returns>
    /// <remarks>
    /// <b>Consumed by T03.</b> Must run only after <see cref="CommitPropertyStore"/> - see that
    /// member's remarks.
    /// </remarks>
    int SaveShortcut(IntPtr persistFile, string shortcutPath);

    /// <summary>
    /// Opens an existing shortcut from disk into a fresh shell link (the read-back path).
    /// </summary>
    /// <param name="shortcutPath">The <c>.lnk</c> path to open.</param>
    /// <param name="shellLink">Receives the shell-link handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of opening and loading the shortcut.</returns>
    /// <remarks>
    /// <b>The read-back must use a fresh shell link, not the one that was written through.</b>
    /// Reading the property off the writer's own in-memory object would report the value it holds
    /// even when <c>Save</c> never serialized it, which is why the seam exposes opening a shortcut
    /// as its own step. <b>Consumed by T03</b> (the read-back check that turns a silent
    /// non-registration into a named failure).
    /// </remarks>
    int OpenShellLink(string shortcutPath, out IntPtr shellLink);

    /// <summary>
    /// Reads the <c>System.AppUserModelID</c> property back through
    /// <c>IPropertyStore.GetValue</c>.
    /// </summary>
    /// <param name="propertyStore">The property-store handle of the freshly opened shell link.</param>
    /// <param name="appUserModelId">
    /// Receives the property value, or <see langword="null"/> when the property is absent or is not
    /// a <c>VT_LPWSTR</c>.
    /// </param>
    /// <returns>The raw <c>HRESULT</c> of <c>IPropertyStore.GetValue</c>.</returns>
    /// <remarks>
    /// A <c>S_OK</c> result with <see langword="null"/> <paramref name="appUserModelId"/> is the
    /// silent-failure case (the property is missing, its variant has type <c>VT_EMPTY</c>): the
    /// caller must treat "the value did not read back as written" as a failure, never as success.
    /// <b>Consumed by T03.</b>
    /// </remarks>
    int GetAppUserModelId(IntPtr propertyStore, out string? appUserModelId);

    /// <summary>
    /// Deletes the shortcut file the library created.
    /// </summary>
    /// <param name="shortcutPath">The <c>.lnk</c> path to delete.</param>
    /// <returns>
    /// <see langword="true"/> when the file was removed (or is already absent); <see langword="false"/>
    /// when the delete failed, in which case <see cref="GetLastError"/> carries the Win32 reason.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The one Win32 (file-system) call on this seam, and the one member whose failure channel is
    /// the last-error value rather than an <c>HRESULT</c>. <b>Consumed by T03</b> (unregister).
    /// </para>
    /// <para>
    /// Measured (M002/S01): deleting the shortcut <em>does not</em> immediately unregister the
    /// identity from the shell's notification state, so "removed" means the file is gone, not that
    /// the id stopped being routable - a distinction the negative control depends on.
    /// </para>
    /// </remarks>
    bool DeleteShortcut(string shortcutPath);

    /// <summary>
    /// Releases one COM/WinRT reference obtained from this seam.
    /// </summary>
    /// <param name="instance">The handle to release; <see cref="IntPtr.Zero"/> is ignored.</param>
    /// <returns>The reference count remaining after the release, as <c>Marshal.Release</c> returns it.</returns>
    /// <remarks>
    /// The disposal contract's primitive: the shell link, its two interfaces, the notifier, the
    /// notification and the XML document are all released through this one member, so a test can
    /// assert that nothing the seam handed out is left unreleased. <b>Consumed by T03 and T04.</b>
    /// </remarks>
    int ReleaseHandle(IntPtr instance);

    /// <summary>
    /// Returns the Win32 error code captured for the most recent failing file-system call.
    /// </summary>
    /// <returns>The raw error code, or <c>0</c> when nothing has failed.</returns>
    /// <remarks>
    /// The Win32 channel, mirroring <see cref="IShellApi.GetLastError"/>: the implementation must
    /// capture the value immediately after the failing call, inside the same member that made it,
    /// and this member only reads that stored value. Read it only after
    /// <see cref="DeleteShortcut"/> returned <see langword="false"/>.
    /// </remarks>
    int GetLastError();

    // ---------------------------------------------------------------------------------------------
    // Contract 2 - the WinRT toast stack (Windows.UI.Notifications / Windows.Data.Xml.Dom).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Acquires the <c>ToastNotificationManager</c> statics through
    /// <c>RoGetActivationFactory</c>.
    /// </summary>
    /// <param name="statics">Receives the factory handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>RoGetActivationFactory</c>.</returns>
    /// <remarks>
    /// The first measured acquisition. <b>Consumed by T04</b> (<c>ToastApi</c>).
    /// </remarks>
    int GetToastNotificationManagerStatics(out IntPtr statics);

    /// <summary>
    /// Acquires the <c>ToastNotification</c> factory through <c>RoGetActivationFactory</c>.
    /// </summary>
    /// <param name="factory">Receives the factory handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>RoGetActivationFactory</c>.</returns>
    /// <remarks><b>Consumed by T04.</b></remarks>
    int GetToastNotificationFactory(out IntPtr factory);

    /// <summary>
    /// Activates a <c>Windows.Data.Xml.Dom.XmlDocument</c> instance through
    /// <c>RoActivateInstance</c>.
    /// </summary>
    /// <param name="xmlDocument">Receives the document handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>RoActivateInstance</c>.</returns>
    /// <remarks><b>Consumed by T04.</b></remarks>
    int ActivateXmlDocument(out IntPtr xmlDocument);

    /// <summary>
    /// Creates the notifier for an explicit AppUserModelID through
    /// <c>IToastNotificationManagerStatics.CreateToastNotifierWithId</c>.
    /// </summary>
    /// <param name="statics">The manager statics from <see cref="GetToastNotificationManagerStatics"/>.</param>
    /// <param name="appUserModelId">The registered identity the notifier must be bound to.</param>
    /// <param name="notifier">Receives the notifier handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>CreateToastNotifierWithId</c>.</returns>
    /// <remarks>
    /// <b>The explicit-identity overload, never the argument-less one.</b> An unpackaged process has
    /// no process default AppUserModelID, so the measured sequence used
    /// <c>CreateToastNotifierWithId</c> with the registered id. <b>Consumed by T04.</b>
    /// </remarks>
    int CreateToastNotifier(IntPtr statics, string appUserModelId, out IntPtr notifier);

    /// <summary>
    /// Reads the notifier's <c>Setting</c> through <c>IToastNotifier.GetSetting</c>.
    /// </summary>
    /// <param name="notifier">The notifier handle.</param>
    /// <param name="setting">
    /// Receives the raw <c>NotificationSetting</c> value (<c>0</c> = enabled, <c>1</c> = disabled for
    /// the app, <c>2</c> = disabled for the user).
    /// </param>
    /// <returns>The raw <c>HRESULT</c> of <c>GetSetting</c>.</returns>
    /// <remarks>
    /// Measured (M002/S01): on first use for a freshly registered identity this call can return
    /// <c>E_NOT_FOUND</c> (<c>0x80070490</c>) and then succeed; that first-use code is benign and
    /// must not be treated as a failure. <b>Consumed by T04.</b>
    /// </remarks>
    int GetNotifierSetting(IntPtr notifier, out int setting);

    /// <summary>
    /// Loads the toast XML into the document through <c>IXmlDocumentIO.LoadXml</c>.
    /// </summary>
    /// <param name="xmlDocument">The document handle from <see cref="ActivateXmlDocument"/>.</param>
    /// <param name="xml">The XML payload exactly as it will be handed to the shell.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>LoadXml</c>.</returns>
    /// <remarks>
    /// Carrying the XML as a plain <see cref="string"/> is what lets a contract test assert the
    /// payload the shell was given (T04) without a WinRT type on the seam. <b>Consumed by T04.</b>
    /// </remarks>
    int LoadXml(IntPtr xmlDocument, string xml);

    /// <summary>
    /// Creates the notification from the loaded document through
    /// <c>IToastNotificationFactory.CreateToastNotification</c>.
    /// </summary>
    /// <param name="factory">The notification factory from <see cref="GetToastNotificationFactory"/>.</param>
    /// <param name="xmlDocument">The loaded document handle.</param>
    /// <param name="notification">Receives the notification handle; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>CreateToastNotification</c>.</returns>
    /// <remarks><b>Consumed by T04.</b></remarks>
    int CreateToastNotification(IntPtr factory, IntPtr xmlDocument, out IntPtr notification);

    /// <summary>
    /// Shows the notification through <c>IToastNotifier.Show</c>.
    /// </summary>
    /// <param name="notifier">The notifier bound to the registered identity.</param>
    /// <param name="notification">The notification to display.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>Show</c>.</returns>
    /// <remarks>
    /// A <c>S_OK</c> here means Windows accepted the toast, not that the user saw it, and - measured
    /// (M002/S01) - it is returned even for an identity that was never registered, where it
    /// nonetheless reaches the Action Center. Delivery is therefore judged out of process, and the
    /// negative control is judged on the absence of an activation callback rather than on this
    /// result. <b>Consumed by T04.</b>
    /// </remarks>
    int Show(IntPtr notifier, IntPtr notification);

    /// <summary>
    /// Subscribes an <c>Activated</c> handler through <c>IToastNotification.add_Activated</c>.
    /// </summary>
    /// <param name="notification">The notification handle.</param>
    /// <param name="handler">The callback that receives the activation's argument string.</param>
    /// <param name="token">Receives the registration token needed to unsubscribe.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>add_Activated</c>.</returns>
    /// <remarks>
    /// <b>Subscribed before <see cref="Show"/></b> in the measured sequence, so an activation that
    /// races the display is not lost. <b>Consumed by T04.</b>
    /// </remarks>
    int SubscribeActivated(IntPtr notification, ToastActivatedHandler handler, out long token);

    /// <summary>
    /// Subscribes a <c>Dismissed</c> handler through <c>IToastNotification.add_Dismissed</c>.
    /// </summary>
    /// <param name="notification">The notification handle.</param>
    /// <param name="handler">The callback that receives the dismissal reason.</param>
    /// <param name="token">Receives the registration token needed to unsubscribe.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>add_Dismissed</c>.</returns>
    /// <remarks><b>Consumed by T04.</b></remarks>
    int SubscribeDismissed(IntPtr notification, ToastDismissedHandler handler, out long token);

    /// <summary>
    /// Subscribes a <c>Failed</c> handler through <c>IToastNotification.add_Failed</c>.
    /// </summary>
    /// <param name="notification">The notification handle.</param>
    /// <param name="handler">The callback that receives the delivery error code.</param>
    /// <param name="token">Receives the registration token needed to unsubscribe.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>add_Failed</c>.</returns>
    /// <remarks><b>Consumed by T04.</b></remarks>
    int SubscribeFailed(IntPtr notification, ToastFailedHandler handler, out long token);

    /// <summary>
    /// Removes the <c>Activated</c> handler through <c>IToastNotification.remove_Activated</c>.
    /// </summary>
    /// <param name="notification">The notification handle.</param>
    /// <param name="token">The token returned by <see cref="SubscribeActivated"/>.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>remove_Activated</c>.</returns>
    /// <remarks>
    /// The three unsubscribes are separate members, mirroring the three measured
    /// <c>remove_*</c> calls, precisely so a test can assert that <em>all three</em> ran before the
    /// notification was released - a handler left subscribed is the leak the disposal contract
    /// forbids. <b>Consumed by T04.</b>
    /// </remarks>
    int UnsubscribeActivated(IntPtr notification, long token);

    /// <summary>
    /// Removes the <c>Dismissed</c> handler through <c>IToastNotification.remove_Dismissed</c>.
    /// </summary>
    /// <param name="notification">The notification handle.</param>
    /// <param name="token">The token returned by <see cref="SubscribeDismissed"/>.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>remove_Dismissed</c>.</returns>
    /// <remarks><b>Consumed by T04.</b></remarks>
    int UnsubscribeDismissed(IntPtr notification, long token);

    /// <summary>
    /// Removes the <c>Failed</c> handler through <c>IToastNotification.remove_Failed</c>.
    /// </summary>
    /// <param name="notification">The notification handle.</param>
    /// <param name="token">The token returned by <see cref="SubscribeFailed"/>.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>remove_Failed</c>.</returns>
    /// <remarks><b>Consumed by T04.</b></remarks>
    int UnsubscribeFailed(IntPtr notification, long token);
}
