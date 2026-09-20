# S01 verification notes

What the **automated** suite proves for slice S01 (core shell interop and icon lifecycle), which
named test is the evidence for each requirement, and — just as importantly — what it does **not**
prove. Nothing here is a claim the suite cannot produce; where a claim is only proven manually it is
listed under "Not covered by automation" and carried by `docs/UAT-S01.md`.

Baseline for this note: the full suite is **113 tests, 0 failures** on `net8.0-windows`, and
`dotnet build Trustsoft.NotifyIcon.sln -c Release` produces the three target-framework assemblies.

## How to run the evidence

From the repository root:

```text
dotnet build Trustsoft.NotifyIcon.sln -c Release
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows
```

Single area:

```text
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows --filter FullyQualifiedName~RealShellApiSignatureProbeTests
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows --filter FullyQualifiedName~TrayIconNegativeTests
dotnet test tests/Trustsoft.NotifyIcon.Tests/Trustsoft.NotifyIcon.Tests.csproj -c Release -f net8.0-windows --filter FullyQualifiedName~SliceContractTests
```

The three-TFM build proof (`PackagePurityTests.Solution_build_outputs_exist_for_all_three_tfms`)
asserts that `src/Trustsoft.NotifyIcon/bin/Release/{net8.0-windows,net9.0-windows,net10.0-windows}`
contain assemblies, so run the solution build before the suite.

(Local harness footnote: when these commands are run through this repository's `gsd_exec` wrapper
rather than an interactive shell, they must be prefixed with
`env 'ProgramFiles(x86)=C:\Program Files (x86)' 'APPDATA=C:\Users\Maxim\AppData\Roaming'`, otherwise
NuGet restore fails in `NuGetEnvironment.CalculateFolderPath` before compilation. That is a property
of the wrapper environment, not of the repository.)

## R001 — A windowless WPF application can create and display a notification-area icon that remains present

Automated evidence that the icon is **registered on the shell with the right protocol, anchored to a
real hidden top-level window, and removed again**:

| Test | What it pins |
| --- | --- |
| `TrayIconLifecycleTests.First_visible_registration_issues_Add_then_SetVersion4` | `NIM_ADD` immediately followed by `NIM_SETVERSION(4)`; the icon is anchored to the host window handle; the callback message and a 16-bit icon id are set |
| `TrayIconLifecycleTests.Add_flags_include_message_icon_tip_and_showtip` | the add flags carry `NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP` |
| `TrayIconLifecycleTests.Add_flags_include_showtip_even_without_an_icon_source` | `NIF_SHOWTIP` is present even when no image is set |
| `TrayIconLifecycleTests.cbSize_on_every_call_is_the_struct_size` | every shell call carries the marshalled struct size (`976` on x64) |
| `TrayIconLifecycleTests.Visible_false_issues_Delete_and_destroys_the_icon` | `NIM_DELETE` removes the registration and releases the handle |
| `TrayIconLifecycleTests.Dispose_is_idempotent_and_removes_the_icon` | disposal removes the icon, is idempotent, and destroys the host window |
| `HostWindowTests.Host_is_a_top_level_window` | `GetAncestor(host, GA_ROOT) == host` — measured, not inferred |
| `HostWindowTests.Host_is_not_visible` | the host is never on screen |
| `HostWindowTests.Host_has_toolwindow_extended_style_and_not_appwindow` | `WS_EX_TOOLWINDOW` set, `WS_EX_APPWINDOW` clear |
| `TrayIconNegativeTests.TrayError_without_a_visual_parent_is_still_raised_and_catchable` | the element has no logical or visual parent and still functions |
| `SliceContractTests.Host_window_handle_is_available_as_a_popup_owner_after_registration` | the host handle is live, top-level, invisible, and is the `hWnd` every shell call carried |

**Not proven automatically:** that an icon is visibly present in the notification area, that it
survives the session, and that no window/taskbar button appears in practice. Those are manual
observations (UAT-S01 checks 1–3).

## R007 — Changing the icon at runtime does not leak GDI or HICON handles and a failed change preserves the previous valid icon

| Test | What it pins |
| --- | --- |
| `HiconFactoryTests.Valid_Bgra32_source_produces_a_nonzero_icon_and_releases_both_bitmaps` | one conversion creates two `HBITMAP`s, produces a non-zero `HICON`, and releases both bitmaps |
| `HiconFactoryTests.Failed_colour_bitmap_creation_releases_nothing` | nothing to release on the first failure; nothing leaks |
| `HiconFactoryTests.Failed_mask_bitmap_creation_still_releases_the_colour_bitmap` | the already-created colour bitmap is still released |
| `HiconFactoryTests.Failed_CreateIconIndirect_reports_the_operation_and_leaks_nothing` | both bitmaps are released when the icon creation fails |
| `HiconFactoryTests.No_GDI_handle_leak_across_50_conversions` | 50 conversions: 100 releases, zero outstanding icons, GDI count flat (`0..2` slack, fake seam allocates no GDI) |
| `HiconFactoryTests.Real_shell_conversion_leaves_the_GDI_count_flat` | 20 **real** conversions through `ShellApi`: real GDI count flat |
| `TrayIconLifecycleTests.IconSource_change_issues_Modify_and_destroys_the_previous_icon` | the previous `HICON` is destroyed only after the shell accepted the replacement |
| `TrayIconLifecycleTests.No_gdi_leak_across_50_icon_replacement_cycles` | 50 real-HICON replacement cycles through `GdiShellApi`: GDI count flat, all created icons destroyed |
| `TrayIconNegativeTests.Failed_icon_replacement_preserves_the_previous_valid_icon` | forced failure: routed error fires with `Retried`, no exception escapes, the surviving handle is the same one and was **not** destroyed, the refused replacement **was** destroyed, exactly one `HICON` remains alive |
| `TrayIconNegativeTests.Setting_IconSource_to_null_leaves_the_registered_icon_untouched_and_destroys_nothing` | no null handle destroyed, no handle destroyed twice, and a real replacement destroys exactly the previous handle |
| `RealShellApiSignatureProbeTests.DestroyIcon_on_a_real_icon_from_our_conversion_path_succeeds` | a real icon built by the delivered path is destroyed by the real `DestroyIcon`, and the real process GDI count returns exactly to its baseline (after one warm-up round trip) |
| `RealShellApiSignatureProbeTests.GetGuiResources_GR_GDIOBJECTS_returns_a_plausible_count_via_the_real_api` | the instrument itself is calibrated: one memory DC → exactly `+1` through both the seam and the independent test-local declaration |
| `PlaceholderTests.GdiHandles_Count_TracksGdiObjectLifetimeExactly(gdiObjectsToCreate: 1|4)` | the GDI counter responds to allocation and release exactly |

**Instrument honesty:** a freshly started Windows process may legitimately own **zero** GDI objects,
so every claim above is an exact delta against a measured baseline (or a seam-level release count),
never a positivity check. `GdiHandles.Count()` throws instead of returning a meaningless zero when
the OS reports a failure.

## R011 — The shipped package has no runtime dependency beyond the BCL and WPF

| Test | What it pins |
| --- | --- |
| `PackagePurityTests.Library_csproj_has_no_package_reference` | the shipping project declares **zero** `PackageReference` elements |
| `PackagePurityTests.Loaded_library_references_only_framework_assemblies` | the **compiled** assembly's referenced-assembly table contains only framework assemblies (no `System.Windows.Forms`, no `System.Drawing`, no third party) |
| `PackagePurityTests.Public_surface_is_only_TrayIcon_and_its_two_error_types` | the exported type list is exactly the documented public surface |
| `PackagePurityTests.Library_targets_three_windows_tfms` | the csproj targets `net8.0-windows;net9.0-windows;net10.0-windows` |
| `PackagePurityTests.Solution_build_outputs_exist_for_all_three_tfms` | a solution build produced all three assemblies (run the solution build first) |

**Not proven automatically:** the dependency group of a *packed* `.nupkg`. Packaging is R010/S07;
until the package exists, the csproj-and-assembly pair is the strongest available evidence.

## R013 — Failures are observable without terminating the application

| Test | What it pins |
| --- | --- |
| `TrayIconLifecycleTests.Failed_Add_throws_TrayIconException_and_registers_nothing` | startup failure throws with operation + Win32 code, registers nothing |
| `TrayIconLifecycleTests.Failed_SetVersion_deletes_the_half_registration_and_throws` | a failed `NIM_SETVERSION` rolls the registration back before throwing |
| `TrayIconLifecycleTests.Failed_Modify_retries_once_then_raises_TrayError_and_traces` | exactly one retry, then a routed event plus a trace line, no throw |
| `TrayIconLifecycleTests.TrayError_is_registered_as_a_bubbling_routed_event` | the routed event registration itself |
| `TrayIconNegativeTests.Visible_is_not_true_when_registration_failed` | a refused add leaves `Visible == false`, not registered, no retained handle |
| `TrayIconNegativeTests.Dispose_after_a_failed_registration_does_not_throw_and_does_not_double_destroy` | the exit path is safe after a failed registration |
| `TrayIconNegativeTests.TrayError_without_a_visual_parent_is_still_raised_and_catchable` | a windowless consumer receives the routed event |
| `TrayIconNegativeTests.ToolTipText_longer_than_the_field_never_produces_an_unterminated_buffer` | the shell never receives an over-long or arbitrarily clipped `szTip` |
| `SliceContractTests.TrayIcon_exposes_a_routed_TrayError_event_with_the_documented_name` | name `TrayError`, `Bubble`, `EventHandler<TrayErrorEventArgs>` |
| `TrayIconExceptionTests.*` (7 tests) | operation constants, message shape, `Win32ErrorCode` round trip, public sealed type |
| `TrayErrorEventArgsTests.*` (6 tests) | operation, code, exception and `Retried` round trip; routed-event plumbing |
| `NotifyIconTraceTests.*` (7 tests) | the `Trustsoft.NotifyIcon` trace source, pinned event id, severity, retry marker, listener-failure tolerance |
| `RealShellApiSignatureProbeTests.ShellNotifyIcon_with_an_invalid_message_code_reports_failure_rather_than_throwing_from_managed_code` | the real export reports failure as `false` + a captured error code, not as a managed exception (D008) |

## R015 — Property changes from a background thread are marshalled; only a dispatcher-less instance throws

| Test | What it pins |
| --- | --- |
| `TrayIconMarshallingTests.Background_thread_Visible_set_registers_the_icon_on_the_dispatcher_thread` | a background-thread assignment reaches the seam **on the dispatcher thread** |
| `TrayIconMarshallingTests.Background_thread_IconSource_and_ToolTipText_changes_reach_the_shell_on_the_dispatcher_thread` | same for the other two properties |
| `TrayIconMarshallingTests.Background_thread_registration_failure_reaches_the_caller_as_TrayIconException` | a marshalled failure surfaces to the calling thread as a named exception |
| `TrayIconMarshallingTests.Instance_without_dispatcher_raises_InvalidOperationException` | the documented carve-out, and it is **not** a `TrayIconException` |
| `TrayIconMarshallingTests.Shut_down_dispatcher_raises_InvalidOperationException` | a shut-down dispatcher is refused with a named message |
| `TrayIconMarshallingTests.Direct_SetValue_on_an_instance_without_dispatcher_is_refused_and_reverted` | a direct `SetValue` bypassing the accessors cannot leave a lying property value |

## Boundary contracts consumed by S02, S03 and S06

| Edge | Test |
| --- | --- |
| S01 → S06 (declarative property surface) | `SliceContractTests.TrayIcon_exposes_the_three_dependency_properties` |
| S01 → S02 (routed-event shape the click events follow) | `SliceContractTests.TrayIcon_exposes_a_routed_TrayError_event_with_the_documented_name` |
| S01 → S02 (raw message stream, unfiltered) | `SliceContractTests.Raw_message_stream_reaches_the_consumer_callback` |
| S01 → S02/S05 (message-id uniqueness) | `SliceContractTests.Callback_message_id_is_distinct_from_TaskbarCreated_and_from_WM_USER` |
| S01 → S03 (popup owner handle) | `SliceContractTests.Host_window_handle_is_available_as_a_popup_owner_after_registration` |

## Real-P/Invoke signature agreement

Everything above except four probes runs against `FakeShellApi`, so a wrong `EntryPoint`, a wrong
`CharSet`, a missing `SetLastError` or a `bool` marshalled as one byte would pass all of them.
`RealShellApiSignatureProbeTests` closes that gap with real, side-effect-free calls:

- `RegisterWindowMessage_TaskbarCreated_returns_a_nonzero_id_via_the_real_api` — the wide entry
  point resolves; the id is stable across calls; the host recognises the id it really received.
- `GetGuiResources_GR_GDIOBJECTS_returns_a_plausible_count_via_the_real_api` — the call that produces
  all of R007's evidence agrees exactly with the independent test-local declaration.
- `DestroyIcon_on_a_real_icon_from_our_conversion_path_succeeds` — real `CreateDIBSection` →
  real `CreateIconIndirect` → real `DestroyIcon`, GDI count flat.
- `ShellNotifyIcon_with_an_invalid_message_code_reports_failure_rather_than_throwing_from_managed_code`
  — the real `Shell_NotifyIconW` export resolves and reports failure as a return value.

## Not covered by automation

These claims are **not** proven by `dotnet test` and must not be reported as if they were:

| Claim | Why it cannot be automated | Where it is carried |
| --- | --- | --- |
| A real icon is visibly present in the notification area and stays there | CI runners have no interactive desktop session (D009); a unit test that puts an icon in the developer's tray and may not clean it up is unacceptable | `docs/UAT-S01.md` checks 1–2 |
| The tooltip text actually renders on hover | requires a live shell and a human pointer | `docs/UAT-S01.md` check 4 |
| No taskbar button and no Alt-Tab entry **in practice** | the suite asserts the host's `WS_EX_TOOLWINDOW`/`WS_EX_APPWINDOW` bits and inactivity, not what the shell does with them | `docs/UAT-S01.md` check 3 |
| Explorer-restart recovery | deliberately outside S01 (S05) | S05's plan and UAT |
| Icon removal when the process exits without `Dispose` | deliberately outside S01 (S05) | S05's plan and UAT |
| DPI/monitor-scale correctness of the icon size | deliberately outside S01 (S03 owns sizing) | S03's plan and UAT |
| Popup-menu placement and click-event delivery | S03 and S02 respectively | their plans and UAT |

## Contract notes discovered while writing these probes

1. **`IconSource = null` means "no change".** The delivered API documents and implements null as a
   no-op (there is no shell operation that means "keep the registration but drop the image"), so
   `Setting_IconSource_to_null_leaves_the_registered_icon_untouched_and_destroys_nothing` pins "no
   call, nothing destroyed, nothing double-destroyed" rather than the earlier plan wording of
   "clearing the source unregisters the bitmap". The plan's actual concern — no null handle
   destroyed, no handle destroyed twice, no wrong handle released — is asserted either way.
2. **A registered icon legitimately keeps exactly one live `HICON`.** After a **failed**
   replacement, `FakeShellApi.OutstandingIcons == 1` is the **correct** state: it is the surviving,
   still-registered handle. The leak claim is "the count does not grow", not "the count is zero";
   zero is reached only after removal or disposal.
3. **A real `NIM_ADD` is deliberately absent from the test suite.** The only live
   `Shell_NotifyIconW` call made by a test uses an invalid operation code against an unregistered
   window/id pair, which cannot place an icon anywhere. A *successful* real registration is the
   manual UAT checklist's evidence.
