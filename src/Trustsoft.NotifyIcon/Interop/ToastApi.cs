using System.IO;
using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The real <see cref="IToastApi"/> implementation for Contract 2 - the WinRT
/// <c>Windows.UI.Notifications</c> toast stack - and the only place in the library where the WinRT
/// ABI (its interface ids, its vtable slot numbers and its HSTRING/delegate conventions) appears.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hand-written, and deliberately so.</b> Just as <see cref="ShortcutLink"/> does for
/// <c>IShellLinkW</c>, this class declares its own <c>[DllImport]</c> entry points and its own
/// <c>[ComImport]</c> interfaces instead of taking a WinRT projection package: the shipped assembly
/// adds no dependency beyond the BCL and WPF (R011 / D049). Every acquisition and every call is a
/// raw <c>HRESULT</c> the caller can attribute to one operation.
/// </para>
/// <para>
/// <b>Raw-vtable dispatch, never the RCW.</b> The M002/S01 measurement recorded that declaring a
/// <c>[ComImport]</c> interface which inherits <c>IInspectable</c> and wrapping it with
/// <c>Marshal.GetObjectForIUnknown</c> mis-dispatches on this runtime
/// (<c>CreateToastNotifierWithId</c> returned <c>E_OUTOFMEMORY</c>; <c>get_History</c> faulted with
/// an access violation). Every inbound call therefore reads the interface's own vtable slot
/// (<c>IUnknown</c> 0-2, <c>IInspectable</c> 3-5, then the interface's methods in metadata order)
/// and invokes it through a function pointer, which is what the ABI actually defines and which
/// touches no reference count.
/// </para>
/// <para>
/// <b>Versioned WinRT interfaces are flat.</b> <c>IToastNotification2</c>,
/// <c>IToastNotifier2</c> and the rest inherit <c>IInspectable</c> directly rather than the
/// earlier version of themselves, so their vtables contain only their own methods. The slot
/// numbers below are taken from the 10.0.26100 headers/winmds the measurement used as its
/// authority, and each is documented with the interface it belongs to.
/// </para>
/// <para>
/// <b>Both contracts, one production object.</b> Contract 1 (the AppUserModelID shortcut) is
/// implemented once, by <see cref="ShortcutLink"/>, and delegated to here; Contract 2 is
/// implemented by this class. A caller that needs the whole seam - which is what S03's notifier
/// will need - therefore takes one <see cref="ToastApi"/> instance rather than juggling two, and
/// there is still exactly one home to inspect for a wrong vtable slot
/// (<see cref="ShortcutLink"/> for the shell interfaces, this class for the WinRT ones).
/// </para>
/// <para>
/// <b>Threading.</b> The COM apartment is initialized on demand (<c>RoInitialize</c> with
/// <c>RO_INIT_SINGLETHREADED</c>), and its result is deliberately not gated on: on a thread that is
/// already in a multi-threaded apartment the call returns <c>RPC_E_CHANGED_MODE</c> and
/// <c>RoGetActivationFactory</c> still succeeds (measured). The only mutable state is the
/// per-instance table of live event subscriptions, guarded by a lock.
/// </para>
/// </remarks>
internal sealed class ToastApi : IToastApi
{
    // ---------------------------------------------------------------------------------------------
    // Runtime classes, interface ids and vtable slots, copied from the 10.0.26100 metadata
    // (the measurement's authoritative source; docs/TOAST-MEASUREMENT.md).
    // ---------------------------------------------------------------------------------------------

    private const string ToastNotificationManagerClassName = "Windows.UI.Notifications.ToastNotificationManager";
    private const string ToastNotificationClassName = "Windows.UI.Notifications.ToastNotification";
    private const string XmlDocumentClassName = "Windows.Data.Xml.Dom.XmlDocument";
    private const string PropertyValueClassName = "Windows.Foundation.PropertyValue";

    // Not readonly: the raw COM interop passes them by ref (CS0199 for a static readonly field).
    private static Guid IidToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
    private static Guid IidToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    private static Guid IidXmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    private static Guid IidXmlDocumentIO = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");

    /// <summary><c>IToastNotification2</c>: carries the tag and group (not toast XML).</summary>
    private static Guid IidToastNotification2 = new("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7");

    /// <summary><c>IPropertyValueStatics</c>: the <c>Windows.Foundation.PropertyValue</c> statics that box the expiry.</summary>
    private static Guid IidPropertyValueStatics = new("629BDBC8-D932-4FF4-96B9-8D96C5C1E858");

    /// <summary><c>IToastNotificationManagerStatics.CreateToastNotifierWithId</c>.</summary>
    private const int StaticsCreateToastNotifierWithIdSlot = 7;

    /// <summary><c>IToastNotifier.Show</c>.</summary>
    private const int NotifierShowSlot = 6;

    /// <summary><c>IToastNotifier.GetSetting</c>.</summary>
    private const int NotifierGetSettingSlot = 8;

    /// <summary><c>IToastNotificationFactory.CreateToastNotification</c>.</summary>
    private const int NotificationFactoryCreateToastNotificationSlot = 6;

    /// <summary>
    /// <c>IToastNotification2.put_Tag</c>: <c>put_Tag</c> 6, <c>get_Tag</c> 7, <c>put_Group</c> 8,
    /// <c>get_Group</c> 9 (windows.ui.notifications.idl, uuid 9DFB9FD1-143A-490E-90BF-B9FBA7132DE7).
    /// </summary>
    private const int ToastNotification2PutTagSlot = 6;

    /// <summary><c>IToastNotification2.put_Group</c>.</summary>
    private const int ToastNotification2PutGroupSlot = 8;

    /// <summary>
    /// <c>IToastNotification.put_ExpirationTime</c>: <c>get_Content</c> 6, <c>put_ExpirationTime</c> 7,
    /// <c>get_ExpirationTime</c> 8, then the event pairs at 9-14
    /// (windows.ui.notifications.idl, uuid 997E2675-059E-4E60-8B06-1760917C8B80).
    /// </summary>
    private const int ToastPutExpirationTimeSlot = 7;

    /// <summary>
    /// <c>IPropertyValueStatics.CreateDateTime</c>: the statics' methods start at slot 6
    /// (<c>CreateEmpty</c>), and <c>CreateDateTime</c> is the sixteenth of them
    /// (windows.foundation.idl, uuid 629BDBC8-D932-4FF4-96B9-8D96C5C1E858).
    /// </summary>
    private const int PropertyValueStaticsCreateDateTimeSlot = 21;

    /// <summary><c>IXmlDocumentIO.LoadXml</c>.</summary>
    private const int XmlDocumentIoLoadXmlSlot = 6;

    /// <summary><c>IToastNotification.add_Dismissed</c> / <c>remove_Dismissed</c>.</summary>
    private const int ToastAddDismissedSlot = 9;

    /// <summary><c>IToastNotification.remove_Dismissed</c>.</summary>
    private const int ToastRemoveDismissedSlot = 10;

    /// <summary><c>IToastNotification.add_Activated</c>.</summary>
    private const int ToastAddActivatedSlot = 11;

    /// <summary><c>IToastNotification.remove_Activated</c>.</summary>
    private const int ToastRemoveActivatedSlot = 12;

    /// <summary><c>IToastNotification.add_Failed</c>.</summary>
    private const int ToastAddFailedSlot = 13;

    /// <summary><c>IToastNotification.remove_Failed</c>.</summary>
    private const int ToastRemoveFailedSlot = 14;

    /// <summary><c>IToastDismissedEventArgs.get_Reason</c> and <c>IToastActivatedEventArgs.get_Arguments</c>-adjacent payload getters.</summary>
    private const int EventArgsPayloadSlot = 6;

    /// <summary><c>RO_INIT_SINGLETHREADED</c>: the apartment the measurement used.</summary>
    private const int RoInitSingleThreaded = 0;

    /// <summary><c>E_NOTIMPL</c>, the documented answer for a runtime class name on a WinRT delegate.</summary>
    private const int ENotImpl = unchecked((int)0x80004001);

    /// <summary>The Contract-1 half of the seam: the shortcut identity, implemented once, elsewhere.</summary>
    private readonly ShortcutLink _shortcuts = new();

    /// <summary>
    /// The live event subscriptions, so the managed handlers and the COM-callable wrapper pointers
    /// stay alive (and release exactly once) between <c>add_*</c> and <c>remove_*</c>.
    /// </summary>
    private readonly Dictionary<long, (object Owner, IntPtr Pointer)> _subscriptions = [];

    private readonly object _subscriptionsGate = new();

    // ---------------------------------------------------------------------------------------------
    // Contract 1 - delegated to ShortcutLink, which owns the shell interop.
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public int CreateShellLink(out IntPtr shellLink) => _shortcuts.CreateShellLink(out shellLink);

    /// <inheritdoc />
    public int ConfigureShortcut(IntPtr shellLink, string targetPath, string description, string iconPath, string arguments) =>
        _shortcuts.ConfigureShortcut(shellLink, targetPath, description, iconPath, arguments);

    /// <inheritdoc />
    public int GetShortcutPropertyStore(IntPtr shellLink, out IntPtr propertyStore) =>
        _shortcuts.GetShortcutPropertyStore(shellLink, out propertyStore);

    /// <inheritdoc />
    public int GetShortcutPersistFile(IntPtr shellLink, out IntPtr persistFile) =>
        _shortcuts.GetShortcutPersistFile(shellLink, out persistFile);

    /// <inheritdoc />
    public int SetAppUserModelId(IntPtr propertyStore, string appUserModelId) =>
        _shortcuts.SetAppUserModelId(propertyStore, appUserModelId);

    /// <inheritdoc />
    public int CommitPropertyStore(IntPtr propertyStore) => _shortcuts.CommitPropertyStore(propertyStore);

    /// <inheritdoc />
    public int SaveShortcut(IntPtr persistFile, string shortcutPath) => _shortcuts.SaveShortcut(persistFile, shortcutPath);

    /// <inheritdoc />
    public int OpenShellLink(string shortcutPath, out IntPtr shellLink) => _shortcuts.OpenShellLink(shortcutPath, out shellLink);

    /// <inheritdoc />
    public int GetAppUserModelId(IntPtr propertyStore, out string? appUserModelId) =>
        _shortcuts.GetAppUserModelId(propertyStore, out appUserModelId);

    /// <inheritdoc />
    public bool DeleteShortcut(string shortcutPath) => _shortcuts.DeleteShortcut(shortcutPath);

    /// <inheritdoc />
    public int GetLastError() => _shortcuts.GetLastError();

    /// <inheritdoc />
    public int ReleaseHandle(IntPtr instance) => _shortcuts.ReleaseHandle(instance);

    // ---------------------------------------------------------------------------------------------
    // Contract 2 - the WinRT toast stack, this class's own interop.
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public int GetToastNotificationManagerStatics(out IntPtr statics)
    {
        InitializeRuntime();
        int hr = AcquireActivationFactory(ToastNotificationManagerClassName, ref IidToastNotificationManagerStatics, out statics);
        NotifyIconTrace.Verbose($"toast: RoGetActivationFactory({ToastNotificationManagerClassName} -> IToastNotificationManagerStatics) hr=0x{hr:X8}");
        return hr;
    }

    /// <inheritdoc />
    public int GetToastNotificationFactory(out IntPtr factory)
    {
        InitializeRuntime();
        int hr = AcquireActivationFactory(ToastNotificationClassName, ref IidToastNotificationFactory, out factory);
        NotifyIconTrace.Verbose($"toast: RoGetActivationFactory({ToastNotificationClassName} -> IToastNotificationFactory) hr=0x{hr:X8}");
        return hr;
    }

    /// <inheritdoc />
    public int ActivateXmlDocument(out IntPtr xmlDocument)
    {
        InitializeRuntime();
        xmlDocument = IntPtr.Zero;

        using HString classId = new(XmlDocumentClassName);
        if (classId.HResult < 0)
        {
            return classId.HResult;
        }

        int hr = RoActivateInstance(classId.Handle, out xmlDocument);
        NotifyIconTrace.Verbose($"toast: RoActivateInstance({XmlDocumentClassName}) hr=0x{hr:X8}");
        return hr;
    }

    /// <inheritdoc />
    public int CreateToastNotifier(IntPtr statics, string appUserModelId, out IntPtr notifier)
    {
        notifier = IntPtr.Zero;

        using HString appId = new(appUserModelId);
        if (appId.HResult < 0)
        {
            return appId.HResult;
        }

        int hr = Vtable<CreateToastNotifierWithIdFn>(statics, StaticsCreateToastNotifierWithIdSlot)(statics, appId.Handle, out notifier);
        NotifyIconTrace.Verbose($"toast: CreateToastNotifierWithId('{appUserModelId}') hr=0x{hr:X8}");
        return hr;
    }

    /// <inheritdoc />
    public int GetNotifierSetting(IntPtr notifier, out int setting) =>
        Vtable<GetSettingFn>(notifier, NotifierGetSettingSlot)(notifier, out setting);

    /// <inheritdoc />
    public int LoadXml(IntPtr xmlDocument, string xml)
    {
        // The document instance is activated as Windows.Data.Xml.Dom.XmlDocument; the LoadXml
        // method lives on IXmlDocumentIO, which the instance exposes by QueryInterface.
        int hr = QueryInterface(xmlDocument, ref IidXmlDocumentIO, out IntPtr xmlDocumentIo);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            using HString payload = new(xml);
            if (payload.HResult < 0)
            {
                return payload.HResult;
            }

            return Vtable<LoadXmlFn>(xmlDocumentIo, XmlDocumentIoLoadXmlSlot)(xmlDocumentIo, payload.Handle);
        }
        finally
        {
            ReleaseHandle(xmlDocumentIo);
        }
    }

    /// <inheritdoc />
    public int CreateToastNotification(IntPtr factory, IntPtr xmlDocument, out IntPtr notification)
    {
        notification = IntPtr.Zero;

        // The factory takes an IXmlDocument, not the activated instance's default interface.
        int hr = QueryInterface(xmlDocument, ref IidXmlDocument, out IntPtr content);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            return Vtable<CreateToastNotificationFn>(factory, NotificationFactoryCreateToastNotificationSlot)(factory, content, out notification);
        }
        finally
        {
            ReleaseHandle(content);
        }
    }

    /// <inheritdoc />
    public int SetNotificationTag(IntPtr notification, string tag)
    {
        // put_Tag lives on IToastNotification2, which the notification exposes by QueryInterface.
        int hr = QueryInterface(notification, ref IidToastNotification2, out IntPtr notification2);
        if (hr < 0)
        {
            NotifyIconTrace.Verbose($"toast: QueryInterface(IToastNotification2) hr=0x{hr:X8}");
            return hr;
        }

        try
        {
            using HString value = new(tag);
            if (value.HResult < 0)
            {
                return value.HResult;
            }

            hr = Vtable<PutHStringFn>(notification2, ToastNotification2PutTagSlot)(notification2, value.Handle);
            NotifyIconTrace.Verbose($"toast: put_Tag('{tag}') hr=0x{hr:X8}");
            return hr;
        }
        finally
        {
            ReleaseHandle(notification2);
        }
    }

    /// <inheritdoc />
    public int SetNotificationGroup(IntPtr notification, string group)
    {
        int hr = QueryInterface(notification, ref IidToastNotification2, out IntPtr notification2);
        if (hr < 0)
        {
            NotifyIconTrace.Verbose($"toast: QueryInterface(IToastNotification2) hr=0x{hr:X8}");
            return hr;
        }

        try
        {
            using HString value = new(group);
            if (value.HResult < 0)
            {
                return value.HResult;
            }

            hr = Vtable<PutHStringFn>(notification2, ToastNotification2PutGroupSlot)(notification2, value.Handle);
            NotifyIconTrace.Verbose($"toast: put_Group('{group}') hr=0x{hr:X8}");
            return hr;
        }
        finally
        {
            ReleaseHandle(notification2);
        }
    }

    /// <inheritdoc />
    public int CreateDateTimePropertyValue(long winrtUniversalTime, out IntPtr propertyValue)
    {
        propertyValue = IntPtr.Zero;
        InitializeRuntime();

        // The statics factory is an implementation detail of this member: it is acquired and
        // released here and hands out no handle of its own.
        int hr = AcquireActivationFactory(PropertyValueClassName, ref IidPropertyValueStatics, out IntPtr statics);
        if (hr < 0)
        {
            NotifyIconTrace.Verbose($"toast: RoGetActivationFactory({PropertyValueClassName} -> IPropertyValueStatics) hr=0x{hr:X8}");
            return hr;
        }

        try
        {
            hr = Vtable<CreateDateTimeFn>(statics, PropertyValueStaticsCreateDateTimeSlot)(statics, winrtUniversalTime, out propertyValue);
            NotifyIconTrace.Verbose($"toast: PropertyValue.CreateDateTime(universalTime={winrtUniversalTime}) hr=0x{hr:X8}");
            return hr;
        }
        finally
        {
            ReleaseHandle(statics);
        }
    }

    /// <inheritdoc />
    public int SetNotificationExpirationTime(IntPtr notification, IntPtr propertyValue) =>
        Vtable<PutExpirationTimeFn>(notification, ToastPutExpirationTimeSlot)(notification, propertyValue);

    /// <inheritdoc />
    public int Show(IntPtr notifier, IntPtr notification) =>
        Vtable<ShowFn>(notifier, NotifierShowSlot)(notifier, notification);

    // The event names below are the operation names a handler-failure trace line carries: they are
    // the WinRT event the callback arrived on, not a library operation constant, because the failure
    // is the consumer handler's and happens after the toast was accepted.

    /// <inheritdoc />
    public int SubscribeActivated(IntPtr notification, ToastActivatedHandler handler, out long token) =>
        Subscribe(notification, ToastAddActivatedSlot, "Activated", (_, args) => handler(ReadActivatedArguments(args)), out token);

    /// <inheritdoc />
    public int SubscribeDismissed(IntPtr notification, ToastDismissedHandler handler, out long token) =>
        Subscribe(notification, ToastAddDismissedSlot, "Dismissed", (_, args) => handler(ReadDismissedReason(args)), out token);

    /// <inheritdoc />
    public int SubscribeFailed(IntPtr notification, ToastFailedHandler handler, out long token) =>
        Subscribe(notification, ToastAddFailedSlot, "Failed", (_, args) => handler(ReadFailedErrorCode(args)), out token);

    /// <inheritdoc />
    public int UnsubscribeActivated(IntPtr notification, long token) => Unsubscribe(notification, ToastRemoveActivatedSlot, token);

    /// <inheritdoc />
    public int UnsubscribeDismissed(IntPtr notification, long token) => Unsubscribe(notification, ToastRemoveDismissedSlot, token);

    /// <inheritdoc />
    public int UnsubscribeFailed(IntPtr notification, long token) => Unsubscribe(notification, ToastRemoveFailedSlot, token);

    // ---------------------------------------------------------------------------------------------
    // Subscription plumbing: a managed handler exposed as a WinRT TypedEventHandler.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Wraps one seam callback in a COM-callable <c>TypedEventHandler</c>, subscribes it at
    /// <paramref name="slot"/> and keeps the wrapper (and its COM-callable interface pointer) alive
    /// until the matching unsubscribe.
    /// </summary>
    /// <param name="notification">The notification whose event is subscribed.</param>
    /// <param name="slot">The <c>add_</c> vtable slot.</param>
    /// <param name="eventName">The WinRT event's name, carried into a handler-failure trace line.</param>
    /// <param name="onInvoke">The callback the wrapper invokes with the raw sender and argument pointers.</param>
    /// <param name="token">Receives the event registration token.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>add_</c>.</returns>
    private int Subscribe(IntPtr notification, int slot, string eventName, Action<IntPtr, IntPtr> onInvoke, out long token)
    {
        token = 0;

        var wrapper = new TypedEventHandler(eventName, onInvoke);
        IntPtr pointer = Marshal.GetComInterfaceForObject(wrapper, typeof(ITypedEventHandler));

        int hr = Vtable<AddEventFn>(notification, slot)(notification, pointer, out EventRegistrationToken registration);
        if (hr < 0)
        {
            // Nothing was registered, so the wrapper must not be kept alive.
            Marshal.Release(pointer);
            return hr;
        }

        lock (_subscriptionsGate)
        {
            _subscriptions[registration.Value] = (wrapper, pointer);
        }

        token = registration.Value;
        return hr;
    }

    /// <summary>
    /// Removes the handler registered under <paramref name="token"/> and releases the COM-callable
    /// wrapper. The release happens on every path, so a failing <c>remove_</c> cannot leak the
    /// wrapper it was called for.
    /// </summary>
    /// <param name="notification">The notification whose event is unsubscribed.</param>
    /// <param name="slot">The <c>remove_</c> vtable slot.</param>
    /// <param name="token">The token returned by the matching <c>add_</c>.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>remove_</c>.</returns>
    private int Unsubscribe(IntPtr notification, int slot, long token)
    {
        var registration = new EventRegistrationToken { Value = token };
        int hr = Vtable<RemoveEventFn>(notification, slot)(notification, registration);

        bool found;
        (object Owner, IntPtr Pointer) entry;

        lock (_subscriptionsGate)
        {
            found = _subscriptions.Remove(token, out entry);
        }

        if (found)
        {
            Marshal.Release(entry.Pointer);
        }

        return hr;
    }

    /// <summary>Reads the activation's argument string out of an <c>IToastActivatedEventArgs</c>.</summary>
    /// <param name="args">The raw event-argument pointer.</param>
    /// <returns>The argument string, or <see langword="null"/> when the toast carried none.</returns>
    private static string? ReadActivatedArguments(IntPtr args)
    {
        int hr = Vtable<GetHStringFn>(args, EventArgsPayloadSlot)(args, out IntPtr handle);
        if (hr < 0 || handle == IntPtr.Zero)
        {
            return null;
        }

        string? arguments = ReadHString(handle);
        WindowsDeleteString(handle);
        return arguments;
    }

    /// <summary>Reads the dismissal reason out of an <c>IToastDismissedEventArgs</c>.</summary>
    /// <param name="args">The raw event-argument pointer.</param>
    /// <returns>The raw <c>ToastDismissedReason</c> value.</returns>
    private static int ReadDismissedReason(IntPtr args)
    {
        int hr = Vtable<GetInt32Fn>(args, EventArgsPayloadSlot)(args, out int reason);
        return hr < 0 ? -1 : reason;
    }

    /// <summary>Reads the delivery error out of an <c>IToastFailedEventArgs</c>.</summary>
    /// <param name="args">The raw event-argument pointer.</param>
    /// <returns>The raw error code, or <c>0</c> when it could not be read.</returns>
    private static int ReadFailedErrorCode(IntPtr args)
    {
        int hr = Vtable<GetInt32Fn>(args, EventArgsPayloadSlot)(args, out int errorCode);
        return hr < 0 ? 0 : errorCode;
    }

    // ---------------------------------------------------------------------------------------------
    // Runtime plumbing: apartment initialization, activation factories, HSTRINGs and raw vtables.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Initializes the COM apartment for the calling thread if it is not already initialized.
    /// </summary>
    /// <remarks>
    /// The result is deliberately not gated on. Measured (M002/S01): on this machine's default
    /// multi-threaded apartment thread <c>RoInitialize(RO_INIT_SINGLETHREADED)</c> returns
    /// <c>RPC_E_CHANGED_MODE</c> (<c>0x80010106</c>) and <c>RoGetActivationFactory</c> succeeds
    /// anyway, so treating that code as a failure would refuse a working machine; on an STA thread
    /// it returns <c>S_OK</c> or <c>S_FALSE</c> (already initialized). The line is traced so a
    /// later failure can be attributed to the apartment rather than to the payload.
    /// </remarks>
    private static void InitializeRuntime()
    {
        int hr = RoInitialize(RoInitSingleThreaded);
        NotifyIconTrace.Verbose($"toast: RoInitialize(RO_INIT_SINGLETHREADED) hr=0x{hr:X8} (RPC_E_CHANGED_MODE 0x80010106 is benign)");
    }

    /// <summary>
    /// Acquires an activation factory for a runtime-class name and interface id through
    /// <c>RoGetActivationFactory</c>.
    /// </summary>
    /// <param name="className">The runtime-class name.</param>
    /// <param name="iid">The interface id to acquire.</param>
    /// <param name="factory">Receives the raw factory pointer; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>RoGetActivationFactory</c>.</returns>
    private static int AcquireActivationFactory(string className, ref Guid iid, out IntPtr factory)
    {
        factory = IntPtr.Zero;

        using HString classId = new(className);
        if (classId.HResult < 0)
        {
            return classId.HResult;
        }

        return RoGetActivationFactory(classId.Handle, ref iid, out factory);
    }

    /// <summary>Reads an HSTRING into a managed string.</summary>
    /// <param name="handle">The HSTRING handle.</param>
    /// <returns>The string, or <see langword="null"/> when the handle is null.</returns>
    private static string? ReadHString(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        IntPtr raw = WindowsGetStringRawBuffer(handle, out uint length);

        if (raw == IntPtr.Zero || length == 0)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringUni(raw, (int)length);
    }

    /// <summary>
    /// Reads the vtable slot at <paramref name="slot"/> of the interface at
    /// <paramref name="instance"/> and wraps it in the delegate type.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate signature of the method.</typeparam>
    /// <param name="instance">The interface pointer.</param>
    /// <param name="slot">The zero-based vtable slot (including <c>IUnknown</c>'s three).</param>
    /// <returns>The delegate bound to that slot.</returns>
    private static TDelegate Vtable<TDelegate>(IntPtr instance, int slot)
        where TDelegate : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(instance);
        IntPtr method = Marshal.ReadIntPtr(vtable + (slot * IntPtr.Size));
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
    }

    /// <summary>
    /// Calls <c>IUnknown.QueryInterface</c> (vtable slot 0) through the raw vtable.
    /// </summary>
    /// <param name="instance">The interface pointer to query.</param>
    /// <param name="riid">The interface id to ask for.</param>
    /// <param name="ppv">Receives the queried interface pointer; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c>.</returns>
    /// <remarks>
    /// The raw call is used rather than <see cref="Marshal.QueryInterface"/> for the same two
    /// reasons as in <see cref="ShortcutLink"/>: every call stays on the one dispatch path, and the
    /// BCL's <c>ref Guid</c> → <c>in Guid</c> signature change in .NET 9 would otherwise force a
    /// target-framework split in a class that must compile for net8/9/10-windows.
    /// </remarks>
    private static int QueryInterface(IntPtr instance, ref Guid riid, out IntPtr ppv) =>
        Vtable<QueryInterfaceFn>(instance, 0)(instance, ref riid, out ppv);

    // ---------------------------------------------------------------------------------------------
    // The WinRT declarations: interfaces, the COM-callable delegate wrapper, structures, entry points.
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

    /// <summary><c>Windows.Foundation.IAgileObject</c>: the marker that lets the shell invoke the delegate from its own thread.</summary>
    [ComImport]
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }

    /// <summary><c>Windows.Foundation.TypedEventHandler&lt;T,U&gt;</c>: the delegate interface the toast events take.</summary>
    [ComImport]
    [Guid("9DE1C534-6AE1-11E0-84E1-18A905BCC53F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITypedEventHandler : IInspectable
    {
        [PreserveSig]
        int Invoke(IntPtr sender, IntPtr args);
    }

    /// <summary>
    /// The managed implementation of <see cref="ITypedEventHandler"/> that the COM-callable
    /// wrapper exposes to the shell.
    /// </summary>
    /// <remarks>
    /// <c>Invoke</c> lands at vtable slot 6 (<c>IUnknown</c> 3 + <c>IInspectable</c> 3) and
    /// <see cref="IAgileObject"/> makes the wrapper callable from the shell's thread - the shape
    /// the M002/S01 probe measured live. A callback that throws is caught and reported as one
    /// Error-level line on the library's trace channel (the source's default Warning level emits it
    /// with no configuration), naming the event it arrived on: an exception crossing back into the
    /// shell's callback would be an unhandled exception in a thread Windows owns, and D055's posture
    /// is that a runtime toast failure is reported, never fatal. This catch is the backstop for the
    /// callback path itself - the notifier already catches a consumer handler's exception in
    /// <c>RaiseSafely</c> before it can reach here - and it deliberately does not raise
    /// <c>ToastError</c>: the failure is the handler's, not the toast's delivery.
    /// </remarks>
    [ComVisible(true)]
    private sealed class TypedEventHandler : ITypedEventHandler, IAgileObject
    {
        private readonly string _eventName;
        private readonly Action<IntPtr, IntPtr> _onInvoke;

        internal TypedEventHandler(string eventName, Action<IntPtr, IntPtr> onInvoke)
        {
            _eventName = eventName;
            _onInvoke = onInvoke;
        }

        public int GetIids(out uint count, out IntPtr iids)
        {
            count = 0;
            iids = IntPtr.Zero;
            return 0;
        }

        public int GetRuntimeClassName(out IntPtr name)
        {
            name = IntPtr.Zero;
            return ENotImpl;
        }

        public int GetTrustLevel(out int level)
        {
            level = 0;
            return 0;
        }

        public int Invoke(IntPtr sender, IntPtr args)
        {
            try
            {
                _onInvoke(sender, args);
            }
            catch (Exception exception)
            {
                NotifyIconTrace.ToastError(_eventName, 0, exception);
            }

            return 0;
        }
    }

    /// <summary>An 8-byte WinRT event registration token.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventRegistrationToken
    {
        public long Value;
    }

    /// <summary>An HSTRING holder that releases the runtime's string on every exit path.</summary>
    private sealed class HString : IDisposable
    {
        private IntPtr _handle;

        internal HString(string value)
        {
            HResult = WindowsCreateString(value, value.Length, out _handle);

            if (HResult < 0)
            {
                _handle = IntPtr.Zero;
            }
        }

        /// <summary>Gets the <c>HRESULT</c> of the underlying <c>WindowsCreateString</c>.</summary>
        internal int HResult { get; }

        /// <summary>Gets the HSTRING handle.</summary>
        internal IntPtr Handle => _handle;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                WindowsDeleteString(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    // One delegate per COM method this class calls, all HRESULT-returning, Winapi, with the
    // interface pointer as the first (self) argument.

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryInterfaceFn(IntPtr self, ref Guid riid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateToastNotifierWithIdFn(IntPtr self, IntPtr appId, out IntPtr notifier);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSettingFn(IntPtr self, out int setting);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int LoadXmlFn(IntPtr self, IntPtr xml);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateToastNotificationFn(IntPtr self, IntPtr content, out IntPtr notification);

    /// <summary><c>put_Tag</c> / <c>put_Group</c>: an HSTRING taken by value.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PutHStringFn(IntPtr self, IntPtr value);

    /// <summary>
    /// <c>IPropertyValueStatics.CreateDateTime</c>: a <c>Windows.Foundation.DateTime</c> (a single
    /// <c>INT64</c>) by value, returning the boxed value through an <c>IInspectable*</c> out-parameter.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDateTimeFn(IntPtr self, long universalTime, out IntPtr propertyValue);

    /// <summary><c>put_ExpirationTime</c>: an <c>IReference&lt;DateTime&gt;*</c> taken as an interface pointer.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PutExpirationTimeFn(IntPtr self, IntPtr propertyValue);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ShowFn(IntPtr self, IntPtr notification);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int AddEventFn(IntPtr self, IntPtr handler, out EventRegistrationToken token);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RemoveEventFn(IntPtr self, EventRegistrationToken token);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetHStringFn(IntPtr self, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetInt32Fn(IntPtr self, out int value);

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
}

/// <summary>
/// The result of one <see cref="ToastShow.Show"/> call: success carries the notifier's setting,
/// and failure carries a <em>named</em> operation plus the <c>HRESULT</c> it returned - the same
/// failure-as-data shape as <see cref="ToastIdentityResult"/> and the seam itself.
/// </summary>
/// <remarks>
/// Nothing here throws and nothing is swallowed: a failed step stops the sequence, unwinds the
/// handles already acquired and reports the seam member name that failed together with the code
/// Windows returned. <see cref="Operation"/> is <see cref="string.Empty"/> on success.
/// </remarks>
internal readonly struct ToastShowResult
{
    private ToastShowResult(
        bool success,
        string operation,
        int code,
        int notificationSetting,
        bool settingRead,
        string? appUserModelId)
    {
        Success = success;
        Operation = operation;
        Code = code;
        NotificationSetting = notificationSetting;
        SettingRead = settingRead;
        AppUserModelId = appUserModelId;
    }

    /// <summary>Gets whether the toast was handed to the shell.</summary>
    internal bool Success { get; }

    /// <summary>Gets the failing operation's name (a seam member name, or a value-level name), or empty on success.</summary>
    internal string Operation { get; }

    /// <summary>Gets the <c>HRESULT</c> of the failing step, or <c>0</c> when none applies.</summary>
    internal int Code { get; }

    /// <summary>
    /// Gets the raw <c>NotificationSetting</c> the notifier reported (<c>0</c> = enabled,
    /// <c>1</c> = disabled for the app, <c>2</c> = disabled for the user), or <c>0</c> when the
    /// setting could not be read. Interpreting it is S04's concern; it is captured here because
    /// reading it is part of the measured sequence.
    /// </summary>
    internal int NotificationSetting { get; }

    /// <summary>
    /// Gets whether <see cref="NotificationSetting"/> carries a value the notifier actually
    /// reported - <see langword="true"/> on every success and on every failure that happened after
    /// the setting step had read one.
    /// </summary>
    /// <remarks>
    /// <b>A failure before the setting step does not mean "enabled".</b>
    /// <see cref="NotificationSetting"/> is <c>0</c> both for the setting <c>Enabled</c> and for a
    /// show that failed before the setting was read, so the raw value alone cannot be interpreted:
    /// this flag is what keeps "notifications are enabled" from being reported for a show that never
    /// got as far as the read (and for a failure at the setting step itself, where the value the call
    /// produced is not trustworthy either).
    /// </remarks>
    internal bool SettingRead { get; }

    /// <summary>Gets the identity the notifier was created for.</summary>
    internal string? AppUserModelId { get; }

    /// <summary>Builds a successful result.</summary>
    /// <param name="notificationSetting">The setting the notifier reported.</param>
    /// <param name="appUserModelId">The identity the toast was shown for.</param>
    /// <returns>The result.</returns>
    /// <remarks>A success always read the setting: the step precedes the payload load and the show.</remarks>
    internal static ToastShowResult Succeeded(int notificationSetting, string appUserModelId) =>
        new(true, string.Empty, 0, notificationSetting, settingRead: true, appUserModelId);

    /// <summary>Builds a failed result.</summary>
    /// <param name="operation">The failing operation's name.</param>
    /// <param name="code">The <c>HRESULT</c> of the failure.</param>
    /// <param name="notificationSetting">The setting read so far, or <c>0</c>.</param>
    /// <param name="settingRead">Whether the setting step had already read a value.</param>
    /// <param name="appUserModelId">The identity, when it was known.</param>
    /// <returns>The result.</returns>
    internal static ToastShowResult Failed(string operation, int code, int notificationSetting, bool settingRead, string? appUserModelId) =>
        new(false, operation, code, notificationSetting, settingRead, appUserModelId);
}

/// <summary>
/// The toast show path: one show operation over the <see cref="IToastApi"/> seam, with its live
/// event subscription and the teardown that leaves nothing behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is the measured sequence, in the measured order.</b> Acquire the
/// <c>ToastNotificationManager</c> statics, acquire the <c>ToastNotification</c> factory, activate
/// the <c>XmlDocument</c>, create the notifier bound to the registered AppUserModelID, read the
/// notifier's setting, load the payload, create the notification, subscribe
/// <c>Activated</c>/<c>Dismissed</c>/<c>Failed</c>, show - then unsubscribe all three and release
/// every handle (<c>docs/TOAST-MEASUREMENT.md</c>, Contract 2). Subscribing <em>before</em>
/// <c>Show</c> is deliberate: an activation that races the display is not lost.
/// </para>
/// <para>
/// <b>Failure is data, and failure unwinds.</b> A failing step stops the sequence, reports the
/// seam member name plus the <c>HRESULT</c> in a <see cref="ToastShowResult"/>, and releases
/// everything acquired so far - including unsubscribing any handler that was already registered.
/// Nothing is reported as delivered unless Windows returned <c>S_OK</c> from <c>Show</c>, and even
/// that means "accepted", not "seen" (measured: an unregistered identity also gets <c>S_OK</c>).
/// </para>
/// <para>
/// <b>Disposal is idempotent and leaves no subscription behind.</b> <see cref="Dispose"/>
/// nulls the three callbacks, unsubscribes the three handlers and releases the notifier, the
/// notification, the document and both factories exactly once; every later call is a no-op, so a
/// caller can dispose in a <c>finally</c> without knowing whether <see cref="Show"/> ran or
/// succeeded. Nulling the callbacks is deliberate: an unsubscribe detaches the registration, but a
/// callback the shell already dequeued can still be invoked, and it must find nothing to invoke.
/// </para>
/// <para>
/// <b>One show per instance.</b> A second <see cref="Show"/> on the same instance is refused as
/// data (<see cref="OperationAlreadyShown"/>) rather than silently reusing a torn-down
/// notification: the object owns one shown toast and its subscription lifetime.
/// </para>
/// </remarks>
internal sealed class ToastShow : IDisposable
{
    /// <summary>The operation name for a missing identity - a caller error no <c>HRESULT</c> describes.</summary>
    internal const string OperationInvalidArgument = "InvalidArgument";

    /// <summary>The operation name for a second <see cref="Show"/> on the same instance.</summary>
    internal const string OperationAlreadyShown = "AlreadyShown";

    /// <summary>
    /// The operation name for the shell's asynchronous delivery failure: the
    /// <c>ToastNotification.Failed</c> callback fired for a toast this application had already
    /// shown, reporting that Windows could not deliver it.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="OperationInvalidArgument"/> and <see cref="OperationAlreadyShown"/> this
    /// name is not raised by a call of ours - the shell raises it out of band, on the thread that
    /// created the notification while that thread pumps - so its <c>code</c> is the <c>HRESULT</c>
    /// the shell reported, not one any call of ours returned.
    /// </remarks>
    internal const string OperationNotificationFailed = "NotificationFailed";

    /// <summary><c>E_INVALIDARG</c>, reported for a missing identity.</summary>
    internal const int ErrorInvalidArgument = unchecked((int)0x80070057);

    /// <summary><c>E_UNEXPECTED</c>, reported for a second show.</summary>
    internal const int ErrorAlreadyShown = unchecked((int)0x8000FFFF);

    /// <summary>
    /// <c>E_NOT_FOUND</c> (<c>0x80070490</c>): measured as the first-use result of
    /// <c>IToastNotifier.GetSetting</c> for an identity that has no notification-setting entry yet.
    /// It is benign and must not be treated as a failure.
    /// </summary>
    internal const int ErrorSettingNotFound = unchecked((int)0x80070490);

    private readonly IToastApi _api;
    private readonly ToastPayload _payload;

    /// <summary>
    /// The absolute path of the temp file the payload's resolved image reference names, or
    /// <see langword="null"/> when this show persisted nothing.
    /// </summary>
    /// <remarks>
    /// Cleared as soon as it has been deleted, so the single unwind path removes the file exactly
    /// once even when <see cref="Fail"/> has already unwound and <see cref="Dispose"/> runs after it.
    /// </remarks>
    private string? _imageFilePath;

    private IntPtr _statics;
    private IntPtr _factory;
    private IntPtr _xmlDocument;
    private IntPtr _notifier;
    private IntPtr _notification;

    /// <summary>
    /// The boxed <c>IReference&lt;DateTime&gt;</c> this show created for the expiry, tracked so it
    /// is released exactly once - at teardown on the success path and during the unwind on the
    /// failure path.
    /// </summary>
    private IntPtr _expirationPropertyValue;

    private long _activatedToken;
    private long _dismissedToken;
    private long _failedToken;
    private bool _activatedSubscribed;
    private bool _dismissedSubscribed;
    private bool _failedSubscribed;

    private int _notificationSetting;

    /// <summary>
    /// Whether <see cref="_notificationSetting"/> holds a value the setting step actually read, so a
    /// failure reported after that step carries the setting and one reported before it does not.
    /// </summary>
    /// <remarks>
    /// Set when the setting is accepted from <c>GetSetting</c> - including the benign first-use
    /// <c>E_NOT_FOUND</c>, which reads as the platform's <c>Enabled</c>. A hard failure at the
    /// setting step leaves it <see langword="false"/>: the out parameter of a failed call is not a
    /// reading, and reporting it would claim the setting is known when it is not.
    /// </remarks>
    private bool _settingRead;

    private bool _disposed;

    /// <summary>Initializes one show operation over a seam and a payload.</summary>
    /// <param name="api">The seam to drive; the production value is <c>new ToastApi()</c>.</param>
    /// <param name="payload">The payload to hand to the shell.</param>
    /// <exception cref="ArgumentNullException">The seam or the payload is <see langword="null"/>.</exception>
    internal ToastShow(IToastApi api, ToastPayload payload)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        _imageFilePath = _payload.ImageFilePath;
    }

    /// <summary>
    /// Gets or sets the callback for a body click or button press, receiving the toast's launch
    /// argument (or <see langword="null"/> when it carried none).
    /// </summary>
    internal Action<string?>? Activated { get; set; }

    /// <summary>Gets or sets the callback for a dismissal, receiving the raw <c>ToastDismissedReason</c>.</summary>
    internal Action<int>? Dismissed { get; set; }

    /// <summary>Gets or sets the callback for a delivery failure, receiving the raw error code.</summary>
    internal Action<int>? Failed { get; set; }

    /// <summary>Gets the raw <c>NotificationSetting</c> the notifier reported, or <c>0</c>.</summary>
    internal int NotificationSetting => _notificationSetting;

    /// <summary>Gets whether a notification is currently live (shown and not yet torn down).</summary>
    internal bool IsShown => _notification != IntPtr.Zero;

    /// <summary>
    /// Converts a <see cref="DateTimeOffset"/> to the instant representation
    /// <c>Windows.Foundation.DateTime</c> carries: 100-nanosecond ticks since 1601-01-01T00:00:00Z.
    /// </summary>
    /// <param name="value">The instant to convert.</param>
    /// <returns>The universal-time value for the property-value boxing step.</returns>
    /// <remarks>
    /// The epoch offset is the number of 100-ns ticks between the WinRT epoch (1601-01-01) and the
    /// CLR epoch (0001-01-01), <c>504_911_232_000_000_000</c>. The conversion goes through
    /// <c>UtcDateTime</c> so a <see cref="DateTimeOffset"/>'s own offset cannot shift the instant;
    /// 1970-01-01T00:00:00Z then yields <c>116_444_736_000_000_000</c> (the 11,644,473,600 seconds
    /// between the two epochs, in 100-ns units).
    /// </remarks>
    internal static long ToWinRtUniversalTime(DateTimeOffset value) =>
        value.UtcDateTime.Ticks - 504_911_232_000_000_000L;

    /// <summary>
    /// Runs the measured show sequence for one identity.
    /// </summary>
    /// <param name="appUserModelId">The identity the notifier must be bound to.</param>
    /// <returns>
    /// Success when the shell accepted the toast; otherwise a failure naming the seam member that
    /// failed and the <c>HRESULT</c> it returned, with every handle acquired so far released.
    /// </returns>
    internal ToastShowResult Show(string? appUserModelId)
    {
        if (_disposed || _notification != IntPtr.Zero)
        {
            return ToastShowResult.Failed(OperationAlreadyShown, ErrorAlreadyShown, _notificationSetting, _settingRead, appUserModelId);
        }

        if (string.IsNullOrEmpty(appUserModelId))
        {
            return ToastShowResult.Failed(OperationInvalidArgument, ErrorInvalidArgument, _notificationSetting, _settingRead, appUserModelId);
        }

        NotifyIconTrace.Verbose($"toast show: begin aumid='{appUserModelId}' title='{_payload.Content.Title}' launch='{_payload.Content.Launch ?? string.Empty}'");

        int hr = _api.GetToastNotificationManagerStatics(out _statics);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.GetToastNotificationManagerStatics), hr);
        }

        hr = _api.GetToastNotificationFactory(out _factory);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.GetToastNotificationFactory), hr);
        }

        hr = _api.ActivateXmlDocument(out _xmlDocument);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.ActivateXmlDocument), hr);
        }

        // The explicit-identity overload: an unpackaged process has no process default
        // AppUserModelID, so the notifier must be bound to the registered id (measured).
        hr = _api.CreateToastNotifier(_statics, appUserModelId, out _notifier);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.CreateToastNotifier), hr);
        }

        // The setting is part of the measured sequence, but a first-use E_NOT_FOUND is benign.
        hr = _api.GetNotifierSetting(_notifier, out int setting);

        if (hr < 0 && hr != ErrorSettingNotFound)
        {
            return Fail(nameof(IToastApi.GetNotifierSetting), hr);
        }

        _notificationSetting = setting;
        _settingRead = true;
        NotifyIconTrace.Verbose($"toast show: GetNotifierSetting hr=0x{hr:X8} setting={setting}{(hr == ErrorSettingNotFound ? " (E_NOT_FOUND on first use is benign)" : string.Empty)}");

        string xml = _payload.ToXml();

        hr = _api.LoadXml(_xmlDocument, xml);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.LoadXml), hr);
        }

        NotifyIconTrace.Verbose($"toast show: LoadXml hr=0x{hr:X8} xml={xml}");

        hr = _api.CreateToastNotification(_factory, _xmlDocument, out _notification);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.CreateToastNotification), hr);
        }

        // Tag, group and expiry are ToastNotification properties, not toast XML: the document has
        // no attribute for them, so they are written onto the notification object itself, in one
        // fixed order, before the subscriptions.
        ToastContent content = _payload.Content;

        if (!string.IsNullOrEmpty(content.Tag))
        {
            hr = _api.SetNotificationTag(_notification, content.Tag);
            if (hr < 0)
            {
                return Fail(nameof(IToastApi.SetNotificationTag), hr);
            }

            NotifyIconTrace.Verbose($"toast show: SetNotificationTag(put_Tag) hr=0x{hr:X8} tag='{content.Tag}'");
        }

        if (!string.IsNullOrEmpty(content.Group))
        {
            hr = _api.SetNotificationGroup(_notification, content.Group);
            if (hr < 0)
            {
                return Fail(nameof(IToastApi.SetNotificationGroup), hr);
            }

            NotifyIconTrace.Verbose($"toast show: SetNotificationGroup(put_Group) hr=0x{hr:X8} group='{content.Group}'");
        }

        if (content.Expiry is { } expiry)
        {
            long universalTime = ToWinRtUniversalTime(expiry);
            hr = _api.CreateDateTimePropertyValue(universalTime, out _expirationPropertyValue);
            if (hr < 0)
            {
                return Fail(nameof(IToastApi.CreateDateTimePropertyValue), hr);
            }

            NotifyIconTrace.Verbose($"toast show: CreateDateTimePropertyValue(CreateDateTime) hr=0x{hr:X8} universalTime={universalTime}");

            hr = _api.SetNotificationExpirationTime(_notification, _expirationPropertyValue);
            if (hr < 0)
            {
                return Fail(nameof(IToastApi.SetNotificationExpirationTime), hr);
            }

            NotifyIconTrace.Verbose($"toast show: SetNotificationExpirationTime(put_ExpirationTime) hr=0x{hr:X8} expiry='{expiry:O}'");
        }

        // Subscribe before Show so an activation that races the display is not lost.
        hr = _api.SubscribeActivated(_notification, OnActivated, out _activatedToken);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.SubscribeActivated), hr);
        }

        _activatedSubscribed = true;

        hr = _api.SubscribeDismissed(_notification, OnDismissed, out _dismissedToken);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.SubscribeDismissed), hr);
        }

        _dismissedSubscribed = true;

        hr = _api.SubscribeFailed(_notification, OnFailed, out _failedToken);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.SubscribeFailed), hr);
        }

        _failedSubscribed = true;

        NotifyIconTrace.Verbose($"toast show: subscribed activated=0x{_activatedToken:X} dismissed=0x{_dismissedToken:X} failed=0x{_failedToken:X}");

        hr = _api.Show(_notifier, _notification);
        if (hr < 0)
        {
            return Fail(nameof(IToastApi.Show), hr);
        }

        NotifyIconTrace.Verbose($"toast show: Show hr=0x{hr:X8} (accepted by the shell; delivery is judged out of process)");

        return ToastShowResult.Succeeded(_notificationSetting, appUserModelId);
    }

    /// <summary>
    /// Nulls the three callbacks, unsubscribes the three handlers and releases every handle this
    /// instance acquired.
    /// </summary>
    /// <remarks>
    /// Idempotent: after the first call every handle is zero and no seam member is invoked again.
    /// The callbacks are nulled <em>before</em> <see cref="Unwind"/> runs: unsubscribing detaches the
    /// registration, but a callback the shell already dequeued can still be invoked, and it must find
    /// nothing to invoke rather than re-enter the notifier after teardown.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NotifyIconTrace.Verbose("toast show: dispose");

        // Null the callbacks first: a dequeued callback that runs after the unsubscribe must find
        // nothing to invoke, so the show cannot reach the notifier once it has been torn down.
        Activated = null;
        Dismissed = null;
        Failed = null;

        Unwind();
    }

    /// <summary>Unsubscribes the handlers that were registered, in the measured teardown order.</summary>
    private void Unsubscribe()
    {
        if (_activatedSubscribed)
        {
            int hr = _api.UnsubscribeActivated(_notification, _activatedToken);
            _activatedSubscribed = false;
            NotifyIconTrace.Verbose($"toast show: UnsubscribeActivated(0x{_activatedToken:X}) hr=0x{hr:X8}");
        }

        if (_dismissedSubscribed)
        {
            int hr = _api.UnsubscribeDismissed(_notification, _dismissedToken);
            _dismissedSubscribed = false;
            NotifyIconTrace.Verbose($"toast show: UnsubscribeDismissed(0x{_dismissedToken:X}) hr=0x{hr:X8}");
        }

        if (_failedSubscribed)
        {
            int hr = _api.UnsubscribeFailed(_notification, _failedToken);
            _failedSubscribed = false;
            NotifyIconTrace.Verbose($"toast show: UnsubscribeFailed(0x{_failedToken:X}) hr=0x{hr:X8}");
        }
    }

    /// <summary>Releases every handle this instance acquired, in reverse acquisition order.</summary>
    /// <remarks>
    /// Only handles that were actually acquired are released: the seam's <c>ReleaseHandle</c>
    /// ignores the null handle, and a failure part-way through the sequence leaves the later
    /// handles null, so the release accounting stays exactly "one release per handle handed out".
    /// </remarks>
    private void ReleaseHandles()
    {
        Release(ref _expirationPropertyValue);
        Release(ref _notification);
        Release(ref _notifier);
        Release(ref _xmlDocument);
        Release(ref _factory);
        Release(ref _statics);
    }

    /// <summary>Releases one handle through the seam and clears the field.</summary>
    /// <param name="handle">The handle field to release; left untouched when it is already null.</param>
    private void Release(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _api.ReleaseHandle(handle);
        handle = IntPtr.Zero;
    }

    /// <summary>
    /// Unsubscribes, releases everything and removes the temp image file this show resolved - the
    /// one unwind path, shared by the failure path and the disposal.
    /// </summary>
    private void Unwind()
    {
        Unsubscribe();
        ReleaseHandles();
        DeleteImageFile();
    }

    /// <summary>
    /// Deletes the temp image file this show's payload resolved, when it resolved one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Part of the unwind, not a seam call.</b> The file is the library's own artefact - the shell
    /// only ever receives the <c>file:///</c> string - so removing it needs no <see cref="IToastApi"/>
    /// member: a new seam member would force matching edits in <c>ToastApi</c>, <c>ShortcutLink</c>
    /// and the recording fake for no gain.
    /// </para>
    /// <para>
    /// <b>Exactly once, after the unsubscribes and the releases.</b> The field is cleared before the
    /// delete runs, so a <see cref="Dispose"/> that follows a <see cref="Fail"/> finds nothing left to
    /// remove and no second delete is attempted. A delete that fails - the shell may still hold the
    /// file while it renders - is traced and swallowed: the toast is already being torn down, and a
    /// leftover file is the same end state as a process killed before teardown, which the class
    /// documents as a limitation rather than a new failure.
    /// </para>
    /// </remarks>
    private void DeleteImageFile()
    {
        if (_imageFilePath is not { } path)
        {
            return;
        }

        _imageFilePath = null;

        bool existed = File.Exists(path);

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: see the remarks on this method.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: see the remarks on this method.
        }

        NotifyIconTrace.Verbose($"toast show: delete image file='{path}' existed={existed}");
    }

    /// <summary>Traces the failure, unwinds the handles acquired so far and returns the failure as data.</summary>
    /// <param name="operation">The failing seam member's name.</param>
    /// <param name="code">The <c>HRESULT</c> it returned.</param>
    /// <returns>The failed result.</returns>
    private ToastShowResult Fail(string operation, int code)
    {
        NotifyIconTrace.Verbose($"toast show: {operation} failed hr=0x{code:X8}; unwinding the handles acquired so far");
        Unwind();
        return ToastShowResult.Failed(operation, code, _notificationSetting, _settingRead, null);
    }

    private void OnActivated(string? arguments) => Activated?.Invoke(arguments);

    private void OnDismissed(int reason) => Dismissed?.Invoke(reason);

    private void OnFailed(int errorCode) => Failed?.Invoke(errorCode);
}
