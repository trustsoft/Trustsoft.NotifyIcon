using System.IO;
using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The real <see cref="IToastApi"/> implementation for Contract 1 - the AppUserModelID identity:
/// the per-user Start-menu shortcut whose <c>System.AppUserModelID</c> property carries the
/// identity an unpackaged process needs before any toast is delivered. Contract 2 (the WinRT
/// <c>Windows.UI.Notifications</c> stack) is deliberately not implemented here; it belongs to
/// <c>ToastApi</c> (T04), which is the only class that declares the raw WinRT vtables.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the interop layer the task calls "adds no dependency".</b> Every call below is a
/// hand-written <c>[DllImport]</c> or raw-vtable dispatch against the platform; there is no COM
/// projection package, no <c>System.Windows.Forms</c>, and no WinRT contracts package, so the
/// shipped assembly's dependency graph is unchanged.
/// </para>
/// <para>
/// <b>Raw-vtable dispatch, not the RCW.</b> The M002/S01 measurement recorded that the ordinary
/// RCW path (<c>[ComImport]</c> + <c>Marshal.GetObjectForIUnknown</c>) mis-dispatches interfaces
/// that inherit <c>IInspectable</c> on this runtime, and it is correct for the plain COM
/// interfaces here. It is still not used, for a different reason: <c>Marshal.GetObjectForIUnknown</c>
/// hands the pointer's single reference to the RCW, so the seam's <c>ReleaseHandle</c> (which the
/// caller drives with <c>Marshal.Release</c>) would release a reference the RCW still owns - a
/// double release. The method calls therefore go through the raw vtable
/// (<see cref="Vtable{TDelegate}"/>), which touches no reference count, and the references are
/// owned exactly once, by the caller, through <see cref="ReleaseHandle"/>. The vtable slots follow
/// the SDK header order: <c>IUnknown</c> occupies slots 0-2, then each interface's methods in
/// declaration order.
/// </para>
/// <para>
/// <b>Failure is data, exactly as the seam requires.</b> Every member returns the raw result it
/// measured - an <c>HRESULT</c> (<c>0</c> = <c>S_OK</c>) for the COM calls, or a
/// <see langword="false"/> plus a captured Win32 code for <see cref="DeleteShortcut"/>. Nothing is
/// interpreted, named or raised here; <see cref="ToastIdentity"/> does that.
/// </para>
/// <para>
/// <b>Threading.</b> <c>CLSID_ShellLink</c> is apartment-threaded, so this class should be used on
/// an STA thread for a direct in-process object; on an MTA thread COM marshals through a proxy
/// (measured to succeed either way, but STA avoids the proxy). The only instance state is the
/// thread-static last-error slot, so one instance is safe to share.
/// </para>
/// </remarks>
internal sealed class ShortcutLink : IToastApi
{
    // ---------------------------------------------------------------------------------------------
    // COM identities and the property key, copied from the 10.0.26100 headers (the measurement's
    // authoritative source).
    // ---------------------------------------------------------------------------------------------

    // These are mutable (not readonly) because the COM interop below passes them by ref
    // (CoCreateInstance and the raw-vtable IUnknown.QueryInterface delegate take `ref Guid`;
    // IPropertyStore takes `ref PropertyKey`). A static readonly field cannot be used as a ref
    // argument (CS0199).
    private static Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static Guid IidIShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static Guid IidIPersistFile = new("0000010B-0000-0000-C000-000000000046");
    private static Guid IidIPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary><c>PKEY_AppUserModel_ID</c>: <c>{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5</c>.</summary>
    private static PropertyKey PkeyAppUserModelId = new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5,
    };

    private const uint ClsctxInprocServer = 0x1;

    /// <summary><c>VT_LPWSTR</c>: the <c>PROPVARIANT</c> type of the identity string.</summary>
    private const ushort VtLpwstr = 31;

    // ---------------------------------------------------------------------------------------------
    // Raw-vtable slot numbers (IUnknown = 0..2, then the interface's own methods in SDK order).
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>IShellLinkW.SetDescription</c>.</summary>
    private const int SetDescriptionSlot = 7;

    /// <summary><c>IShellLinkW.SetArguments</c>.</summary>
    private const int SetArgumentsSlot = 11;

    /// <summary><c>IShellLinkW.SetIconLocation</c>.</summary>
    private const int SetIconLocationSlot = 17;

    /// <summary><c>IShellLinkW.SetPath</c>.</summary>
    private const int SetPathSlot = 20;

    /// <summary><c>IPersistFile.Load</c>.</summary>
    private const int PersistLoadSlot = 5;

    /// <summary><c>IPersistFile.Save</c>.</summary>
    private const int PersistSaveSlot = 6;

    /// <summary><c>IPropertyStore.GetValue</c>.</summary>
    private const int PropertyGetValueSlot = 5;

    /// <summary><c>IPropertyStore.SetValue</c>.</summary>
    private const int PropertySetValueSlot = 6;

    /// <summary><c>IPropertyStore.Commit</c>.</summary>
    private const int PropertyCommitSlot = 7;

    /// <summary>
    /// The Win32 error code captured from the most recent failing file-system call on this thread.
    /// </summary>
    /// <remarks>
    /// Thread-static for the same reason as in <see cref="ShellApi"/>: Win32 last error is thread
    /// state, and the value must be captured at the call boundary, inside the same member.
    /// </remarks>
    [ThreadStatic]
    private static int _lastError;

    /// <inheritdoc />
    public int CreateShellLink(out IntPtr shellLink)
    {
        shellLink = IntPtr.Zero;
        return CoCreateInstance(ref ClsidShellLink, IntPtr.Zero, ClsctxInprocServer, ref IidIShellLinkW, out shellLink);
    }

    /// <inheritdoc />
    public int ConfigureShortcut(IntPtr shellLink, string targetPath, string description, string iconPath, string arguments)
    {
        // The four IShellLinkW setters, in the SDK declaration order, so the first failure is the
        // one reported and the call stops at it.
        int hr = Vtable<SetPathFn>(shellLink, SetPathSlot)(shellLink, targetPath);
        if (hr < 0)
        {
            return hr;
        }

        hr = Vtable<SetDescriptionFn>(shellLink, SetDescriptionSlot)(shellLink, description);
        if (hr < 0)
        {
            return hr;
        }

        hr = Vtable<SetIconLocationFn>(shellLink, SetIconLocationSlot)(shellLink, iconPath, 0);
        if (hr < 0)
        {
            return hr;
        }

        return Vtable<SetArgumentsFn>(shellLink, SetArgumentsSlot)(shellLink, arguments);
    }

    /// <inheritdoc />
    public int GetShortcutPropertyStore(IntPtr shellLink, out IntPtr propertyStore) =>
        QueryInterface(shellLink, ref IidIPropertyStore, out propertyStore);

    /// <inheritdoc />
    public int GetShortcutPersistFile(IntPtr shellLink, out IntPtr persistFile) =>
        QueryInterface(shellLink, ref IidIPersistFile, out persistFile);

    /// <inheritdoc />
    public int SetAppUserModelId(IntPtr propertyStore, string appUserModelId)
    {
        // IPropertyStore.SetValue copies the value out of the PROPVARIANT, so the string buffer is
        // owned by this frame and freed after the call. A VT_LPWSTR is a pointer at the variant's
        // first union slot, which is offset 8 in a 64-bit PROPVARIANT (24 bytes total).
        IntPtr buffer = Marshal.StringToCoTaskMemUni(appUserModelId);
        try
        {
            var value = new PropVariant { Vt = VtLpwstr, PointerValue = buffer };
            return Vtable<PropertySetValueFn>(propertyStore, PropertySetValueSlot)(propertyStore, ref PkeyAppUserModelId, ref value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    /// <inheritdoc />
    public int CommitPropertyStore(IntPtr propertyStore) =>
        Vtable<PropertyCommitFn>(propertyStore, PropertyCommitSlot)(propertyStore);

    /// <inheritdoc />
    public int SaveShortcut(IntPtr persistFile, string shortcutPath) =>
        Vtable<PersistSaveFn>(persistFile, PersistSaveSlot)(persistFile, shortcutPath, true);

    /// <inheritdoc />
    public int OpenShellLink(string shortcutPath, out IntPtr shellLink)
    {
        shellLink = IntPtr.Zero;

        int hr = CoCreateInstance(ref ClsidShellLink, IntPtr.Zero, ClsctxInprocServer, ref IidIShellLinkW, out shellLink);
        if (hr < 0)
        {
            return hr;
        }

        hr = QueryInterface(shellLink, ref IidIPersistFile, out IntPtr persistFile);
        if (hr < 0)
        {
            ReleaseHandle(shellLink);
            shellLink = IntPtr.Zero;
            return hr;
        }

        // Load the shortcut into the shell link, then release the IPersistFile reference - the
        // caller only needs the loaded shell link, from which it reads the property store.
        hr = Vtable<PersistLoadFn>(persistFile, PersistLoadSlot)(persistFile, shortcutPath, 0);
        ReleaseHandle(persistFile);

        if (hr < 0)
        {
            ReleaseHandle(shellLink);
            shellLink = IntPtr.Zero;
        }

        return hr;
    }

    /// <inheritdoc />
    public int GetAppUserModelId(IntPtr propertyStore, out string? appUserModelId)
    {
        appUserModelId = null;

        int hr = Vtable<PropertyGetValueFn>(propertyStore, PropertyGetValueSlot)(propertyStore, ref PkeyAppUserModelId, out PropVariant value);
        if (hr < 0)
        {
            return hr;
        }

        // A S_OK result with vt != VT_LPWSTR (measured: vt=0, VT_EMPTY, for a shortcut that
        // carries no property) reads back as null - the silent-failure case the caller must treat
        // as "the value did not read back as written", never as success. The variant owns the
        // string (CoTaskMemAlloc'd), so it is freed after the read.
        if (value.Vt == VtLpwstr && value.PointerValue != IntPtr.Zero)
        {
            appUserModelId = Marshal.PtrToStringUni(value.PointerValue);
            Marshal.FreeCoTaskMem(value.PointerValue);
        }

        return hr;
    }

    /// <inheritdoc />
    public bool DeleteShortcut(string shortcutPath)
    {
        // Already absent is success, with a quiet error channel. Otherwise delete and capture the
        // Win32 code inside this same member, exactly like ShellApi does.
        if (!File.Exists(shortcutPath))
        {
            _lastError = 0;
            return true;
        }

        bool deleted = DeleteFileW(shortcutPath);
        CaptureLastError();
        return deleted;
    }

    /// <inheritdoc />
    public int ReleaseHandle(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
        {
            return 0;
        }

        return Marshal.Release(instance);
    }

    /// <inheritdoc />
    public int GetLastError() => _lastError;

    // ---------------------------------------------------------------------------------------------
    // Contract 2 - the WinRT toast stack. Not this class's home: ToastApi (T04) owns the raw WinRT
    // vtables, and this class is the shortcut half of the seam only.
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public int GetToastNotificationManagerStatics(out IntPtr statics) => throw NotSupported(nameof(GetToastNotificationManagerStatics));

    /// <inheritdoc />
    public int GetToastNotificationFactory(out IntPtr factory) => throw NotSupported(nameof(GetToastNotificationFactory));

    /// <inheritdoc />
    public int ActivateXmlDocument(out IntPtr xmlDocument) => throw NotSupported(nameof(ActivateXmlDocument));

    /// <inheritdoc />
    public int CreateToastNotifier(IntPtr statics, string appUserModelId, out IntPtr notifier) => throw NotSupported(nameof(CreateToastNotifier));

    /// <inheritdoc />
    public int GetNotifierSetting(IntPtr notifier, out int setting) => throw NotSupported(nameof(GetNotifierSetting));

    /// <inheritdoc />
    public int LoadXml(IntPtr xmlDocument, string xml) => throw NotSupported(nameof(LoadXml));

    /// <inheritdoc />
    public int CreateToastNotification(IntPtr factory, IntPtr xmlDocument, out IntPtr notification) => throw NotSupported(nameof(CreateToastNotification));

    /// <inheritdoc />
    public int Show(IntPtr notifier, IntPtr notification) => throw NotSupported(nameof(Show));

    /// <inheritdoc />
    public int SubscribeActivated(IntPtr notification, ToastActivatedHandler handler, out long token) => throw NotSupported(nameof(SubscribeActivated));

    /// <inheritdoc />
    public int SubscribeDismissed(IntPtr notification, ToastDismissedHandler handler, out long token) => throw NotSupported(nameof(SubscribeDismissed));

    /// <inheritdoc />
    public int SubscribeFailed(IntPtr notification, ToastFailedHandler handler, out long token) => throw NotSupported(nameof(SubscribeFailed));

    /// <inheritdoc />
    public int UnsubscribeActivated(IntPtr notification, long token) => throw NotSupported(nameof(UnsubscribeActivated));

    /// <inheritdoc />
    public int UnsubscribeDismissed(IntPtr notification, long token) => throw NotSupported(nameof(UnsubscribeDismissed));

    /// <inheritdoc />
    public int UnsubscribeFailed(IntPtr notification, long token) => throw NotSupported(nameof(UnsubscribeFailed));

    // ---------------------------------------------------------------------------------------------
    // Raw-vtable dispatch and the COM method delegates.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the vtable slot at <paramref name="slot"/> on the interface at
    /// <paramref name="instance"/> and wraps it in the delegate type, so a COM method can be
    /// called without an RCW and without touching the object's reference count.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type describing the method.</typeparam>
    /// <param name="instance">The interface pointer.</param>
    /// <param name="slot">The vtable slot (0-based, including <c>IUnknown</c>'s three).</param>
    /// <returns>The delegate bound to the method.</returns>
    private static TDelegate Vtable<TDelegate>(IntPtr instance, int slot)
        where TDelegate : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(instance);
        IntPtr method = Marshal.ReadIntPtr(vtable + (slot * IntPtr.Size));
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
    }

    /// <summary>
    /// Calls <c>IUnknown.QueryInterface</c> (vtable slot 0) directly, rather than through
    /// <see cref="Marshal.QueryInterface"/>, for two reasons: it keeps every COM call on the one
    /// raw-vtable dispatch path (exactly like the method calls below), and it avoids the
    /// <c>ref Guid</c> → <c>in Guid</c> signature change the BCL made to
    /// <see cref="Marshal.QueryInterface"/> in .NET 9, which would otherwise force a TFM split in
    /// a class that must compile for net8/9/10-windows.
    /// </summary>
    /// <param name="instance">The interface pointer to query.</param>
    /// <param name="riid">The interface id to ask for.</param>
    /// <param name="ppv">Receives the queried interface pointer; <see cref="IntPtr.Zero"/> on failure.</param>
    /// <returns>The raw <c>HRESULT</c> of <c>IUnknown.QueryInterface</c>.</returns>
    private static int QueryInterface(IntPtr instance, ref Guid riid, out IntPtr ppv) =>
        Vtable<QueryInterfaceFn>(instance, 0)(instance, ref riid, out ppv);

    /// <summary>
    /// Builds the exception a Contract-2 call raises: the WinRT half of the seam has exactly one
    /// home, and it is not this class.
    /// </summary>
    /// <param name="member">The seam member that was called.</param>
    /// <returns>The exception to throw.</returns>
    private static NotSupportedException NotSupported(string member) =>
        new($"{member} is the WinRT half of the toast seam and is implemented by ToastApi (T04), not by ShortcutLink, which owns only the shortcut identity (Contract 1).");

    /// <summary>
    /// Stores the error code the just-returned <c>DeleteFileW</c> reported, as the first statement
    /// after that call.
    /// </summary>
    private static void CaptureLastError() => _lastError = Marshal.GetLastPInvokeError();

    // ---------------------------------------------------------------------------------------------
    // Delegates: one per COM method the shortcut path calls, all HRESULT-returning, stdcall, with
    // the interface pointer as the first (self) argument.
    // ---------------------------------------------------------------------------------------------

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceFn(IntPtr self, ref Guid riid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPathFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string pszFile);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDescriptionFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string pszName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetIconLocationFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetArgumentsFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PersistLoadFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PersistSaveFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PropertyGetValueFn(IntPtr self, ref PropertyKey key, out PropVariant value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PropertySetValueFn(IntPtr self, ref PropertyKey key, ref PropVariant value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PropertyCommitFn(IntPtr self);

    // ---------------------------------------------------------------------------------------------
    // Structures and entry points.
    // ---------------------------------------------------------------------------------------------

    /// <summary>A <c>PROPERTYKEY</c>: a format id plus a property id (20 bytes, blittable).</summary>
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
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("kernel32.dll", EntryPoint = "DeleteFileW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool DeleteFileW([MarshalAs(UnmanagedType.LPWStr)] string path);
}
