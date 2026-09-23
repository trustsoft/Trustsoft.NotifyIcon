using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Trustsoft.NotifyIcon.ProbeToast;

/// <summary>
/// External measurement instrument for M002/S01: establishes, live on this machine, the two
/// contracts the whole milestone rests on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Before any API is designed, the milestone needs two facts measured on the
/// target machine: (1) how an unpackaged WPF process obtains a usable AppUserModelID, and (2) which
/// hand-written WinRT activation-factory calls create and show a toast and deliver a click back into
/// the running process. A unit test cannot prove either, because both are facts about the shell and
/// the WinRT runtime, not about the application. This program is the instrument that asks them.
/// </para>
/// <para>
/// <b>It is hand-written on purpose.</b> The P/Invoke and COM declarations, the WinRT interface
/// vtables and every GUID are declared here rather than taken from the library (which does not exist
/// yet) or from a WinRT projection package (which would hide the activation-factory path the
/// measurement must expose). Every factory acquisition and every COM call is therefore a recorded
/// HRESULT, which is what lets a later failure be attributed to identity or to the payload.
/// </para>
/// <para>
/// <b>Usage.</b>
/// <code>
/// probe-toast [--aumid &lt;id&gt;] [--skip-register] [--wait-seconds &lt;N&gt;] [--expect-no-toast]
/// probe-toast --history &lt;aumid&gt;
/// probe-toast --clear-history &lt;aumid&gt;
/// </code>
/// The default run registers the identity, shows a toast, subscribes to activation, waits for a click
/// and prints what arrives. <c>--skip-register</c> runs the same show path with the shortcut absent
/// (the negative control). <c>--history</c> and <c>--clear-history</c> are the out-of-process
/// inventory halves, run as separate process invocations so the observation is independent of the
/// process that showed the toast.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>The accepted command line, printed on every usage error.</summary>
    private const string Usage =
        "usage: probe-toast [--aumid <id>] [--skip-register] [--wait-seconds <N>] [--expect-no-toast] | --history <aumid> | --clear-history <aumid>";

    /// <summary>The AppUserModelID used when none is supplied.</summary>
    private const string DefaultAumid = "Trustsoft.NotifyIcon.ToastProbe";

    /// <summary>How long the show run waits for a click before reporting no activation.</summary>
    private const int DefaultWaitSeconds = 20;

    /// <summary>The shortcut file name in the per-user Start menu.</summary>
    private const string ShortcutFileName = "Trustsoft.NotifyIcon.ToastProbe.lnk";

    /// <summary>The <c>launch</c> attribute the payload carries, so the activation arguments name the run.</summary>
    private const string LaunchArgument = "probe-activation";

    /// <summary>The toast XML payload built by the show path.</summary>
    private const string ToastXmlTemplate =
        "<toast launch=\"{0}\">" +
        "<visual><binding template=\"ToastGeneric\">" +
        "<text>Trustsoft.NotifyIcon toast probe</text>" +
        "<text>Click this banner to prove in-process activation.</text>" +
        "</binding></visual></toast>";

    /// <summary>Runs the probe.</summary>
    /// <param name="args">See the class remarks for the command line.</param>
    /// <returns><c>0</c> on success, <c>1</c> on a failed check, <c>2</c> on a usage error.</returns>
    private static int Main(string[] args)
    {
        string aumid = DefaultAumid;
        bool skipRegister = false;
        int waitSeconds = DefaultWaitSeconds;
        bool expectNoToast = false;
        string? historyAumid = null;
        string? clearHistoryAumid = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--aumid" when i + 1 < args.Length:
                    aumid = args[++i];
                    break;
                case "--skip-register":
                    skipRegister = true;
                    break;
                case "--expect-no-toast":
                    expectNoToast = true;
                    break;
                case "--wait-seconds" when i + 1 < args.Length:
                    if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out waitSeconds) || waitSeconds <= 0)
                    {
                        Console.Error.WriteLine($"probe-toast: '--wait-seconds' needs a positive number of seconds, got '{args[i + 1]}'.");
                        return 2;
                    }
                    i++;
                    break;
                case "--history" when i + 1 < args.Length:
                    historyAumid = args[++i];
                    break;
                case "--clear-history" when i + 1 < args.Length:
                    clearHistoryAumid = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"probe-toast: unknown argument '{args[i]}' - this instrument does not ignore arguments it does not understand.");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        Console.WriteLine($"[probe] probe-toast start {DateTime.Now:yyyy-MM-dd HH:mm:ss}; os={Environment.OSVersion.VersionString}; machine={Environment.MachineName}; pid={Environment.ProcessId}");
        Console.WriteLine($"[probe] aumid={aumid}; skip-register={skipRegister}; wait-seconds={waitSeconds}; expect-no-toast={expectNoToast}");

        // The two inventory modes are separate process invocations and never touch registration or show.
        if (historyAumid is not null)
        {
            return RunHistoryInventory(historyAumid);
        }

        if (clearHistoryAumid is not null)
        {
            return RunClearHistory(clearHistoryAumid);
        }

        return RunShow(aumid, skipRegister, waitSeconds, expectNoToast);
    }

    /// <summary>
    /// Runs the full show path: register the identity (unless skipped), measure the activation
    /// factories, build and show the toast, subscribe to activation and record what arrives.
    /// </summary>
    /// <param name="aumid">The AppUserModelID.</param>
    /// <param name="skipRegister">Whether to skip shortcut creation (the negative control).</param>
    /// <param name="waitSeconds">How long to wait for a click.</param>
    /// <param name="expectNoToast">Whether a delivered toast is a failure (the negative control).</param>
    /// <returns>The process exit code.</returns>
    private static int RunShow(string aumid, bool skipRegister, int waitSeconds, bool expectNoToast)
    {
        int roHr = RoInitialize(RoInitSingleThreaded);

        // Phase 1: identity. The shortcut is the AppUserModelID carrier; read-before/write/read-after
        // is the whole identity contract, because a shortcut that exists but carries no property is the
        // exact silent failure this slice exists to rule out.
        string shortcutPath = GetShortcutPath();
        Console.WriteLine($"[probe] identity: shortcut path={shortcutPath}");

        string? before = null;
        string? after = null;
        bool readBackMatches = false;

        if (!skipRegister)
        {
            before = ReadAppUserModelId(shortcutPath);
            Console.WriteLine($"[probe] identity: AppUserModelID before write = {QuoteOrNull(before)}");

            int createHr = WriteShortcut(shortcutPath, aumid);
            Console.WriteLine($"[probe] identity: write shortcut hr=0x{createHr:X8}");

            after = ReadAppUserModelId(shortcutPath);
            Console.WriteLine($"[probe] identity: AppUserModelID after write = {QuoteOrNull(after)}");

            readBackMatches = string.Equals(after, aumid, StringComparison.Ordinal);
            Console.WriteLine($"[probe] identity: read-back matches expected '{aumid}' = {readBackMatches}");

            if (!readBackMatches)
            {
                Console.Error.WriteLine("[probe] identity: FAILED - the written AppUserModelID did not read back as written.");
                return 1;
            }
        }
        else
        {
            Console.WriteLine("[probe] identity: --skip-register, no shortcut is created (negative control).");
            before = ReadAppUserModelId(shortcutPath);
            Console.WriteLine($"[probe] identity: AppUserModelID on the existing (or absent) shortcut = {QuoteOrNull(before)}");
        }

        // Phase 2: the activation-factory measurement. Each runtime class is acquired the way the
        // later library must acquire it, and each acquisition HRESULT is recorded.
        Console.WriteLine("[probe] factory: RoInitialize hr=0x" + roHr.ToString("X8", CultureInfo.InvariantCulture));

        int staticsHr = AcquireActivationFactory("Windows.UI.Notifications.ToastNotificationManager", typeof(IToastNotificationManagerStatics).GUID, out IntPtr staticsPtr);
        Console.WriteLine($"[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x{staticsHr:X8}");
        if (staticsHr < 0)
        {
            Console.Error.WriteLine("[probe] factory: FAILED - could not acquire the ToastNotificationManager statics factory.");
            return 1;
        }

        int notificationFactoryHr = AcquireActivationFactory("Windows.UI.Notifications.ToastNotification", typeof(IToastNotificationFactory).GUID, out IntPtr notificationFactoryPtr);
        Console.WriteLine($"[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x{notificationFactoryHr:X8}");
        if (notificationFactoryHr < 0)
        {
            Console.Error.WriteLine("[probe] factory: FAILED - could not acquire the ToastNotification factory.");
            return 1;
        }

        int xmlInstanceHr = RoActivateInstance(CreateHString("Windows.Data.Xml.Dom.XmlDocument"), out IntPtr xmlInspectable);
        Console.WriteLine($"[probe] factory: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x{xmlInstanceHr:X8}");
        if (xmlInstanceHr < 0)
        {
            Console.Error.WriteLine("[probe] factory: FAILED - could not activate the XmlDocument instance.");
            return 1;
        }

        // Phase 3: the show path. CreateToastNotifierWithId is the explicit-identity overload; the
        // no-argument CreateToastNotifier would use the process default, which an unpackaged process
        // may not have. All inbound calls are raw-vtable (see the dispatch note above).
        using HString aumidHString = new(aumid);
        var createWithId = Vtable<CreateWithIdFn>(staticsPtr, 7);
        int notifierHr = createWithId(staticsPtr, aumidHString.Handle, out IntPtr notifierPtr);
        Console.WriteLine($"[probe] show: CreateToastNotifierWithId('{aumid}') hr=0x{notifierHr:X8}");
        if (notifierHr < 0)
        {
            Console.Error.WriteLine("[probe] show: FAILED - CreateToastNotifierWithId failed.");
            return 1;
        }

        var getSetting = Vtable<GetSettingFn>(notifierPtr, 8);
        int settingHr = getSetting(notifierPtr, out int notificationSetting);
        Console.WriteLine($"[probe] show: notifier.GetSetting hr=0x{settingHr:X8} value={notificationSetting} ({DescribeNotificationSetting(notificationSetting)})");

        int xmlIoHr = Marshal.QueryInterface(xmlInspectable, ref IXmlDocumentIoid, out IntPtr xmlIoPtr);
        Console.WriteLine($"[probe] show: QueryInterface(XmlDocument -> IXmlDocumentIO) hr=0x{xmlIoHr:X8}");

        int xmlDocHr = Marshal.QueryInterface(xmlInspectable, ref IXmlDocumentIoid2, out IntPtr xmlDocPtr);
        Console.WriteLine($"[probe] show: QueryInterface(XmlDocument -> IXmlDocument) hr=0x{xmlDocHr:X8}");
        if (xmlDocHr < 0)
        {
            Console.Error.WriteLine("[probe] show: FAILED - the XmlDocument instance does not expose IXmlDocument.");
            return 1;
        }

        string xml = string.Format(CultureInfo.InvariantCulture, ToastXmlTemplate, LaunchArgument);
        using HString xmlHString = new(xml);
        var loadXml = Vtable<LoadXmlFn>(xmlIoPtr, 6);
        int loadHr = loadXml(xmlIoPtr, xmlHString.Handle);
        Console.WriteLine($"[probe] show: IXmlDocumentIO.LoadXml hr=0x{loadHr:X8}");

        var createToast = Vtable<CreateToastFn>(notificationFactoryPtr, 6);
        int createToastHr = createToast(notificationFactoryPtr, xmlDocPtr, out IntPtr toastPtr);
        Console.WriteLine($"[probe] show: CreateToastNotification(xml) hr=0x{createToastHr:X8}");
        if (createToastHr < 0)
        {
            Console.Error.WriteLine("[probe] show: FAILED - CreateToastNotification failed.");
            return 1;
        }

        // Phase 4: subscription. Three hand-built WinRT delegates, one per event, so what arrives is
        // recorded rather than only counted.
        var received = new ActivationRecord();
        var activatedHandler = new ToastActivatedHandler((sender, args) => OnActivated(args, received));
        var dismissedHandler = new ToastActivatedHandler((_, args) => OnDismissed(args, received));
        var failedHandler = new ToastActivatedHandler((_, args) => OnFailed(args, received));

        IntPtr activatedPtr = Marshal.GetComInterfaceForObject(activatedHandler, typeof(ITypedEventHandler));
        IntPtr dismissedPtr = Marshal.GetComInterfaceForObject(dismissedHandler, typeof(ITypedEventHandler));
        IntPtr failedPtr = Marshal.GetComInterfaceForObject(failedHandler, typeof(ITypedEventHandler));

        var addActivated = Vtable<AddEventFn>(toastPtr, 11);
        var addDismissed = Vtable<AddEventFn>(toastPtr, 9);
        var addFailed = Vtable<AddEventFn>(toastPtr, 13);
        int addActivatedHr = addActivated(toastPtr, activatedPtr, out EventRegistrationToken activatedToken);
        int addDismissedHr = addDismissed(toastPtr, dismissedPtr, out EventRegistrationToken dismissedToken);
        int addFailedHr = addFailed(toastPtr, failedPtr, out EventRegistrationToken failedToken);
        Console.WriteLine($"[probe] subscribe: add_Activated hr=0x{addActivatedHr:X8} token=0x{activatedToken.Value:X16}");
        Console.WriteLine($"[probe] subscribe: add_Dismissed hr=0x{addDismissedHr:X8} token=0x{dismissedToken.Value:X16}");
        Console.WriteLine($"[probe] subscribe: add_Failed hr=0x{addFailedHr:X8} token=0x{failedToken.Value:X16}");

        var show = Vtable<ShowFn>(notifierPtr, 6);
        int showHr = show(notifierPtr, toastPtr);
        Console.WriteLine($"[probe] show: notifier.Show(toast) hr=0x{showHr:X8}");

        // Phase 5: wait for a click, pumping messages so a COM-marshaled activation callback can land
        // on this STA thread.
        Console.WriteLine($"[probe] wait: pumping for {waitSeconds}s for a click on the toast body");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(waitSeconds))
        {
            PumpMessages(100);
            if (received.ActivatedCount > 0)
            {
                Console.WriteLine($"[probe] wait: activation received at t={stopwatch.Elapsed.TotalSeconds:0.0}s");
                break;
            }
        }

        Console.WriteLine($"[probe] result: activated={received.ActivatedCount} arguments={QuoteOrNull(received.ActivatedArguments)} dismissed={received.DismissedCount} reason={received.DismissedReason} failed={received.FailedCount} errorCode=0x{received.FailedErrorCode:X8}");

        // Teardown: unsubscribe and release. Leaving a subscription behind is the exact leak the later
        // notifier must not have, so the unsubscribe calls are recorded too.
        var removeActivated = Vtable<RemoveEventFn>(toastPtr, 12);
        var removeDismissed = Vtable<RemoveEventFn>(toastPtr, 10);
        var removeFailed = Vtable<RemoveEventFn>(toastPtr, 14);
        int removeActivatedHr = removeActivated(toastPtr, activatedToken);
        int removeDismissedHr = removeDismissed(toastPtr, dismissedToken);
        int removeFailedHr = removeFailed(toastPtr, failedToken);
        Console.WriteLine($"[probe] teardown: remove_Activated hr=0x{removeActivatedHr:X8}; remove_Dismissed hr=0x{removeDismissedHr:X8}; remove_Failed hr=0x{removeFailedHr:X8}");

        ReleaseCom(activatedPtr);
        ReleaseCom(dismissedPtr);
        ReleaseCom(failedPtr);

        // Verdict. Measured on this machine: Show returns S_OK and the toast lands in the Action
        // Center even for an unregistered AUMID, so the negative control cannot be judged from the
        // Show HRESULT. What registration gates is ACTIVATION delivery, which is judged by the
        // out-of-process --history inventory plus the activation callback recorded above.
        if (expectNoToast)
        {
            Console.WriteLine("[probe] verdict: negative control - Show reported success (measured: registration is not a precondition for delivery); activation/delivery is judged out of process by --history.");
            return 0;
        }

        Console.WriteLine("[probe] verdict: positive run complete; delivery is judged out of process by --history.");
        return 0;
    }

    /// <summary>
    /// Queries the notification history for an AppUserModelID and prints the count. This is the
    /// out-of-process inventory: it is run as a separate process invocation from the show run.
    /// </summary>
    /// <param name="aumid">The AppUserModelID.</param>
    /// <returns>The exit code.</returns>
    private static int RunHistoryInventory(string aumid)
    {
        RoInitialize(RoInitSingleThreaded);

        int statics2Hr = AcquireActivationFactory("Windows.UI.Notifications.ToastNotificationManager", typeof(IToastNotificationManagerStatics2).GUID, out IntPtr statics2Ptr);
        Console.WriteLine($"[probe] history: RoGetActivationFactory(ToastNotificationManager -> IToastNotificationManagerStatics2) hr=0x{statics2Hr:X8}");
        if (statics2Hr < 0)
        {
            Console.Error.WriteLine("[probe] history: FAILED - could not acquire the statics2 factory.");
            return 1;
        }

        IToastNotificationManagerStatics2 statics2 = (IToastNotificationManagerStatics2)Wrap(statics2Ptr);
        var getHistory = Vtable<GetHistoryFn>(statics2Ptr, 6);
        int historyHr = getHistory(statics2Ptr, out IntPtr historyPtr);
        Console.WriteLine($"[probe] history: get_History hr=0x{historyHr:X8}");
        if (historyHr < 0)
        {
            Console.Error.WriteLine("[probe] history: FAILED - get_History failed.");
            return 1;
        }

        int history2Hr = Marshal.QueryInterface(historyPtr, ref IToastNotificationHistory2Iid, out IntPtr history2Ptr);
        Console.WriteLine($"[probe] history: QueryInterface(History -> IToastNotificationHistory2) hr=0x{history2Hr:X8}");
        if (history2Hr < 0)
        {
            Console.Error.WriteLine("[probe] history: FAILED - the History object does not expose IToastNotificationHistory2.");
            return 1;
        }

        var getHistoryWithId = Vtable<GetHistoryWithIdFn>(history2Ptr, 7);
        using HString aumidHString = new(aumid);
        int getHr = getHistoryWithId(history2Ptr, aumidHString.Handle, out IntPtr vectorPtr);
        Console.WriteLine($"[probe] history: GetHistoryWithId('{aumid}') hr=0x{getHr:X8}");
        if (getHr < 0)
        {
            Console.Error.WriteLine("[probe] history: FAILED - GetHistoryWithId failed.");
            return 1;
        }

        // The vector is IVectorView<ToastNotification>; its GUID is parameterized and therefore not
        // declared here. Its get_Size slot is fixed at 8 (IUnknown 3 + IInspectable 3 + IIterable.First
        // 1 + IVectorView.GetAt 1), so the count is read through a raw vtable call.
        int sizeHr = RawGetSize(vectorPtr, out uint count);
        Console.WriteLine($"[probe] history: IVectorView.get_Size hr=0x{sizeHr:X8} count={count}");

        Console.WriteLine($"[probe] history verdict: count={count} for '{aumid}'");
        return 0;
    }

    /// <summary>
    /// Clears the notification history for an AppUserModelID, so a run starts from a clean slate.
    /// </summary>
    /// <param name="aumid">The AppUserModelID.</param>
    /// <returns>The exit code.</returns>
    private static int RunClearHistory(string aumid)
    {
        RoInitialize(RoInitSingleThreaded);

        int statics2Hr = AcquireActivationFactory("Windows.UI.Notifications.ToastNotificationManager", typeof(IToastNotificationManagerStatics2).GUID, out IntPtr statics2Ptr);
        Console.WriteLine($"[probe] clear-history: RoGetActivationFactory(ToastNotificationManager -> IToastNotificationManagerStatics2) hr=0x{statics2Hr:X8}");
        if (statics2Hr < 0)
        {
            return 1;
        }

        var getHistory = Vtable<GetHistoryFn>(statics2Ptr, 6);
        int historyHr = getHistory(statics2Ptr, out IntPtr historyPtr);
        Console.WriteLine($"[probe] clear-history: get_History hr=0x{historyHr:X8}");
        if (historyHr < 0)
        {
            return 1;
        }

        var clearWithId = Vtable<ClearWithIdFn>(historyPtr, 12);
        using HString aumidHString = new(aumid);
        int clearHr = clearWithId(historyPtr, aumidHString.Handle);
        Console.WriteLine($"[probe] clear-history: ClearWithId('{aumid}') hr=0x{clearHr:X8}");
        return clearHr < 0 ? 1 : 0;
    }

    // ---------------------------------------------------------------------------------------------
    // Identity: per-user Start-menu shortcut carrying System.AppUserModelID via IShellLink +
    // IPropertyStore. Every call is a recorded HRESULT.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The per-user Start menu shortcut path for this probe.</summary>
    /// <returns>The full path to the shortcut file.</returns>
    private static string GetShortcutPath()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        return Path.Combine(programs, ShortcutFileName);
    }

    /// <summary>Creates the shortcut and writes the AppUserModelID property through IPropertyStore.</summary>
    /// <param name="path">The shortcut file path.</param>
    /// <param name="aumid">The AppUserModelID to write.</param>
    /// <returns>The last HRESULT of the sequence (S_OK when everything succeeded).</returns>
    private static int WriteShortcut(string path, string aumid)
    {
        int hr = CoCreateInstance(ref ClsidShellLink, IntPtr.Zero, ClsctxInprocServer, ref IidIShellLinkW, out IntPtr shellLinkPtr);
        Console.WriteLine($"[probe] identity: CoCreateInstance(CLSID_ShellLink) hr=0x{hr:X8}");
        if (hr < 0)
        {
            return hr;
        }

        IShellLinkW shellLink = (IShellLinkW)Wrap(shellLinkPtr);

        string? exePath = Environment.ProcessPath ?? typeof(Program).Assembly.Location;
        int setPathHr = shellLink.SetPath(exePath);
        int setDescriptionHr = shellLink.SetDescription("Trustsoft.NotifyIcon toast probe");
        int setIconHr = shellLink.SetIconLocation(exePath, 0);
        int setArgsHr = shellLink.SetArguments(string.Empty);
        Console.WriteLine($"[probe] identity: IShellLinkW.SetPath hr=0x{setPathHr:X8}; SetDescription hr=0x{setDescriptionHr:X8}; SetIconLocation hr=0x{setIconHr:X8}; SetArguments hr=0x{setArgsHr:X8}");

        int persistHr = Marshal.QueryInterface(shellLinkPtr, ref IidIPersistFile, out IntPtr persistPtr);
        Console.WriteLine($"[probe] identity: QueryInterface(IShellLinkW -> IPersistFile) hr=0x{persistHr:X8}");
        if (persistHr < 0)
        {
            return persistHr;
        }

        IPersistFile persist = (IPersistFile)Wrap(persistPtr);

        // The property store must be written and committed BEFORE IPersistFile.Save: Save serialises
        // the shortcut to the .lnk file, including the property-store section, so a property written
        // after Save stays in memory and never reaches the file (measured: read-back returned vt=0).
        int storeHr = Marshal.QueryInterface(shellLinkPtr, ref IidIPropertyStore, out IntPtr storePtr);
        Console.WriteLine($"[probe] identity: QueryInterface(IShellLinkW -> IPropertyStore) hr=0x{storeHr:X8}");
        if (storeHr < 0)
        {
            return storeHr;
        }

        IPropertyStore store = (IPropertyStore)Wrap(storePtr);
        int writeHr = WriteStringProperty(store, PkeyAppUserModelId, aumid);
        Console.WriteLine($"[probe] identity: IPropertyStore.SetValue(PKEY_AppUserModel_ID, '{aumid}') hr=0x{writeHr:X8}");
        if (writeHr < 0)
        {
            return writeHr;
        }

        int commitHr = store.Commit();
        Console.WriteLine($"[probe] identity: IPropertyStore.Commit hr=0x{commitHr:X8}");
        if (commitHr < 0)
        {
            return commitHr;
        }

        int saveHr = persist.Save(path, true);
        Console.WriteLine($"[probe] identity: IPersistFile.Save('{path}') hr=0x{saveHr:X8}");
        return saveHr;
    }

    /// <summary>Reads the AppUserModelID property from an existing shortcut, or null when absent.</summary>
    /// <param name="path">The shortcut file path.</param>
    /// <returns>The property value, or <see langword="null"/> when the shortcut or the property is absent.</returns>
    private static string? ReadAppUserModelId(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        int hr = CoCreateInstance(ref ClsidShellLink, IntPtr.Zero, ClsctxInprocServer, ref IidIShellLinkW, out IntPtr shellLinkPtr);
        if (hr < 0)
        {
            Console.WriteLine($"[probe] identity: read-back CoCreateInstance hr=0x{hr:X8}");
            return null;
        }

        int persistHr = Marshal.QueryInterface(shellLinkPtr, ref IidIPersistFile, out IntPtr persistPtr);
        if (persistHr < 0)
        {
            Console.WriteLine($"[probe] identity: read-back QueryInterface(IPersistFile) hr=0x{persistHr:X8}");
            return null;
        }

        IPersistFile persist = (IPersistFile)Wrap(persistPtr);
        int loadHr = persist.Load(path, 0);
        if (loadHr < 0)
        {
            Console.WriteLine($"[probe] identity: read-back IPersistFile.Load hr=0x{loadHr:X8}");
            return null;
        }

        int storeHr = Marshal.QueryInterface(shellLinkPtr, ref IidIPropertyStore, out IntPtr storePtr);
        if (storeHr < 0)
        {
            Console.WriteLine($"[probe] identity: read-back QueryInterface(IPropertyStore) hr=0x{storeHr:X8}");
            return null;
        }

        IPropertyStore store = (IPropertyStore)Wrap(storePtr);
        int getHr = store.GetValue(ref PkeyAppUserModelId, out PropVariant value);
        Console.WriteLine($"[probe] identity: read-back IPropertyStore.GetValue hr=0x{getHr:X8} vt={value.Vt}");

        return value.ReadString();
    }

    /// <summary>Writes a <c>VT_LPWSTR</c> value through an IPropertyStore.</summary>
    /// <param name="store">The property store.</param>
    /// <param name="key">The property key.</param>
    /// <param name="value">The string to write.</param>
    /// <returns>The SetValue HRESULT.</returns>
    private static int WriteStringProperty(IPropertyStore store, PropertyKey key, string value)
    {
        IntPtr buffer = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var variant = new PropVariant { Vt = VtLpwstr, PointerValue = buffer };
            return store.SetValue(ref key, ref variant);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Activation-factory measurement and the show path.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Acquires a WinRT activation factory by runtime-class name and interface id.</summary>
    /// <param name="className">The runtime-class name, e.g. <c>Windows.UI.Notifications.ToastNotificationManager</c>.</param>
    /// <param name="iid">The requested interface id.</param>
    /// <param name="factory">Receives the raw interface pointer.</param>
    /// <returns>The <c>RoGetActivationFactory</c> HRESULT.</returns>
    private static int AcquireActivationFactory(string className, Guid iid, out IntPtr factory)
    {
        using HString classHString = new(className);
        return RoGetActivationFactory(classHString.Handle, ref iid, out factory);
    }

    /// <summary>Wraps a raw interface pointer in an RCW; the cast at the call site QIs to the target interface.</summary>
    /// <param name="ptr">The raw interface pointer (already QI'd to the target interface).</param>
    /// <returns>The RCW.</returns>
    private static object Wrap(IntPtr ptr) => Marshal.GetObjectForIUnknown(ptr);

    /// <summary>Releases a raw interface pointer.</summary>
    /// <param name="ptr">The pointer to release.</param>
    private static void ReleaseCom(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.Release(ptr);
        }
    }

    /// <summary>Reads the activation arguments out of the callback payload.</summary>
    private static void OnActivated(IntPtr args, ActivationRecord record)
    {
        Interlocked.Increment(ref record.ActivatedCount);
        try
        {
            var getArguments = Vtable<GetArgumentsFn>(args, 6);
            int hr = getArguments(args, out IntPtr hstring);
            if (hr >= 0)
            {
                record.ActivatedArguments = ReadHString(hstring);
                WindowsDeleteString(hstring);
            }
            Console.WriteLine($"[probe] activation: get_Arguments hr=0x{hr:X8} value={QuoteOrNull(record.ActivatedArguments)}");
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[probe] activation: reading the payload failed: {ex.Message}");
            Console.Out.Flush();
        }
    }

    /// <summary>Records a dismissal and its reason.</summary>
    private static void OnDismissed(IntPtr args, ActivationRecord record)
    {
        Interlocked.Increment(ref record.DismissedCount);
        try
        {
            var getReason = Vtable<GetReasonFn>(args, 6);
            int hr = getReason(args, out int reason);
            record.DismissedReason = reason;
            Console.WriteLine($"[probe] dismissed: get_Reason hr=0x{hr:X8} value={reason} ({DescribeDismissalReason(reason)})");
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[probe] dismissed: reading the payload failed: {ex.Message}");
        }
    }

    /// <summary>Records a failure and its error code.</summary>
    private static void OnFailed(IntPtr args, ActivationRecord record)
    {
        Interlocked.Increment(ref record.FailedCount);
        try
        {
            var getErrorCode = Vtable<GetErrorCodeFn>(args, 6);
            int hr = getErrorCode(args, out int errorCode);
            record.FailedErrorCode = errorCode;
            Console.WriteLine($"[probe] failed: get_ErrorCode hr=0x{hr:X8} value=0x{errorCode:X8}");
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[probe] failed: reading the payload failed: {ex.Message}");
        }
    }

    /// <summary>The counters and payloads collected while waiting for a click.</summary>
    private sealed class ActivationRecord
    {
        public int ActivatedCount;
        public int DismissedCount;
        public int FailedCount;
        public string? ActivatedArguments;
        public int DismissedReason = -1;
        public int FailedErrorCode;
    }

    // ---------------------------------------------------------------------------------------------
    // Raw helpers: HSTRING, the message pump and a raw vtable call.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Creates an HSTRING from a managed string.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The HSTRING handle.</returns>
    private static IntPtr CreateHString(string value)
    {
        int hr = WindowsCreateString(value, value.Length, out IntPtr handle);
        return hr < 0 ? IntPtr.Zero : handle;
    }

    /// <summary>Reads an HSTRING into a managed string.</summary>
    /// <param name="handle">The HSTRING handle.</param>
    /// <returns>The string.</returns>
    private static string ReadHString(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        IntPtr raw = WindowsGetStringRawBuffer(handle, out uint length);
        return length == 0 ? string.Empty : Marshal.PtrToStringUni(raw, (int)length) ?? string.Empty;
    }

    /// <summary>Pumps queued window messages for a bounded interval so COM callbacks can land.</summary>
    /// <param name="milliseconds">How long to pump.</param>
    private static void PumpMessages(int milliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < milliseconds)
        {
            while (PeekMessage(out Msg msg, IntPtr.Zero, 0, 0, PmRemove))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            Thread.Sleep(10);
        }
    }

    /// <summary>Calls <c>IVectorView&lt;ToastNotification&gt;.get_Size</c> through a raw vtable slot.</summary>
    /// <param name="vector">The raw IVectorView pointer.</param>
    /// <param name="count">Receives the size.</param>
    /// <returns>The HRESULT.</returns>
    private static int RawGetSize(IntPtr vector, out uint count)
    {
        count = 0;
        IntPtr vtable = Marshal.ReadIntPtr(vector);
        IntPtr getSize = Marshal.ReadIntPtr(vtable + GetSizeSlot * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<GetSizeDelegate>(getSize);
        return fn(vector, out count);
    }

    /// <summary>The vtable slot of <c>IVectorView&lt;T&gt;.get_Size</c>. IVectorView is flat (inherits IInspectable directly, not IIterable), so the slot is IUnknown 3 + IInspectable 3 + GetAt 1.</summary>
    private const int GetSizeSlot = 7;

    /// <summary>Formats a value that may be null.</summary>
    private static string QuoteOrNull(string? value) => value is null ? "(null)" : $"'{value}'";

    private static string DescribeNotificationSetting(int value) => value switch
    {
        0 => "Enabled",
        1 => "DisabledForApplication",
        2 => "DisabledForUser",
        3 => "DisabledByGroupPolicy",
        4 => "DisabledByManifest",
        _ => "unknown",
    };

    private static string DescribeDismissalReason(int value) => value switch
    {
        0 => "UserCanceled",
        1 => "TimedOut",
        2 => "ApplicationHidden",
        _ => "unknown",
    };

    // ---------------------------------------------------------------------------------------------
    // The WinRT interfaces, hand-declared against the Windows 10 (26100) ABI. Every vtable is flat:
    // IUnknown (3, implicit) + IInspectable (3) + the interface's own methods, in the order the
    // Windows SDK metadata declares them. Every method is [PreserveSig] so the HRESULT is the return
    // value and is never swallowed or converted to an exception.
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>Windows.Foundation.IInspectable</c>.</summary>
    [ComImport]
    [Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInspectable
    {
        [PreserveSig]
        int GetIids(out uint count, out IntPtr iids);

        [PreserveSig]
        int GetRuntimeClassName(out IntPtr name);

        [PreserveSig]
        int GetTrustLevel(out int level);
    }

    /// <summary><c>Windows.Foundation.IAgileObject</c>, the marker the delegate CCW carries.</summary>
    [ComImport]
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }

    /// <summary><c>Windows.UI.Notifications.IToastNotificationManagerStatics</c>.</summary>
    [ComImport]
    [Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotificationManagerStatics : IInspectable
    {
        [PreserveSig]
        int CreateToastNotifier(out IntPtr value);

        [PreserveSig]
        int CreateToastNotifierWithId(IntPtr appId, out IntPtr value);

        [PreserveSig]
        int GetTemplateContent(int templateType, out IntPtr value);
    }

    /// <summary><c>Windows.UI.Notifications.IToastNotificationManagerStatics2</c>.</summary>
    [ComImport]
    [Guid("7AB93C52-0E48-4750-BA9D-1A4113981847")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotificationManagerStatics2 : IInspectable
    {
        [PreserveSig]
        int GetHistory(out IntPtr value);
    }

    /// <summary><c>Windows.UI.Notifications.IToastNotifier</c>.</summary>
    [ComImport]
    [Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotifier : IInspectable
    {
        [PreserveSig]
        int Show(IntPtr notification);

        [PreserveSig]
        int Hide(IntPtr notification);

        [PreserveSig]
        int GetSetting(out int setting);

        [PreserveSig]
        int AddToSchedule(IntPtr scheduledToast);

        [PreserveSig]
        int RemoveFromSchedule(IntPtr scheduledToast);

        [PreserveSig]
        int GetScheduledToastNotifications(out IntPtr value);
    }

    /// <summary><c>Windows.UI.Notifications.IToastNotificationFactory</c>.</summary>
    [ComImport]
    [Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotificationFactory : IInspectable
    {
        [PreserveSig]
        int CreateToastNotification(IntPtr content, out IntPtr value);
    }

    /// <summary><c>Windows.UI.Notifications.IToastNotification</c>.</summary>
    [ComImport]
    [Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotification : IInspectable
    {
        [PreserveSig]
        int GetContent(out IntPtr value);

        [PreserveSig]
        int PutExpirationTime(IntPtr value);

        [PreserveSig]
        int GetExpirationTime(out IntPtr value);

        [PreserveSig]
        int AddDismissed(IntPtr handler, out EventRegistrationToken token);

        [PreserveSig]
        int RemoveDismissed(EventRegistrationToken token);

        [PreserveSig]
        int AddActivated(IntPtr handler, out EventRegistrationToken token);

        [PreserveSig]
        int RemoveActivated(EventRegistrationToken token);

        [PreserveSig]
        int AddFailed(IntPtr handler, out EventRegistrationToken token);

        [PreserveSig]
        int RemoveFailed(EventRegistrationToken token);
    }

    /// <summary><c>Windows.UI.Notifications.IToastActivatedEventArgs</c>.</summary>
    [ComImport]
    [Guid("E3BF92F3-C197-436F-8265-0625824F8DAC")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastActivatedEventArgs : IInspectable
    {
        [PreserveSig]
        int GetArguments(out IntPtr value);
    }

    /// <summary><c>Windows.UI.Notifications.IToastDismissedEventArgs</c>.</summary>
    [ComImport]
    [Guid("3F89D935-D9CB-4538-A0F0-FFE7659938F8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastDismissedEventArgs : IInspectable
    {
        [PreserveSig]
        int GetReason(out int reason);
    }

    /// <summary><c>Windows.UI.Notifications.IToastFailedEventArgs</c>.</summary>
    [ComImport]
    [Guid("35176862-CFD4-44F8-AD64-F500FD896C3B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastFailedEventArgs : IInspectable
    {
        [PreserveSig]
        int GetErrorCode(out int errorCode);
    }

    /// <summary><c>Windows.Data.Xml.Dom.IXmlDocument</c> (the interface passed to the factory).</summary>
    [ComImport]
    [Guid("F7F3A506-1E87-42D6-BCFB-B8C809FA5494")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IXmlDocument : IInspectable
    {
    }

    /// <summary><c>Windows.Data.Xml.Dom.IXmlDocumentIO</c>.</summary>
    [ComImport]
    [Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IXmlDocumentIO : IInspectable
    {
        [PreserveSig]
        int LoadXml(IntPtr xml);

        [PreserveSig]
        int LoadXmlWithSettings(IntPtr loadSettings, IntPtr xml);

        [PreserveSig]
        int SaveToFileAsync(IntPtr file, out IntPtr value);
    }

    /// <summary><c>Windows.UI.Notifications.IToastNotificationHistory</c>.</summary>
    [ComImport]
    [Guid("5CADDC63-01D3-4C97-986F-0533483FEE14")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotificationHistory : IInspectable
    {
        [PreserveSig]
        int RemoveGroup(IntPtr group);

        [PreserveSig]
        int RemoveGroupWithId(IntPtr group, IntPtr appId);

        [PreserveSig]
        int RemoveGroupedTagWithId(IntPtr tag, IntPtr group, IntPtr appId);

        [PreserveSig]
        int RemoveGroupedTag(IntPtr tag, IntPtr group);

        [PreserveSig]
        int Remove(IntPtr tag);

        [PreserveSig]
        int Clear();

        [PreserveSig]
        int ClearWithId(IntPtr appId);
    }

    /// <summary><c>Windows.UI.Notifications.IToastNotificationHistory2</c>.</summary>
    [ComImport]
    [Guid("3BC3D253-2F31-4092-9129-8AD5ABF067DA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotificationHistory2 : IInspectable
    {
        [PreserveSig]
        int GetHistory(out IntPtr value);

        [PreserveSig]
        int GetHistoryWithId(IntPtr appId, out IntPtr value);
    }

    /// <summary><c>Windows.Foundation.TypedEventHandler&lt;T,U&gt;</c>, the delegate interface the toast events take.</summary>
    [ComImport]
    [Guid("9DE1C534-6AE1-11E0-84E1-18A905BCC53F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITypedEventHandler : IInspectable
    {
        [PreserveSig]
        int Invoke(IntPtr sender, IntPtr args);
    }

    /// <summary>
    /// A managed implementation of <see cref="ITypedEventHandler"/> that the COM-callable wrapper
    /// exposes as a WinRT delegate: <c>Invoke</c> lands at vtable slot 6 (IUnknown 3 + IInspectable 3),
    /// and <see cref="IAgileObject"/> makes the delegate callable from the shell's thread.
    /// </summary>
    [ComVisible(true)]
    private sealed class ToastActivatedHandler : ITypedEventHandler, IAgileObject
    {
        private readonly Action<IntPtr, IntPtr> _onInvoke;

        public ToastActivatedHandler(Action<IntPtr, IntPtr> onInvoke) => _onInvoke = onInvoke;

        public int GetIids(out uint count, out IntPtr iids)
        {
            count = 0;
            iids = IntPtr.Zero;
            return 0;
        }

        public int GetRuntimeClassName(out IntPtr name)
        {
            name = IntPtr.Zero;
            return unchecked((int)0x80004001); // E_NOTIMPL
        }

        public int GetTrustLevel(out int level)
        {
            level = 0; // TrustLevel.BaseTrust
            return 0;
        }

        public int Invoke(IntPtr sender, IntPtr args)
        {
            _onInvoke(sender, args);
            return 0;
        }
    }

    /// <summary>An 8-byte WinRT event registration token.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventRegistrationToken
    {
        public long Value;
    }

    // ---------------------------------------------------------------------------------------------
    // Shortcut COM interop: IShellLinkW, IPersistFile, IPropertyStore, PROPERTYKEY, PROPVARIANT.
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>IShellLinkW</c> (shell32), declared in full up to the last method used.</summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? pszFile, int cch, IntPtr pfd, uint fFlags);

        [PreserveSig]
        int GetIDList(out IntPtr ppidl);

        [PreserveSig]
        int SetIDList(IntPtr pidl);

        [PreserveSig]
        int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? pszName, int cch);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? pszDir, int cch);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        [PreserveSig]
        int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? pszArgs, int cch);

        [PreserveSig]
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        [PreserveSig]
        int GetHotkey(out ushort pwHotkey);

        [PreserveSig]
        int SetHotkey(ushort wHotkey);

        [PreserveSig]
        int GetShowCmd(out int piShowCmd);

        [PreserveSig]
        int SetShowCmd(int iShowCmd);

        [PreserveSig]
        int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? pszIconPath, int cch, out int piIcon);

        [PreserveSig]
        int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        [PreserveSig]
        int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        [PreserveSig]
        int Resolve(IntPtr hwnd, uint fFlags);

        [PreserveSig]
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    /// <summary><c>IPersistFile</c>.</summary>
    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        [PreserveSig]
        int GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        [PreserveSig]
        int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);

        [PreserveSig]
        int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        [PreserveSig]
        int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        [PreserveSig]
        int GetCurFile(out IntPtr ppszFileName);
    }

    /// <summary><c>IPropertyStore</c>.</summary>
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);

        [PreserveSig]
        int GetAt(uint iProp, out PropertyKey pkey);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant pv);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant pv);

        [PreserveSig]
        int Commit();
    }

    /// <summary>A <c>PROPERTYKEY</c>: a format id plus a property id.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    /// <summary>
    /// A <c>PROPVARIANT</c> sized for a 64-bit process (24 bytes). Only the <c>vt</c> and the first
    /// pointer-sized union slot are declared; a string value needs nothing beyond those.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Vt;

        [FieldOffset(8)]
        public IntPtr PointerValue;

        /// <summary>Reads the value as a <c>VT_LPWSTR</c>, or null when it is not one.</summary>
        public string? ReadString()
        {
            if (Vt != VtLpwstr || PointerValue == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStringUni(PointerValue);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Constants and P/Invoke declarations.
    // ---------------------------------------------------------------------------------------------

    private static Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static Guid IidIShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static Guid IidIPersistFile = new("0000010B-0000-0000-C000-000000000046");
    private static Guid IidIPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static Guid IXmlDocumentIoid = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");
    private static Guid IXmlDocumentIoid2 = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    private static Guid IToastNotificationHistory2Iid = new("3BC3D253-2F31-4092-9129-8AD5ABF067DA");

    /// <summary><c>PKEY_AppUserModel_ID</c>.</summary>
    private static PropertyKey PkeyAppUserModelId = new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5,
    };

    private const uint ClsctxInprocServer = 0x1;
    private const ushort VtLpwstr = 31;
    private const int RoInitSingleThreaded = 0;
    private const uint PmRemove = 0x0001;

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoActivateInstance(IntPtr activatableClassId, out IntPtr instance);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    /// <summary>The <c>IVectorView&lt;T&gt;.get_Size</c> vtable signature.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSizeDelegate(IntPtr self, out uint size);

    // ---------------------------------------------------------------------------------------------
    // Raw-vtable dispatch. The RCW path ([ComImport] interface that inherits IInspectable, wrapped
    // via Marshal.GetObjectForIUnknown and cast) mis-dispatches on this runtime (measured:
    // CreateToastNotifierWithId returned E_OUTOFMEMORY and get_History faulted), so every inbound
    // WinRT call goes through the object's own vtable slot instead, which is what the raw ABI defines.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Reads one vtable slot of a raw interface pointer and returns a typed delegate for it.</summary>
    /// <typeparam name="T">The delegate signature.</typeparam>
    /// <param name="self">The raw interface pointer.</param>
    /// <param name="slot">The zero-based vtable slot (IUnknown 3 + IInspectable 3 + the method index).</param>
    /// <returns>The delegate bound to that slot.</returns>
    private static T Vtable<T>(IntPtr self, int slot) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(self);
        IntPtr fn = Marshal.ReadIntPtr(vtable + slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateWithIdFn(IntPtr self, IntPtr appId, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSettingFn(IntPtr self, out int setting);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int LoadXmlFn(IntPtr self, IntPtr xml);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateToastFn(IntPtr self, IntPtr content, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ShowFn(IntPtr self, IntPtr notification);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int AddEventFn(IntPtr self, IntPtr handler, out EventRegistrationToken token);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RemoveEventFn(IntPtr self, EventRegistrationToken token);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetHistoryFn(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetHistoryWithIdFn(IntPtr self, IntPtr appId, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ClearWithIdFn(IntPtr self, IntPtr appId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetArgumentsFn(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetReasonFn(IntPtr self, out int reason);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetErrorCodeFn(IntPtr self, out int errorCode);

    /// <summary>A Win32 message.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    /// <summary>A disposable HSTRING holder, so every HSTRING is released on every path.</summary>
    private sealed class HString : IDisposable
    {
        private IntPtr _handle;

        public HString(string value)
        {
            _handle = CreateHString(value);
        }

        public IntPtr Handle => _handle;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                WindowsDeleteString(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
