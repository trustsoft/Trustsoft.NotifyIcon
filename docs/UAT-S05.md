# UAT-S05 - Continuity and teardown

**Slice:** M001 / S05 (Continuity and teardown)
**Requirements:** R005 (explorer-restart recovery), R006 (no stale icon after a process dies without Dispose)
**Date:** 2026-09-21
**Revision tested:** `milestone/M001` working tree on top of `15694bd` (S04 complete) with the S05 recovery change applied
**Machine:** MINIBOOKX, `Microsoft Windows NT 10.0.26200.0`, single monitor 1920x1200 physical, display scale 150 % (dpi 144), process is per-monitor-v2 DPI aware
**Session:** interactive, single user session (`Console`, session 1)

## Verdict

| Claim | Verdict | Evidence |
|---|---|---|
| R005 - the icon returns by itself after a real Explorer restart, with no application action | **PASS** | Check 1: presence series `present` -> `absent` for 8 s -> `present`, GDI flat at 17 across the whole event |
| R005 - the recovered icon is still fully functional | **PASS** | Check 2 (the shell accepted a balloon from the recovered registration), Check 3 (the menu opened at the recovered icon) |
| R006 - a process that dies without Dispose leaves no stale icon | **PASS** | Check 4 (`taskkill /f`, shell no longer holds the icon), Check 5 (counter-case agrees) |
| The teardown verdict is not stuck on one answer | **PASS** | Check 6 (positive control: a live icon is reported `still present`) and Check 7 (guard: a process that never had an icon reports `NOT OBSERVED`, exit code 1) |

## What this slice does not claim

- **No stale-icon branch was observed for a dead process.** Every run in this session reported `icon-after-exit: gone`. The `still present` text is therefore demonstrated by the positive control in Check 6 (same call, same identity, live process), not by a library-produced stale icon. That is the honest direction of the evidence: the library never produced the failure the check looks for.
- **One machine, one OS build, one display configuration.** Mixed-DPI and multi-monitor behaviour is S03's evidence, not this slice's.
- **The Verbose recovery line was not observed from the sample process.** See finding F1. Recovery is proven by the shell's own answer, not by the library's log line.

---

## The instrument

`scripts/probe-live` is a small console program (its own project, deliberately not in the solution) that observes a notification-area icon from **outside** the application that owns it. It is the instrument every check below uses.

**How it decides whether the icon is there.** It calls `Shell_NotifyIconGetRect` with a `NOTIFYICONIDENTIFIER` of `(hWnd, uID)`, which the shell answers with the icon's screen rectangle if and only if the shell currently holds that icon. That is a documented OS answer rather than a screenshot of the notification area, which on Windows 11 would mean finding an icon inside the overflow flyout.

**How it learns the identity.** It enumerates the sample's top-level windows and scans icon ids 1..32, and the first `Shell_NotifyIconGetRect` that succeeds names the icon the shell really holds:

```
[probe] identity: hwnd=0x3BD06F2 uID=1 title="Trustsoft.NotifyIcon.TrayMessageWindow" (Trustsoft.NotifyIcon.TrayMessageWindow is the expected title)
```

A wrong identity cannot produce a false pass: it produces *no reading at all*, which is reported as `icon=no-reading` and never as absence.

**How a vacuous pass is prevented.** The program exits non-zero when the icon was never observed present, and it refuses to report `icon-after-exit: gone` in that case:

```
[probe] icon-after-exit: NOT OBSERVED (the icon was never observed present, so its current absence proves nothing)
```

**Command line.**

```
dotnet build scripts/probe-live/probe-live.csproj -c Release
dotnet run --project scripts/probe-live -c Release --no-build -- <sampleExe> <observeSeconds> \
    [--kill-after <seconds>] [--keep-sample-alive] [--sample-arg <arg>]...
```

The sample owns its own demonstration switches, so a check is composed by passing them through: `--run-seconds N`, `--show-balloon-after N`, `--open-menu-after N`.

**Shell-restart pitfall (recorded because it silently invalidated a first attempt).** In Git Bash, `taskkill /f /im explorer.exe` is mangled by MSYS path conversion into `taskkill -F:/ -im explorer.exe` and fails with `ERROR: Invalid argument/option - 'F:/'`. Use the doubled form:

```
taskkill //f //im explorer.exe
```

A first recovery attempt used the mangled form, explorer never died, and the presence series stayed `present` throughout - a run that would have looked like a pass for the wrong reason. It is not cited as evidence; the run below is.

---

## Check 1 - recovery across a real Explorer restart

**Command.**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 75 \
    --sample-arg --run-seconds --sample-arg 150 \
    --sample-arg --show-balloon-after --sample-arg 55 \
    --sample-arg --open-menu-after --sample-arg 68
```

**Steps performed while it ran.** At t=16 s: `taskkill //f //im explorer.exe`. At t=20 s (as soon as the taskbar was gone): `cmd //c start "" explorer.exe`. Nothing else touched the sample: no restart, no `Visible` toggle, no click, no input of any kind.

**Raw series around the event** (`[probe] t=...` lines, verbatim; the display scale is 150 %, so these are physical pixels):

```
[probe] t=12s pid=22060 icon=present rect=(1446,1128,1494,1200) gdi=17
[probe] t=13s pid=22060 icon=present rect=(1446,1128,1494,1200) gdi=17
[probe] t=14s pid=22060 icon=present rect=(1446,1128,1494,1200) gdi=17
[probe] t=15s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=16s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=17s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=18s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=19s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=20s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=21s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=22s pid=22060 icon=absent hr=0x80004005 gdi=17
[probe] t=23s pid=22060 icon=present rect=(0,1128,48,1128) gdi=17
[probe] t=24s pid=22060 icon=present rect=(1446,1128,1494,1200) gdi=17
[probe] t=25s pid=22060 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=26s pid=22060 icon=present rect=(1398,1128,1446,1200) gdi=17
```

**What it says.**

1. **The icon disappeared with the shell.** `present` becomes `absent` (`hr=0x80004005`, `E_FAIL`) at t=15 s and stays absent for eight consecutive readings. This is the premise of R005 made visible: the registration lived in the Explorer process, and that process was replaced.
2. **The icon came back by itself.** At t=23 s the shell again holds the icon for the *same* window and the *same* icon id, with no application action between those readings.
3. **Recovery is what brought it back, not a lucky `NIM_MODIFY`.** A modify cannot create a registration the shell no longer holds, and the sample's rotation modifies the icon every second: during the outage those modifies were refused (eight `TrayError operation=Modify` lines, all inside the outage window - the sample's own listener printed them, and the last one precedes the balloon at Check 2). Modifies only start succeeding again after the re-add.
4. **GDI stayed flat at 17 across the entire event.** Nine seconds of absence and the re-add itself produce no step in the count. This is the live form of the ownership rule: recovery re-used the handle the instance already owned instead of converting `IconSource` again.
5. **The icon is interactive afterwards** - Checks 2 and 3 - and it settles one tray slot to the left of where it was, which is the shell's own layout decision for the re-added icon, not a library behaviour.

**Finding F2 (recorded, not a defect).** The first positive reading after recovery is a degenerate rectangle, `(0,1128,48,1128)` - zero height, at the left edge - reported once at t=23 s while the new taskbar was still laying its icons out. The next reading is the real rectangle. It is recorded because a reader comparing series should not mistake it for a mis-placed icon, and because it shows the shell answers before it has finished laying out.

**Finding F1 (observability gap, recorded honestly).** The library writes a Verbose line on a successful recovery, and the sample attaches a listener to the library's trace source at `SourceLevels.Verbose` - yet that line does not appear in this run's output. The reason is the caveat the sample itself documents at its trace setup: a `TraceSource` constructed with an existing source's *name* gets its own listener list, so the sample's instance never receives the library's static instance's events. Consequence: the recovery's log line is **not** observable from this harness, and this document does not claim it was observed. It is proven instead by the two things that cannot be faked - the shell's answers before and after, and the seam tests that pin the recorded call sequence.

---

## Check 2 - the recovered icon still shows balloons (discharges the S04 hand-off)

`docs/UAT-S04.md` ends by handing this to S05: proving that a recovered icon can still show a balloon after a real Explorer restart. The same run triggers one 55 s of self-show, i.e. 32 s after recovery.

```
sample| [sample] --show-balloon-after: showing a balloon now, with no click injected.
sample| [sample] raw callback hwnd=0x3BD06F2 msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
sample| [sample] raw callback hwnd=0x3BD06F2 msg=0x0401 event=0x0404 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010404
```

**What it says.** The request is followed by the shell's own acceptance callback: `event=0x0402` is `NIN_BALLOONSHOW`, delivered for `hwnd=0x3BD06F2` with `iconId=1` - the exact identity the probe independently resolved for the recovered icon. A later `event=0x0404` (`NIN_BALLOONTIMEOUT`) closes the lifecycle. This is the request-versus-acceptance discipline: the app's return value is not the proof, the shell's callback is. The balloon path therefore works on a registration that did not exist when the balloon was configured, which is what "recovery does not corrupt balloon state" means in practice.

---

## Check 3 - the recovered icon still opens its menu at the right place

Same run, 68 s, i.e. 45 s after recovery:

```
sample| [sample] --open-menu-after: requesting the menu now, with no click injected.
sample| [sample] menu opened: popup=0x5025C class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;003bde76-fbad-42f9-aa42-2c0424c0394a] rect=1398,1045 296x83 dpi=144 scale=1.5 owner=0x0 cursor=1919,0 bottomLeftDip=932,752
```

**What it says.** The popup landed at `rect=1398,1045 296x83`, directly above the recovered icon at `(1398,1128)-(1446,1200)` - the icon's left edge and its width match, and the popup sits on top of it. Placement is re-derived per open from the live icon rectangle, so recovering the registration is all that was needed; nothing had to be re-anchored. Also recorded: `owner=0x0`, the same WPF-assigned-owner behaviour S03 documented as finding F5 - unchanged by this slice, as intended. The 150 % scale factor appears once, `dpi=144`.

---

## Check 4 - teardown after a hard kill

**Command.**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 34 \
    --kill-after 20 --sample-arg --run-seconds --sample-arg 300
```

**Raw evidence.**

```
[probe] observe: 34s; kill-after: 20s; sample args: --run-seconds 300
[probe] launched pid=4368
[probe] t=14s pid=4368 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=15s pid=4368 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=16s pid=4368 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=17s pid=4368 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=18s pid=4368 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=19s pid=4368 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] taskkill /f /pid 4368 -> exit 0; SUCCESS: The process with PID 4368 has been terminated.
[probe] t=20s pid=4368 icon=absent hr=0x80004005 gdi=0
[probe] sample-exited at t=21s with exit code 1
[probe] observed-present: yes
[probe] sample-alive-at-end: False; exit-code: 1
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x40236 uID=1)
```

**What it says.** The icon was firmly present for six consecutive readings, then `taskkill /f /pid 4368` terminated the process (exit code 1 - no managed shutdown ran, no `Dispose`, no `ProcessExit` handler, no finalizer), and the shell no longer holds the icon: the same call that answered with a rectangle for `hwnd=0x40236 uID=1` answers `E_FAIL` afterwards. The `gdi=0` reading is the dead process's handle being unreadable, which is itself consistent with the process being gone.

**Why this needs no library code, and why there is none.** The registration is bound to the host window. Process termination destroys that window, so the shell drops the icon with it. A `ProcessExit` handler or a finalizer would be dead code in exactly the case it claimed to cover, because a force-killed process runs no managed code at all. The library therefore ships neither; the guarantee is the OS mechanism plus this live proof.

---

## Check 5 - counter-case: a graceful exit is also clean

**Command.**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 20 \
    --sample-arg --run-seconds --sample-arg 12
```

**Raw evidence.**

```
[probe] observe: 20s; kill-after: no; sample args: --run-seconds 12
[probe] launched pid=23392
[probe] t=10s pid=23392 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=11s pid=23392 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] t=12s pid=23392 icon=present rect=(1398,1128,1446,1200) gdi=17
[probe] sample-exited at t=13s with exit code 0
[probe] observed-present: yes
[probe] sample-alive-at-end: False; exit-code: 0
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x5026A uID=1)
```

**What it says.** The disposed path and the killed path give the same verdict. Both are `gone`, and this is reported rather than dressed up: the purpose of the counter-case is to show the comparison was made, not to manufacture a difference. What the two runs together do establish is that the verdict follows the process ending in both ways.

---

## Check 6 - the verdict column discriminates (positive control)

A check that only ever prints `gone` proves nothing, so the probe has a switch that stops it from killing the sample and lets the after-exit verdict run against a live process:

```
dotnet run --project scripts/probe-live -c Release --no-build -- <sampleExe> 12 --keep-sample-alive --sample-arg --run-seconds --sample-arg 60
```

```
[probe] observed-present: yes
[probe] note: --keep-sample-alive, so the next verdict is the oracle's positive control - a live icon must be reported as still present
[probe] after-exit check pending: still present at t+0.0s (settling)
[probe] after-exit check pending: still present at t+1.0s (settling)
[probe] after-exit check pending: still present at t+2.0s (settling)
[probe] after-exit check pending: still present at t+3.0s (settling)
[probe] after-exit check pending: still present at t+4.0s (settling)
[probe] icon-after-exit: still present rect=(1398,1128,1446,1200) after 5s - the shell still holds the icon for hwnd=0x40236 uID=1
```

**What it says.** The same call, for the same identity, reports `still present` while the process lives and `gone` once it is dead. So `gone` in Checks 4 and 5 is a change in the shell's answer, not a constant.

---

## Check 7 - the guard against a vacuous pass

```
dotnet run --project scripts/probe-live -c Release --no-build -- cmd.exe 5
```

```
[probe] launched pid=19708
[probe] sample-exited at t=1s with exit code 0
[probe] identity: hwnd=0x0 uID=0 title="(none resolved)" (Trustsoft.NotifyIcon.TrayMessageWindow is the expected title)
[probe] observed-present: no
[probe] icon-after-exit: NOT OBSERVED (the icon was never observed present, so its current absence proves nothing)
```

The process exits **1** (measured: `EXIT=1`). A target that never had an icon cannot produce a passing teardown verdict.

---

## Verification summary - what the seam tests pin, and what the suite says

This section exists so a reader can tell proven from assumed without opening the test files.

**The recovery call shape, over the scripted shell seam** (`tests/Trustsoft.NotifyIcon.Tests/TrayIconRecoveryTests.cs`, `[Collection(TraceChannelCollection.Name)]`, broadcast injected with the same-thread `Win32.SendMessage` idiom S02 uses):

| Test | Claim it pins |
|---|---|
| `TaskbarCreated_broadcast_re_adds_with_the_retained_handle` | exactly one `NIM_ADD` then one `NIM_SETVERSION`, same `uID` and host window, `hIcon` identical to the handle observed before the broadcast, no handle destroyed, `IsRegistered` and `Visible` still true |
| `Re_add_carries_the_registration_flags_and_the_tooltip_but_never_a_balloon_flag` | `uFlags == NIF_MESSAGE \| NIF_TIP \| NIF_SHOWTIP \| NIF_ICON`, no `NIF_INFO`, `szTip` truncated to 127, `cbSize` correct on every recorded call |
| `Re_add_of_an_icon_without_an_image_keeps_showtip_and_omits_the_icon_flag` | a no-image icon still recovers with the tooltip flag set and no icon flag |
| `Broadcast_after_the_icon_was_removed_is_a_silent_no_op` | zero shell calls after `Visible = false`, with the host window still alive so the guard is genuinely reached |
| `Broadcast_after_disposal_is_a_no_op` | disposal is terminal; no shell call, no resurrection |
| `Each_broadcast_recovers_and_nothing_is_destroyed` | two broadcasts recover twice, no conversion, no destruction, handle identity unchanged |
| `Failed_re_add_retries_once_then_raises_TrayError_without_throwing` | two `NIM_ADD` attempts, one `TrayError` with `Retried` and operation `Add`, trace line `failed (Win32 error 5); retried=True`, no exception escaping the sink, handle retained |
| `Failed_SetVersion_during_recovery_rolls_back_and_keeps_the_handle` | recorded sequence `ADD, SETVERSION, SETVERSION, DELETE`; `TrayError` names `SetVersion`; handle and flag survive |
| `Successful_recovery_writes_one_verbose_line_naming_the_broadcast` | one `Verbose` line at `NotifyIconTrace.VerboseEventId` naming `TaskbarCreated` |
| `SliceContractTests.Explorer_restart_recovery_re_adds_and_touches_nothing_else` | the S03/S04 boundary: never a `NIM_MODIFY`-only update, never `NIF_INFO`, no icon created or destroyed inside the recovery window, version re-selected |
| `TrayIconMenuActivationTests.An_explorer_restart_while_the_menu_is_open_leaves_the_menu_and_its_anchor_alone` | with a real popup open, the broadcast leaves the menu instance, the anchor handle, the popup window and `IsOpen` exactly as they were |

**Full suite and build.**

| Measurement | Result |
|---|---|
| `dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore` | succeeded, **0 warnings, 0 errors** (all three target frameworks: net8.0-windows, net9.0-windows, net10.0-windows) |
| `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | **384 passed, 0 failed, 0 skipped** |
| Baseline at the start of this slice | 373 passed, 0 failed - so exactly **11 tests were added**, all named in the table above |
| Public surface | unchanged: recovery is a private method, adds no public type and no exception operation constant, and the surface-pin tests in `PackagePurityTests` stay green without being touched - the seven documented public types from D034 are still seven |

---

## Reproducing this

Raw, unfiltered logs from the runs cited above:

| Run | Log |
|---|---|
| Check 1, 2, 3 (Explorer restart) | `/tmp/probe-restart2.log` |
| Check 4, 5 (hard kill, graceful) | `/tmp/t05.log` |
| Check 6 (positive control) | `/tmp/t05-control.log` |
| Check 7 (guard) | `/tmp/probe-negative.log` |

The two commands that make a check reproducible are the probe line above and, for Check 1 only, the shell restart (`taskkill //f //im explorer.exe`, then start `explorer.exe`). Everything else is self-contained in the probe invocation. Restarting Explorer is disruptive - the taskbar restarts, open File Explorer windows close, the overflow flyout resets - so Check 1 is a once-per-session measurement, not a loop.
