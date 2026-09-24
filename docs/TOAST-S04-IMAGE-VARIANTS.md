# M002/S04 task T01 — the toast image contract, measured live with the independent probe

Scope: **M002/S04/T01**. This document is the measurement record for the three facts S02 deliberately
left open when it handed the image half of R017 to S04: whether a `ToastGeneric` `<image>` is accepted
by `IXmlDocumentIO.LoadXml` with and without the schema's `id` attribute, whether the shell reports a
**missing** local file at all, and what the platform actually stores for a `file:///` reference.

It records only what the instrument produced. Where a reading needs a human eye, the document says so
instead of inferring it into a pass.

## The instrument

`scripts/probe-toast` is a hand-written WinRT program: it is absent from `Trustsoft.NotifyIcon.sln`
and takes no project reference to the library, so its interop declarations are independent of the code
they measure. T01 added one image variant to it plus one runner:

- `scripts/probe-toast/Program.cs` — new switches `--image <absolute path>`,
  `--image-placement hero|appLogoOverride`, `--image-id <n>` and `--delete-image-after <seconds>`.
  The program injects one `<image src="…" placement="…" [id="n"]/>` element as the last child of the
  `ToastGeneric` binding (the position S02 pins), prints the exact XML handed to `LoadXml`, prints the
  image file's existence and byte length at send time, and can delete the file part-way through its
  existing wait loop. It keeps the program's strict argument style: an unrecognised argument, an image
  sub-option without `--image`, a relative path, or a path that cannot be expressed as a file URI is a
  usage error and exits `2`.
- `scripts/probe-toast/run-image-variants.ps1` — creates the test images (a 364×180 hero and a 48×48
  app-logo copy, both with a real `89 50 4E 47` PNG signature), drives the four variants, tees every
  `[probe]` line to a per-variant log, adds the out-of-process `--history` inventory, and finally
  searches a **copy** of the platform database for the reference. It exits non-zero when a variant is
  missing an expected reading. It invokes the probe's apphost `probe-toast.exe` (falling back to the
  framework-dependent `.dll` through an absolute `dotnet.exe` path), so the run does not depend on an
  extensionless `dotnet` being resolvable on `PATH`.

**The `src` the instrument feeds the shell is the absolute `file:///` reference**, derived with
`new Uri(path).AbsoluteUri` — the rule R6 assigns to the library's own resolver — not the raw Windows
path. The path is what the existence and byte-length readings are taken from. That is why the space
and non-ASCII variant below exercises URI escaping rather than a raw path.

## Environment and how to reproduce

Measured 2026-09-23 on `MINIBOOKX`, Windows `10.0.26200.0` (Windows 11), `net8.0-windows`,
AUMID `Trustsoft.NotifyIcon.ToastProbe.Image`.

```text
dotnet build scripts/probe-toast/ProbeToast.csproj -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-toast/run-image-variants.ps1 -WaitSeconds 3
```

The runner's default `-WaitSeconds` is 6; the captured run below used 3, which still exceeds the 2 s
delete point. `gsd_exec`'s sandbox strips Windows environment variables, so a run there needs
`APPDATA`, `LOCALAPPDATA`, `ProgramData`, `ProgramFiles(x86)`, `DOTNET_CLI_HOME` **and `PATHEXT`**
exported; a normal shell already has them. `PATHEXT` matters because PowerShell refuses to run a `.exe`
it cannot classify as an application (it reports `Cannot run a document in the middle of a pipeline`),
and it cannot resolve an extensionless `dotnet` either — so a stripped `PATHEXT` breaks both the
apphost and the `dotnet` fallback while their directories sit on `PATH`. The probe registers
`…\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk` on every show and the runner removes
nothing — the capture names it so the run is not mistaken for side-effect free.

## The four variants, in one table

| Variant | `src` | `placement` | `id` | file at send | `LoadXml` | `CreateToastNotification` | `Show` | `Failed` callback |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `no-id` | `file:///C:/…/toast-hero.png` | `hero` | omitted | exists, 770 B | `0x00000000` | `0x00000000` | `0x00000000` | none (`failed=0`) |
| `id-1-applogo-space-nonascii` | `file:///C:/…/image%20variants%20%C3%A4%C3%B6%C3%BC/toast-applogo.png` | `appLogoOverride` | `1` | exists, 191 B | `0x00000000` | `0x00000000` | `0x00000000` | none |
| `missing-file` | `file:///C:/…/this-image-was-never-created.png` | `hero` | omitted | **absent** | `0x00000000` | `0x00000000` | `0x00000000` | none |
| `delete-at-t-plus-2s` | `file:///C:/…/toast-hero.png` | `hero` | omitted | exists 770 B, deleted at t=2.0 s | `0x00000000` | `0x00000000` | `0x00000000` | none |

## Raw capture

The runner's complete output, verbatim (its own lines and every `[probe]` line it tee'd):

```text
probe image variants: start 2026-09-23 21:26:17 on MINIBOOKX; wait-seconds=3; delete-after=2s
runner: the probe registers 'C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk' when it shows a toast; this runner does not remove it.
runner: created 'C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png' bytes=770 magic=89 50 4E 47 0D 0A 1A 0A
runner: created 'C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\image variants äöü\toast-applogo.png' bytes=191 magic=89 50 4E 47 0D 0A 1A 0A
runner: the missing-file variant points at 'C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\this-image-was-never-created.png' (exists=False)

=== variant: no-id ===
runner: C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M002\scripts\probe-toast\bin\Release\net8.0-windows\probe-toast.exe --aumid Trustsoft.NotifyIcon.ToastProbe.Image --wait-seconds 3 --image C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png --image-placement hero
[probe] probe-toast start 2026-09-23 21:26:17; os=Microsoft Windows NT 10.0.26200.0; machine=MINIBOOKX; pid=14380
[probe] aumid=Trustsoft.NotifyIcon.ToastProbe.Image; skip-register=False; wait-seconds=3; expect-no-toast=False
[probe] image: configured path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png'; placement=hero; id=(none); delete-image-after=(never)
[probe] identity: shortcut path=C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID before write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: CoCreateInstance(CLSID_ShellLink) hr=0x00000000
[probe] identity: IShellLinkW.SetPath hr=0x00000000; SetDescription hr=0x00000000; SetIconLocation hr=0x00000000; SetArguments hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPersistFile) hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPropertyStore) hr=0x00000000
[probe] identity: IPropertyStore.SetValue(PKEY_AppUserModel_ID, 'Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] identity: IPropertyStore.Commit hr=0x00000000
[probe] identity: IPersistFile.Save('C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk') hr=0x00000000
[probe] identity: write shortcut hr=0x00000000
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID after write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: read-back matches expected 'Trustsoft.NotifyIcon.ToastProbe.Image' = True
[probe] factory: RoInitialize hr=0x80010106
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
[probe] factory: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
[probe] show: CreateToastNotifierWithId('Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] show: notifier.GetSetting hr=0x00000000 value=0 (Enabled)
[probe] show: QueryInterface(XmlDocument -> IXmlDocumentIO) hr=0x00000000
[probe] show: QueryInterface(XmlDocument -> IXmlDocument) hr=0x00000000
[probe] show: payload xml=<toast launch="probe-activation"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon toast probe</text><text>Click this banner to prove in-process activation.</text><image src="file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png" placement="hero"/></binding></visual></toast>
[probe] image: path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png'; placement=hero; id=(none)
[probe] image: at send exists=True bytes=770
[probe] show: IXmlDocumentIO.LoadXml hr=0x00000000
[probe] show: CreateToastNotification(xml) hr=0x00000000
[probe] subscribe: add_Activated hr=0x00000000 token=0xEB71730F476E84B5
[probe] subscribe: add_Dismissed hr=0x00000000 token=0x307A30AC11B2365E
[probe] subscribe: add_Failed hr=0x00000000 token=0xF04F3FFCE81A0BF2
[probe] show: notifier.Show(toast) hr=0x00000000
[probe] wait: pumping for 3s for a click on the toast body
[probe] result: activated=0 arguments=(null) dismissed=0 reason=-1 failed=0 errorCode=0x00000000
[probe] teardown: remove_Activated hr=0x00000000; remove_Dismissed hr=0x00000000; remove_Failed hr=0x00000000
[probe] verdict: positive run complete; delivery is judged out of process by --history.
runner: variant no-id exit=0 log=C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\variant-no-id.log

=== variant: id-1-applogo-space-nonascii ===
runner: C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M002\scripts\probe-toast\bin\Release\net8.0-windows\probe-toast.exe --aumid Trustsoft.NotifyIcon.ToastProbe.Image --wait-seconds 3 --image C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\image variants äöü\toast-applogo.png --image-placement appLogoOverride --image-id 1
[probe] probe-toast start 2026-09-23 21:26:21; os=Microsoft Windows NT 10.0.26200.0; machine=MINIBOOKX; pid=14736
[probe] aumid=Trustsoft.NotifyIcon.ToastProbe.Image; skip-register=False; wait-seconds=3; expect-no-toast=False
[probe] image: configured path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\image variants äöü\toast-applogo.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/image%20variants%20%C3%A4%C3%B6%C3%BC/toast-applogo.png'; placement=appLogoOverride; id=1; delete-image-after=(never)
[probe] identity: shortcut path=C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID before write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: CoCreateInstance(CLSID_ShellLink) hr=0x00000000
[probe] identity: IShellLinkW.SetPath hr=0x00000000; SetDescription hr=0x00000000; SetIconLocation hr=0x00000000; SetArguments hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPersistFile) hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPropertyStore) hr=0x00000000
[probe] identity: IPropertyStore.SetValue(PKEY_AppUserModel_ID, 'Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] identity: IPropertyStore.Commit hr=0x00000000
[probe] identity: IPersistFile.Save('C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk') hr=0x00000000
[probe] identity: write shortcut hr=0x00000000
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID after write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: read-back matches expected 'Trustsoft.NotifyIcon.ToastProbe.Image' = True
[probe] factory: RoInitialize hr=0x80010106
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
[probe] factory: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
[probe] show: CreateToastNotifierWithId('Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] show: notifier.GetSetting hr=0x00000000 value=0 (Enabled)
[probe] show: QueryInterface(XmlDocument -> IXmlDocumentIO) hr=0x00000000
[probe] show: QueryInterface(XmlDocument -> IXmlDocument) hr=0x00000000
[probe] show: payload xml=<toast launch="probe-activation"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon toast probe</text><text>Click this banner to prove in-process activation.</text><image src="file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/image%20variants%20%C3%A4%C3%B6%C3%BC/toast-applogo.png" placement="appLogoOverride" id="1"/></binding></visual></toast>
[probe] image: path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\image variants äöü\toast-applogo.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/image%20variants%20%C3%A4%C3%B6%C3%BC/toast-applogo.png'; placement=appLogoOverride; id=1
[probe] image: at send exists=True bytes=191
[probe] show: IXmlDocumentIO.LoadXml hr=0x00000000
[probe] show: CreateToastNotification(xml) hr=0x00000000
[probe] subscribe: add_Activated hr=0x00000000 token=0xD132894CF1FFF855
[probe] subscribe: add_Dismissed hr=0x00000000 token=0x7F811BBF21B3127D
[probe] subscribe: add_Failed hr=0x00000000 token=0xB7ABF8A67B087F03
[probe] show: notifier.Show(toast) hr=0x00000000
[probe] wait: pumping for 3s for a click on the toast body
[probe] result: activated=0 arguments=(null) dismissed=0 reason=-1 failed=0 errorCode=0x00000000
[probe] teardown: remove_Activated hr=0x00000000; remove_Dismissed hr=0x00000000; remove_Failed hr=0x00000000
[probe] verdict: positive run complete; delivery is judged out of process by --history.
runner: variant id-1-applogo-space-nonascii exit=0 log=C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\variant-id-1-applogo-space-nonascii.log

=== variant: missing-file ===
runner: C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M002\scripts\probe-toast\bin\Release\net8.0-windows\probe-toast.exe --aumid Trustsoft.NotifyIcon.ToastProbe.Image --wait-seconds 3 --image C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\this-image-was-never-created.png --image-placement hero
[probe] probe-toast start 2026-09-23 21:26:24; os=Microsoft Windows NT 10.0.26200.0; machine=MINIBOOKX; pid=12104
[probe] aumid=Trustsoft.NotifyIcon.ToastProbe.Image; skip-register=False; wait-seconds=3; expect-no-toast=False
[probe] image: configured path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\this-image-was-never-created.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/this-image-was-never-created.png'; placement=hero; id=(none); delete-image-after=(never)
[probe] identity: shortcut path=C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID before write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: CoCreateInstance(CLSID_ShellLink) hr=0x00000000
[probe] identity: IShellLinkW.SetPath hr=0x00000000; SetDescription hr=0x00000000; SetIconLocation hr=0x00000000; SetArguments hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPersistFile) hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPropertyStore) hr=0x00000000
[probe] identity: IPropertyStore.SetValue(PKEY_AppUserModel_ID, 'Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] identity: IPropertyStore.Commit hr=0x00000000
[probe] identity: IPersistFile.Save('C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk') hr=0x00000000
[probe] identity: write shortcut hr=0x00000000
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID after write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: read-back matches expected 'Trustsoft.NotifyIcon.ToastProbe.Image' = True
[probe] factory: RoInitialize hr=0x80010106
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
[probe] factory: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
[probe] show: CreateToastNotifierWithId('Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] show: notifier.GetSetting hr=0x00000000 value=0 (Enabled)
[probe] show: QueryInterface(XmlDocument -> IXmlDocumentIO) hr=0x00000000
[probe] show: QueryInterface(XmlDocument -> IXmlDocument) hr=0x00000000
[probe] show: payload xml=<toast launch="probe-activation"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon toast probe</text><text>Click this banner to prove in-process activation.</text><image src="file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/this-image-was-never-created.png" placement="hero"/></binding></visual></toast>
[probe] image: path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\this-image-was-never-created.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/this-image-was-never-created.png'; placement=hero; id=(none)
[probe] image: at send exists=False bytes=(absent)
[probe] show: IXmlDocumentIO.LoadXml hr=0x00000000
[probe] show: CreateToastNotification(xml) hr=0x00000000
[probe] subscribe: add_Activated hr=0x00000000 token=0x27023A13101251AC
[probe] subscribe: add_Dismissed hr=0x00000000 token=0x3CAD337159B5DDF1
[probe] subscribe: add_Failed hr=0x00000000 token=0xDFE67B6530751675
[probe] show: notifier.Show(toast) hr=0x00000000
[probe] wait: pumping for 3s for a click on the toast body
[probe] result: activated=0 arguments=(null) dismissed=0 reason=-1 failed=0 errorCode=0x00000000
[probe] teardown: remove_Activated hr=0x00000000; remove_Dismissed hr=0x00000000; remove_Failed hr=0x00000000
[probe] verdict: positive run complete; delivery is judged out of process by --history.
runner: variant missing-file exit=0 log=C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\variant-missing-file.log

=== variant: delete-at-t-plus-2s ===
runner: C:\Users\Maxim\Desktop\Trustsoft.NotifyIcon\.gsd-worktrees\M002\scripts\probe-toast\bin\Release\net8.0-windows\probe-toast.exe --aumid Trustsoft.NotifyIcon.ToastProbe.Image --wait-seconds 3 --image C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png --image-placement hero --delete-image-after 2
[probe] probe-toast start 2026-09-23 21:26:27; os=Microsoft Windows NT 10.0.26200.0; machine=MINIBOOKX; pid=7020
[probe] aumid=Trustsoft.NotifyIcon.ToastProbe.Image; skip-register=False; wait-seconds=3; expect-no-toast=False
[probe] image: configured path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png'; placement=hero; id=(none); delete-image-after=2
[probe] identity: shortcut path=C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID before write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: CoCreateInstance(CLSID_ShellLink) hr=0x00000000
[probe] identity: IShellLinkW.SetPath hr=0x00000000; SetDescription hr=0x00000000; SetIconLocation hr=0x00000000; SetArguments hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPersistFile) hr=0x00000000
[probe] identity: QueryInterface(IShellLinkW -> IPropertyStore) hr=0x00000000
[probe] identity: IPropertyStore.SetValue(PKEY_AppUserModel_ID, 'Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] identity: IPropertyStore.Commit hr=0x00000000
[probe] identity: IPersistFile.Save('C:\Users\Maxim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Trustsoft.NotifyIcon.ToastProbe.lnk') hr=0x00000000
[probe] identity: write shortcut hr=0x00000000
[probe] identity: read-back IPropertyStore.GetValue hr=0x00000000 vt=31
[probe] identity: AppUserModelID after write = 'Trustsoft.NotifyIcon.ToastProbe.Image'
[probe] identity: read-back matches expected 'Trustsoft.NotifyIcon.ToastProbe.Image' = True
[probe] factory: RoInitialize hr=0x80010106
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotificationManager -> IToastNotificationManagerStatics) hr=0x00000000
[probe] factory: RoGetActivationFactory(Windows.UI.Notifications.ToastNotification -> IToastNotificationFactory) hr=0x00000000
[probe] factory: RoActivateInstance(Windows.Data.Xml.Dom.XmlDocument) hr=0x00000000
[probe] show: CreateToastNotifierWithId('Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] show: notifier.GetSetting hr=0x00000000 value=0 (Enabled)
[probe] show: QueryInterface(XmlDocument -> IXmlDocumentIO) hr=0x00000000
[probe] show: QueryInterface(XmlDocument -> IXmlDocument) hr=0x00000000
[probe] show: payload xml=<toast launch="probe-activation"><visual><binding template="ToastGeneric"><text>Trustsoft.NotifyIcon toast probe</text><text>Click this banner to prove in-process activation.</text><image src="file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png" placement="hero"/></binding></visual></toast>
[probe] image: path='C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png'; src='file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png'; placement=hero; id=(none)
[probe] image: at send exists=True bytes=770
[probe] show: IXmlDocumentIO.LoadXml hr=0x00000000
[probe] show: CreateToastNotification(xml) hr=0x00000000
[probe] subscribe: add_Activated hr=0x00000000 token=0x9437E5F17CD0D1DB
[probe] subscribe: add_Dismissed hr=0x00000000 token=0x8927FE94A8AC8A77
[probe] subscribe: add_Failed hr=0x00000000 token=0x75FCEFCC56FA19AE
[probe] show: notifier.Show(toast) hr=0x00000000
[probe] wait: pumping for 3s for a click on the toast body
[probe] image: delete requested after 2s, deleted at t=2.0s; after delete exists=False bytes=(absent)
[probe] result: activated=0 arguments=(null) dismissed=0 reason=-1 failed=0 errorCode=0x00000000
[probe] teardown: remove_Activated hr=0x00000000; remove_Dismissed hr=0x00000000; remove_Failed hr=0x00000000
[probe] verdict: positive run complete; delivery is judged out of process by --history.
runner: variant delete-at-t-plus-2s exit=0 log=C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\variant-delete-at-t-plus-2s.log
runner: independent check after the delete-while-live variant: 'C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\toast-hero.png' is absent

--- node: out-of-process delivery inventory ---
[probe] probe-toast start 2026-09-23 21:26:30; os=Microsoft Windows NT 10.0.26200.0; machine=MINIBOOKX; pid=14344
[probe] aumid=Trustsoft.NotifyIcon.ToastProbe; skip-register=False; wait-seconds=20; expect-no-toast=False
[probe] history: RoGetActivationFactory(ToastNotificationManager -> IToastNotificationManagerStatics2) hr=0x00000000
[probe] history: get_History hr=0x00000000
[probe] history: QueryInterface(History -> IToastNotificationHistory2) hr=0x00000000
[probe] history: GetHistoryWithId('Trustsoft.NotifyIcon.ToastProbe.Image') hr=0x00000000
[probe] history: IVectorView.get_Size hr=0x00000000 count=1
[probe] history verdict: count=1 for 'Trustsoft.NotifyIcon.ToastProbe.Image'
runner: history count for 'Trustsoft.NotifyIcon.ToastProbe.Image' after the four variants: 1 (a snapshot - the Action Center entry can be purged between runs, so a 0 here is a reading, not a variant failure)

--- node: what a copy of the notification database holds ---
dbcopy: copied 'C:\Users\Maxim\AppData\Local\Microsoft\Windows\Notifications\wpndatabase.db' -> 'C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\wpndatabase-copy\wpndatabase.db' bytes=1048576
dbcopy: copied 'C:\Users\Maxim\AppData\Local\Microsoft\Windows\Notifications\wpndatabase.db-wal' -> 'C:\Users\Maxim\AppData\Local\Temp\probe-image-variants\wpndatabase-copy\wpndatabase.db-wal' bytes=2138312
dbcopy: 'wpndatabase.db' contains a file URI at all: latin1=False utf8=False utf16le=False
dbcopy: 'wpndatabase.db' contains 'file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png': latin1=False utf8=False utf16le=False
dbcopy: 'wpndatabase.db' contains 'file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/image%20variants%20%C3%A4%C3%B6%C3%BC/toast-applogo.png': latin1=False utf8=False utf16le=False
dbcopy: 'wpndatabase.db' contains 'toast-hero.png': latin1=False utf8=False utf16le=False
dbcopy: 'wpndatabase.db' contains 'toast-applogo.png': latin1=False utf8=False utf16le=False
dbcopy: 'wpndatabase.db-wal' contains a file URI at all: latin1=True utf8=True utf16le=False
dbcopy: 'wpndatabase.db-wal' contains 'file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png': latin1=True utf8=True utf16le=False
dbcopy: 'wpndatabase.db-wal' contains 'file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/image%20variants%20%C3%A4%C3%B6%C3%BC/toast-applogo.png': latin1=True utf8=True utf16le=False
dbcopy: 'wpndatabase.db-wal' contains 'toast-hero.png': latin1=True utf8=True utf16le=False
dbcopy: 'wpndatabase.db-wal' contains 'toast-applogo.png': latin1=True utf8=True utf16le=False
dbcopy: verdict - a variant's image reference is present in the database copy: True
dbcopy: the downloaded-image cache 'C:\Users\Maxim\AppData\Local\Microsoft\Windows\Notifications\wpnidm' holds 0 entries (a local file:/// image is rendered from disk, not cached)
dbcopy: note - the banner painting the picture is NOT machine-visible from here; only a human can confirm the image rendered.

runner verdict: PASS (every variant printed its expected readings)
```

## Settled readings

Each item is labelled **measured-here** (a line captured in this task), **bounded** (measured, but only
within the window the instrument observed) or **not machine-visible**.

### 1. The `id` attribute is not required — S02's omission stands

**Measured-here.** The payloads with `placement="hero"` (no `id`) and with
`placement="appLogoOverride" id="1"` produced the same HRESULT chain:
`IXmlDocumentIO.LoadXml hr=0x00000000`, `CreateToastNotification(xml) hr=0x00000000`,
`notifier.Show(toast) hr=0x00000000`, and `result: … failed=0 errorCode=0x00000000`. Nothing
observable differs between them.

**The `id` decision this implies:** keep omitting `id`. S02 pinned that choice (the image shapes in
`docs/TOAST-MEASUREMENT.md` §S02.1 have no `id`, and its exact-string tests pin the omission), and the
schema's "Required" marking on `id` is the tile-template heritage, not a `ToastGeneric` acceptance
rule. `ToastPayload` needs no change and the 27 exact-string payload tests stay as they are.

### 2. A missing local file is NOT reported by the shell

**Measured-here.** With `src` pointing at a file that does not exist
(`exists=False bytes=(absent)` at send time), `LoadXml`, `CreateToastNotification` and `Show` all
returned `0x00000000`, the notifier reported `GetSetting … value=0 (Enabled)`, and the `Failed` callback
never fired — `failed=0 errorCode=0x00000000` — for the whole wait window. No
`0x803E0202 WPN_E_IMAGE_NOT_FOUND_IN_CACHE` (or any other code) arrived.

**What it implies:** the candidate "let the shell tell us the image is missing" design is dead. The
**library's own pre-show `File.Exists` check is the only honest "image missing" report** the consumer
can get, because the shell accepts the payload and silently drops the image. That check, and the
outcome it reports, is therefore a design requirement for the library side of this slice, not an
optional courtesy.

### 3. The platform stores the `file:///` reference, not the image bytes

**Measured-here.** A copy of the notification platform's database was searched for each variant's
absolute reference:

- the main `wpndatabase.db` copy (1 048 576 B) contained **no** `file:///` string at all;
- its write-ahead log copy `wpndatabase.db-wal` (2 138 312 B in the captured run) contained both
  references **verbatim** —
  `file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/toast-hero.png` and the
  percent-escaped
  `file:///C:/Users/Maxim/AppData/Local/Temp/probe-image-variants/image%20variants%20%C3%A4%C3%B6%C3%BC/toast-applogo.png`
  — as single-byte UTF-8 (`latin1=True utf8=True utf16le=False`);
- the platform's downloaded-image cache `…\Microsoft\Windows\Notifications\wpnidm` held **0 entries**.
- **Re-capture (same runner, later run):** the main `wpndatabase.db` copy (still 1 048 576 B) **also**
  held both references verbatim (`latin1=True utf8=True`), with the write-ahead log down to 90 672 B.
  Which of the two files carries the string is therefore a **checkpoint-timing detail, not a fact
  about the platform**: the row is written to the WAL first and folded into the main file when the
  platform checkpoints. Both captures agree on the substantive reading - the URI string is present,
  the image bytes are not.

**What the wpndatabase copy shows about the reference-versus-bytes question:** the reference side wins.
The platform keeps the URI string in its own notification state and holds no copy of the image; the
shell reads the file from disk when it renders. That is precisely why the file must outlive the
notification, and why "own the file from `Show` until the show is torn down" is the correct lifetime
rule.

**Instrument note:** searching only `wpndatabase.db` would have produced a misleading "not present" in
the first capture — the un-checkpointed row lived in the WAL. The runner copies and searches both and
reports which one hit. Because the split between the two files moves with checkpointing, **a
"not present in `wpndatabase.db`" reading is only meaningful together with the `-wal` result**; never
read either file alone.

### 4. Deleting the file while the notification is live — bounded reading

**Bounded.** The file was deleted at t=2.0 s with the notification already shown (`Show` had returned
`0x00000000`), the pump continued to t=3 s, and still `failed=0 errorCode=0x00000000` — nothing
observable. The runner's independent check after the run confirmed the file was gone.

**What it implies:** deleting at show teardown is safe for everything this instrument can see, so the
conservative rule T02 encodes — own the file from `Show` until the show is torn down and delete it in
the one unwind path — is not contradicted by the platform. It is *not* evidence that deleting
immediately after `Show` returns is safe: the banner and the Action Center can render the image later,
and this instrument observed only a 3 s window in one process. Do not weaken the rule on this reading.

### 5. Not machine-visible

- **Whether the banner actually painted the picture** — the pixels, the crop, the app-logo versus hero
  scaling — cannot be established from here. The instrument records HRESULTs, callbacks, file state and
  database bytes; it has no eye. **A human looking at the screen is required**, and this measurement
  claims nothing about rendering.
- **The raw (non-URI) Windows path form** was not measured: the probe always feeds the absolute
  `file:///` reference. The raw-path spelling is therefore neither claimed supported nor claimed broken.
- **Windows 11 Focus Assist / Do Not Disturb** was not exercised (no public detector; S03's settings
  measurement owns the disabled path). Nothing here asserts anything about it.

### 6. Findings that ride along

- The out-of-process history verdict was `count=1` in the captured run, but the same runner read
  `count=1`, `count=0` and `count=3` across captures. The payloads carry no tag/group, and the count
  moves with Action Center purge and coalescing timing, so the inventory only confirms that **the last
  payload reached the platform**. It is a **snapshot**: the runner reports it without making it a
  variant failure, and no claim is drawn here about entries replacing or accumulating, because the
  captures do not settle that question either way.
- `notifier.GetSetting` reported `0 (Enabled)` on every run — this run did not script the disabled
  state (S03's measurement), so nothing here contradicts it.
- `RoInitialize hr=0x80010106` (`RPC_E_CHANGED_MODE`) appears on every run: pre-existing and benign,
  the probe is already an STA thread and every factory still resolves.
- A path containing a **space and a non-ASCII character** survived the R6 rule exactly like the
  ASCII-only path: `new Uri(path).AbsoluteUri` produced `%20` and `%C3%A4%C3%B6%C3%BC`, the shell
  accepted the escaped reference, and the database copy held the escaped string. R6's escaping risk is
  measured, not assumed.

## Failure Modes (Q5)

| External dependency | Failure path | Handling in this task |
| --- | --- | --- |
| Image file on disk (read) | absent or unreadable | `DescribeImageFile` reports `exists=False bytes=(absent)` or `exists=unknown read-failed='…'` and never throws. This is the `missing-file` variant, and the reading is a result, not an error. |
| Image file on disk (delete) | delete denied or file already gone | `TryDeleteImage` catches, prints `[probe] image: delete failed: …` and continues pumping; the runner's expected `deleted at t=…; after delete exists=False` reading then fails loudly rather than silently passing. |
| Usage surface | unknown argument, relative path, sub-option without `--image`, path not expressible as a file URI | Explicit `usage` error and exit `2` — the instrument does not ignore arguments it does not understand. |
| Platform / WinRT | activation-factory, `LoadXml` or `CreateToastNotification` failure | Every HRESULT is printed; a failed factory or `CreateToastNotification` exits `1`; the runner treats a non-zero probe exit as a variant failure. |
| Notification database (read) | live file locked or absent | Copied with `[System.IO.File]::Copy` inside `try/catch`, reported as `dbcopy: copying … failed: …` or `dbcopy: '…' is absent`; the live file is never written and never read in place. |
| PowerShell image creation | `System.Drawing` unavailable | `Add-Type -AssemblyName System.Drawing` throws PowerShell's own error at startup; the PNG signature is printed per image so a bad image cannot silently reach the probe. |
| Probe build | not built | The runner checks for the apphost `probe-toast.exe` (then the `.dll`) and throws with the exact build command before running anything. |

## Load Profile (Q6)

There is no service-level load dimension: this is a one-shot measurement instrument that runs **four
sequential short-lived processes**, one toast each, copies a ~1 MB database plus its WAL, and scans
those copies once. The resource that saturates first at 10× is the **interactive notification surface**
— 40 toasts on the user's desktop — followed by the sequential wall time
(`variants × WaitSeconds`). There is no pool, queue or retry to size; the protection is structural: the
variant list is fixed in the runner, the wait is bounded by `-WaitSeconds`, the database is only ever
read as a copy, and nothing loops.

## Negative Tests (Q7)

The negative surface of this task **is** the measurement, so the negative cases are first-class variants
rather than an afterthought:

| Negative case | Where it is asserted |
| --- | --- |
| `src` names a file that does not exist | `missing-file` variant — expects `image: at send exists=False bytes=(absent)` **and** still expects the `LoadXml` / `CreateToastNotification` / `Show` / `result` lines, so "the shell silently accepted a missing image" is a pinned, asserted reading. |
| The image is deleted while the notification is live | `delete-at-t-plus-2s` variant — expects `delete requested after 2s, deleted at t=…; after delete exists=False`, plus the absent-file check the runner repeats independently after the run. |
| A path with a space and a non-ASCII character | `id-1-applogo-space-nonascii` variant — `src` must be the escaped `file:///…%20…%C3%A4%C3%B6%C3%BC…` reference and must still be accepted. |
| Usage errors: unknown argument, relative `--image`, `--image-placement` without `--image`, bad placement, non-numeric id | The probe's argument parser, exit `2` with the usage line (demonstrated for `--image rel.png`, `--image-id 1` without `--image`, `--image-placement bogus --image C:\x.png` and `--delete-image-after 3` without `--image`). |
| A variant that silently changes the payload | `run-image-variants.ps1` asserts the exact `<image …>` spelling per variant (`placement="hero"` versus `placement="appLogoOverride" id="1"`), so a drifting payload fails the run. |
| A missing expected reading anywhere | The runner collects every unmet pattern and exits non-zero with the list. |

## Files added or changed by T01

- `scripts/probe-toast/Program.cs` — the image switches, the file-URI derivation, the payload and
  `[probe] image:` readings, and the delete-during-wait step.
- `scripts/probe-toast/run-image-variants.ps1` — new: the four-variant runner, the per-variant logs,
  the out-of-process history inventory and the database-copy reading.
- `docs/TOAST-S04-IMAGE-VARIANTS.md` — this document.
