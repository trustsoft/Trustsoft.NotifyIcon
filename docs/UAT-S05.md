# UAT-S05 - Continuity and teardown

**Slice:** M001 / S05 (Continuity and teardown)
**Requirements:** R005 (explorer-restart recovery), R006 (no stale icon after a process dies without Dispose)
**Date:** 2026-09-21
**Revision tested:** Check 1-8: `milestone/M001` working tree on top of `15694bd` (S04 complete) with the S05 recovery change applied. Check 9: the committed `milestone/M001` tip (`4cd52db`), re-measured in a fresh session. Check 10: the `milestone/M001` tip (`d091f87`) plus the new second instrument `scripts/tray-inventory.ps1`, measured in a fresh session. Check 11: the T06 revision - the whole demo (window-free host, Explorer restart, balloon, menu, force kill) carried out inside a single session with one sample process, with an independent tray scan of the recovered icon in the middle of it and a second one after the kill; the driver asserts all six legs and both scans and exits 0 only when all fourteen assertions pass
**Machine:** MINIBOOKX, `Microsoft Windows NT 10.0.26200.0`, single monitor 1920x1200 physical, display scale 150 % (dpi 144), process is per-monitor-v2 DPI aware
**Session:** interactive, single user session (`Console`, session 1)

## Verdict

| Claim | Verdict | Evidence |
|---|---|---|
| R005 - the icon returns by itself after a real Explorer restart, with no application action | **PASS** | Check 1: presence series `present` -> `absent` for 8 s -> `present`, GDI flat at 17 across the whole event. Re-measured on the delivered revision by Check 9 (the task's own acceptance command): `present` -> `absent` for 6 readings over 7 s -> `present`, GDI flat at 17 across the event. Measured a third time by Check 11, inside the same session that then shows a balloon, opens the menu and force-kills the process: `absent` for 8 readings over 8 s -> `present` at t=23 s with nothing touching the sample, while the shell's own tray UI answered `PRESENT` to the scan running at t~27 s |
| R005 - the recovered icon is still fully functional | **PASS** | Check 2 (the shell accepted a balloon from the recovered registration), Check 3 (the menu opened at the recovered icon); both re-measured in Check 9 after a second, independent restart, and a third time in Check 11 - the `NIN_BALLOONSHOW` callback and a real popup window at the icon, both after the restart and both on the same process that then died |
| R006 - a process that dies without Dispose leaves no stale icon | **PASS** | Check 4 (`taskkill /f`, shell no longer holds the icon), Check 5 (counter-case agrees), Check 10 (fresh `taskkill /f` on the delivered revision: probe verdict `icon-after-exit: gone` **and** two independent observations of the notification area via the shell's own tray UI agreeing), Check 11 (the same verdict at the end of the single end-to-end session: `taskkill /f` -> `icon=absent` with `gdi=0`, `icon-after-exit: gone`, and the tray UI answering `ABSENT`) |
| The teardown verdict is not stuck on one answer | **PASS** | Check 6 (positive control: a live icon is reported `still present`) and Check 7 (guard: a process that never had an icon reports `NOT OBSERVED`, exit code 1) |
| The teardown verdict does not rest on a single oracle | **PASS** | Check 10: the shell's tray UI, read through UI Automation, exposed `Trustsoft.NotifyIcon sample - the icon changes every second` before the kill and nothing matching it afterwards, while the two oracles agreed on the same 60x60 slot in between; the scanner's own two-way control is in the same check |

## What this slice does not claim

- **No stale-icon branch was observed for a dead process.** Every run in this session reported `icon-after-exit: gone`. The `still present` text is therefore demonstrated by the positive control in Check 6 (same call, same identity, live process), not by a library-produced stale icon. That is the honest direction of the evidence: the library never produced the failure the check looks for.
- **One machine, one OS build, one display configuration.** Mixed-DPI and multi-monitor behaviour is S03's evidence, not this slice's.
- **The independent observation reads the shell's UI tree, not pixels.** `scripts/tray-inventory.ps1` asks UI Automation what the shell's tray windows expose; it does not read the framebuffer. So it proves that the shell no longer *exposes* the icon - which is the shell's decision - but no claim about drawn pixels is made here. It is independent of `Shell_NotifyIconGetRect`, which is the property Check 10 needs.
- **The Verbose recovery line was not observed from the sample process.** See finding F1. Recovery is proven by the shell's own answer, not by the library's log line.

---

## The instrument

`scripts/probe-live` is a small console program (its own project, deliberately not in the solution) that observes a notification-area icon from **outside** the application that owns it. It is the instrument every check below uses.

**How it decides whether the icon is there.** It calls `Shell_NotifyIconGetRect` with a `NOTIFYICONIDENTIFIER` of `(hWnd, uID)`, which the shell answers with the icon's screen rectangle if and only if the shell currently holds that icon. That is a documented OS answer, but it is the shell's *app-facing* API: it is the shell's registration table answering, not the notification area as a user sees it. Checks 1-9 rest on it alone, and a single oracle is a weak basis for a claim about what is displayed in the notification area, so Check 10 adds a second instrument that reads the shell's own tray **user interface** instead.

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
dotnet restore scripts/probe-live/probe-live.csproj
dotnet build scripts/probe-live/probe-live.csproj -c Release --no-restore
dotnet run --project scripts/probe-live -c Release --no-build -- <sampleExe> <observeSeconds> \
    [--kill-after <seconds>] [--click-after <seconds>] [--balloon-after <seconds>] \
    [--menu-after <seconds>] [--keep-sample-alive] [--sample-arg <arg>]...
```

**The `--no-restore` is not optional in this worktree.** A bare `dotnet build` here fails with `NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')` - the repository-hygiene issue `docs/UAT-S04.md` already records (duplicate `NuGet.config`/`nuget.config` pair reachable from the worktree). `dotnet restore` succeeds on its own and the build is clean immediately afterwards, so the check is reproducible but needs the two commands.

**Composing a check.** The sample owns its own demonstration switches (`--run-seconds N`, `--show-balloon-after N`, `--open-menu-after N`), and the probe has native spellings for the post-recovery ones: `--balloon-after <seconds>` forwards as `--show-balloon-after <seconds>` and `--menu-after <seconds>` as `--open-menu-after <seconds>`. Both are validated, so a missing or non-numeric value exits **2** with the usage line rather than degrading into "no demonstration was requested" - a typo must not be able to produce a run that reads like a successful demonstration of nothing. `--click-after <seconds>` injects a real right click at the icon's own rectangle through the shell, and `--sample-arg <arg>` stays available for anything else. The startup line prints the effective triggers and the composed sample arguments, so a capture records what was asked for next to what happened.

**The second instrument, added by Check 10: `scripts/tray-inventory.ps1`.** The probe asks the shell's API; this script asks the shell's rendered UI. It opens the notification area's own *Show Hidden Icons* flyout by invoking that chevron through UI Automation, then enumerates the named elements of every window the shell uses for the tray (`Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `NotifyIconOverflowWindow`, `TopLevelWindowForOverflowXamlIsland`) and prints them with their rectangles. UI Automation reports the name a tray icon's owning application set - its tooltip - so the sample's tooltip text, `Trustsoft.NotifyIcon sample - the icon changes every second`, is the identifier to look for, exactly as the task asks. Opening the flyout is an operator action on the shell, never on the application under observation: nothing is injected into the sample, and the flyout is closed again with Escape. The mode exists because a hidden icon - which is where a sample's icon lives on this machine - is not in the UI tree at all until the flyout is open.

The two instruments share no code path, no API and no notion of identity: one is answered by the shell's registration table for a `(hWnd, uID)` pair, the other by the shell's toolbar tree. They can therefore disagree, and that is what makes their agreement evidence rather than a restatement.

```
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File scripts/tray-inventory.ps1 \
    [-OpenOverflow] [-All] [-Needle <text>] [-KeepOverflowOpen] [-ListTrayWindows]
```

`-Needle` is the substring to search for, `-All` dumps every named element with its rectangle, and the last line is always a `verdict:` line: `PRESENT` with a match count, or `ABSENT` with the number of named elements that were searched - so "found nothing" cannot be confused with "looked at nothing". A failed open prints `overflow: FAILED` and the absence verdict is then read together with it, not instead of it.

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

## Check 8 - the harness re-verified independently (T03 closeout)

The runs above were made while the slice was being built. This check re-did the harness's own acceptance from a clean state, on the delivered revision, so the instrument is known to still work and not merely to have worked once. Three logs, all in `docs/uat-logs/S05/`.

**8a - the task's own acceptance command** (`t03-plan-verify.txt`): the plain 20 s observation run, re-built and re-run. Twenty consecutive `icon=present` readings for the same identity, GDI `13,15,17` and then flat at `17` for the remaining 18 s, `icons-in-notification-area: 1`, **zero** `TrayError` lines, `icon-after-exit: gone`, probe exit **0**, and `0 Warning(s), 0 Error(s)` from the build.

The GDI series deserves one honest word: it ramps `13 -> 15 -> 17` over the first three seconds because the sample materialises its three rotation frames lazily, then holds flat. "Flat" here means flat after the sample's own start-up, which is the window in which any leak from the icon's lifetime would appear. The recovery runs in Check 1 start from the settled value.

**8b - the negative controls** (`t03-negative-controls.txt`):

| Case | Target | Result |
|---|---|---|
| N1 | the sample handed a switch it rejects, so it exits at t=1 s having registered nothing | probe exit **1**, `observed-present: no`, `icon-after-exit: NOT OBSERVED` - never `gone` |
| N2 | `cmd.exe` with `/c` | **discarded**, recorded as such: MSYS path conversion rewrote `/c` to `C:/` before the probe saw it, so the run proved nothing. Same mangling class as the `taskkill /f` trap above |
| N3 | Windows Character Map - alive for the whole run, owning **seven** top-level windows and no icon | probe exit **1**; the scan tried all 32 ids on every window (224 shell calls) and found nothing, so every reading is `icon=no-reading` and **never** `icon=absent`; `observed-present: no`; teardown half printed `NOT OBSERVED` |
| N4 | `--balloon-after` with no value, and `--menu-after soon` | exit **2** with the usage line, in both cases |
| N5 | a target path that does not exist | exit **2**, nothing on stdout, one message plus the usage line on stderr - a usage error rather than a stack trace |

N3 is the one that matters most, because it is the shape in which the instrument could fool a reader: the target is alive and window-rich for the entire observation, so a series of bare "not there" readings could be mistaken for teardown evidence. The probe reports `no-reading` for an identity it could not confirm and reserves `absent` for an identity it *did* confirm, which keeps "I could not find it" and "the shell does not have it" as different facts.

**8c - the probe-triggered actions and the oracle's positive control** (`t03-alias-triggers.txt`): `--balloon-after 6 --menu-after 11` forwards to the sample's switches (the startup line prints the composed `--show-balloon-after 6 --open-menu-after 11`), the balloon request is followed by the shell's own `event=0x0402` (`NIN_BALLOONSHOW`) for `iconId=1`, and the menu opens at `rect=1542,1045 296x83` directly above the icon at `(1542,1128)-(1590,1200)` with `dpi=144` applied once. With `--keep-sample-alive` the after-exit verdict prints `still present` for a live icon, which is the control that makes `gone` in Checks 4, 5 and 8a a change in the shell's answer rather than a constant.

---

## Check 9 - the T04 acceptance run: live recovery on the delivered revision

Checks 1-3 were measured while the slice was being built. This check re-made the recovery measurement **on the delivered revision**, in a fresh session, using the task's own acceptance command line rather than a hand-composed one, so R005 does not rest on a single capture. It is the acceptance run T04 names.

**Command line (verbatim, the task's verify command).**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 90 \
    --balloon-after 45 --menu-after 60
```

The probe built the sample's arguments for it and printed them back, so the capture records what was asked for beside what happened:

```
[probe] probe-live start 2026-09-21 11:58:19; os=Microsoft Windows NT 10.0.26200.0; machine=MINIBOOKX
[probe] sample exe: samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe
[probe] observe: 90s; kill-after: no; click-after: no; balloon-after: 45s; menu-after: 60s; sample args: --show-balloon-after 45 --open-menu-after 60
[probe] launched pid=24448
sample| [sample] menu self-open requested: the assigned menu will open once after 60s and be closed again 6s later (--open-menu-after). No shell click is injected for this.
sample| [sample] balloon self-show requested: a balloon will be shown once after 45s (--show-balloon-after). No shell click is injected for this.
sample| [sample] tray icon registered, rotating 3 frames every 1s.
```

**Run facts.** Run length 90 s of observation, balloon trigger at 45 s, menu trigger at 60 s, and **no** `--run-seconds`, so the sample stayed alive for the whole window (`sample-alive-at-end: True`, one pid `24448` from first reading to last). Windows build `10.0.26200.9457` (`Microsoft Windows NT 10.0.26200.0` in the probe line), host `MINIBOOKX`, session `Console` #1. Display configuration from the sample's own startup block: one monitor, `\.\DISPLAY1`, `rect=0,0 1920x1200`, work area `0,0 1920x1128`, `dpi=144 scale=1.5`, process per-monitor-v2 DPI aware. This is the same machine and display as Checks 1-7, on a **different Explorer instance** (`explorer.exe` PID 5616 killed; PID 9648 running at the end of the run).

**The restart, with wall-clock timestamps** (from `t04-live-recovery.ops.txt`; the probe started at 11:58:19, so wall clock minus that is the probe's `t=`):

```
[t04] 11:58:34 t+16s: taskkill //f //im explorer.exe
SUCCESS: The process "explorer.exe" with PID 5616 has been terminated.
[t04] 11:58:36 explorer after kill (expect none):
INFO: No tasks are running which match the specified criteria.
[t04] 11:58:36 starting explorer.exe
[t04] 11:58:36 explorer process is back (after 1s of polling)
```

Explorer was down for ~2-3 s and was restarted as soon as it was gone, never leaving the desktop shell-less. Nothing else in the run touches the sample: no restart, no `Visible` toggle, no click, no keyboard input, no test harness. The only two commands the operator issued are the two in that block.

**The per-second series around the event** (verbatim `[probe]` lines and the sample's interleaved error lines; display scale 150 %, so these are physical pixels):

```
[probe] t=13s pid=24448 icon=present rect=(1542,1128,1590,1200) gdi=17
[probe] t=14s pid=24448 icon=present rect=(1542,1128,1590,1200) gdi=17
[probe] t=15s pid=24448 icon=absent hr=0x80004005 gdi=17
sample! [sample] TrayError operation=Modify win32Error=-2147467259 retried=True exception=TrayIconException: ...
[probe] t=16s pid=24448 icon=absent hr=0x80004005 gdi=17
sample! [sample] TrayError operation=Modify win32Error=-2147467259 retried=True exception=TrayIconException: ...
[probe] t=17s pid=24448 icon=absent hr=0x80004005 gdi=17
sample! [sample] TrayError operation=Modify win32Error=-2147467259 retried=True exception=TrayIconException: ...
[probe] t=18s pid=24448 icon=absent hr=0x80004005 gdi=17
sample! [sample] TrayError operation=Modify win32Error=-2147467259 retried=True exception=TrayIconException: ...
[probe] t=19s pid=24448 icon=absent hr=0x80004005 gdi=17
sample! [sample] TrayError operation=Modify win32Error=-2147467259 retried=True exception=TrayIconException: ...
[probe] t=21s pid=24448 icon=absent hr=0x80004005 gdi=17
sample! [sample] TrayError operation=Modify win32Error=-2147467259 retried=True exception=TrayIconException: ...
[probe] t=22s pid=24448 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=23s pid=24448 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=24s pid=24448 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=25s pid=24448 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=26s pid=24448 icon=present rect=(1494,1128,1542,1200) gdi=17
```

**Sampling gap, recorded rather than smoothed: there is no `t=20s` line in this capture** (the series has 89 readings for a 90 s window, and `t=19s` is followed directly by `t=21s`). The absence claim does not depend on it - `absent` is reported by all six readings that the capture does contain inside the outage (`t=15s`, `t=16s`, `t=17s`, `t=18s`, `t=19s`, `t=21s`), every one of them a real reading rather than an inferred one, and the gap sits in the middle of the outage rather than at either edge of it. It is written down because a reader diffing this series against Check 1's would otherwise find a missing line and have to guess.

**What the series says.**

1. **The registration died with the shell.** `present` at `t=14s` becomes `absent hr=0x80004005` (`E_FAIL`) at `t=15s`, one second after `taskkill` was issued at wall clock 11:58:34, and stays absent for every subsequent reading until the comeback (six readings, over 7 s).
2. **The icon came back by itself.** At `t=22s` the shell answers again for the *same* window and the *same* icon id, with the process pid unchanged (`24448`) and no application action in between. Explorer's process was back at 11:58:36 (`t+17s`); the reading at `t=22s` is the shell having built its notification area and broadcast `TaskbarCreated` to the surviving top-level host window.
3. **The one action during the outage was a Modify, and it was refused.** All six sample error lines in this window are `operation=Modify` with `retried=True`. A modify cannot create a registration the shell does not hold, and these six were *refused* - so no successful modify can be the cause of the `t=22s` reading. The re-add between `t=21s` and `t=22s` is what put the icon back, and the broadcast is the only thing that triggers it.
4. **GDI was flat at 17 across the entire event** - `gdi=17` on every reading from `t=3s` through `t=60s`, spanning the six absent readings, the re-add, and 38 s of post-recovery rotation. No handle was built to recover, which is the live form of the retained-`HICON` rule (R007).
5. **Nothing was double-registered:** the probe's final sweep reports `icons-in-notification-area: 1` for the sample's window, so recovery replaced one registration rather than stacking a second. As in Check 1, the recovered icon settled one tray slot to the left (`1542` -> `1494`), which is the shell's own layout decision.

**The GDI ramp and the later step, stated plainly.** The count ramps `13 -> 15 -> 17` over the first three seconds because the sample materialises its three rotation frames lazily, and it steps `17 -> 29` at `t=61s` when the WPF popup opens at the menu trigger, then holds at 29. The flat claim is about the recovery window (`t=15s`..`t=60s`); the `t=61s` step belongs to the menu, not to recovery, and the menu step is the same behaviour `docs/UAT-S03.md` documents for a WPF popup.

### 9a - the recovered icon still shows balloons (discharges the S04 hand-off)

The balloon was triggered 23 s after recovery, with no click injected:

```
[probe] t=45s pid=24448 icon=present rect=(1494,1128,1542,1200) gdi=17
sample| [sample] --show-balloon-after: showing a balloon now, with no click injected.
sample| [sample] raw callback hwnd=0x2B505AA msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
```

`event=0x0402` is `NIN_BALLOONSHOW`, delivered by the shell for `hwnd=0x2B505AA` `iconId=1` - the identity the probe resolved independently for the **recovered** registration (the callback `hwnd` equals the `hwnd` in the probe's identity line). The request is therefore followed by the shell's own acceptance, not merely by a return value. Two lifecycle completions were delivered at the timeout:

```
sample| [sample] raw callback hwnd=0x2B505AA msg=0x0401 event=0x0404 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010404
sample| [sample] raw callback hwnd=0x2B505AA msg=0x0401 event=0x0404 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010404
```

`0x0404` is `NIN_BALLOONTIMEOUT`; **two** were delivered in this run where Check 1's run had one. That is recorded as observed rather than explained away: the acceptance claim rests on the `0x0402`, and the duplicate `0x0404` is a shell-side callback count this run makes no claim about.

**The S04 hand-off, discharged.** `docs/UAT-S04.md` check 7 ends: *"proving that a recovered icon still shows balloons after a real explorer restart is S05's live check"* (and its table records the matching verdict as passing only as a seam contract). Check 1 and Check 9a are that live check, made twice on two different Explorer instances: a balloon shown after recovery reaches the shell and is acknowledged by the shell's own `NIN_BALLOONSHOW` for the recovered identity. The hand-off is **closed**, not deferred.

### 9b - the recovered icon still opens its menu at the icon

The menu was triggered 38 s after recovery:

```
[probe] t=60s pid=24448 icon=present rect=(1494,1128,1542,1200) gdi=17
sample| [sample] --open-menu-after: requesting the menu now, with no click injected.
sample| [sample] menu opened: popup=0x506BE class=HwndWrapper[Trustsoft.NotifyIcon.Sample;;ca676e1c-bb02-4ed2-9055-56c5d095e86a] rect=1494,1045 296x83 dpi=144 scale=1.5 owner=0x0 cursor=0,0 bottomLeftDip=996,752
```

Placement is at the icon, not at the origin: the popup's left edge `1494` equals the recovered icon's left edge `1494`, its bottom edge `1045 + 83 = 1128` equals the icon's top edge `1128`, and it sits above the icon - a popup anchored at `(0,0)` or at a stale pre-restart rectangle would show the pre-restart left edge `1542`, not `1494`. `cursor=0,0` says no pointer was at the icon, so the placement was re-derived from the live icon rectangle. `dpi=144` appears once, i.e. the 150 % scale applied once. The menu was then closed by the sample at `t=66s` (`menu dismissed.`) and the icon stayed present.

### 9c - the TrayError accounting for this run, stated exactly

The task's step 3 asks that "the sample must print no `TrayError` line". **That literal condition was not met, and this document does not claim it was.** The capture contains exactly **six** `TrayError` lines, and all six are:

- `operation=Modify` (never `Add`, never `SetVersion`),
- `win32Error=-2147467259` (`E_FAIL`, the shell refusing), with `retried=True`,
- interleaved with the readings `t=15s`..`t=21s`, i.e. entirely inside the window in which the shell held no registration for this window.

What is true and checkable is the stronger, more useful statement: **the recovery path itself raised no error of any kind.** Zero `TrayError` lines carry `operation=Add` or `operation=SetVersion`, and no error line appears after `t=21s`. The six `Modify` refusals are the sample's own 1 Hz icon rotation hitting a shell that had no registration to modify - the documented expected consequence of rotation during an outage, and independently useful here because they are what proves the `t=22s` comeback was a re-add rather than a lucky modify.

---

## Check 10 - the T05 acceptance run: a real hard kill, seen by the shell's API and by the shell's own tray UI

Checks 4 and 5 were made while the slice was being built, with one oracle. R006 is the one requirement in this slice that is deliberately verification-only, and a single oracle is a weak basis for a claim about what the notification area shows, so this check re-makes the claim on the delivered revision, in a fresh session, with a **second oracle that shares no code path with the first**. It is the acceptance run T05 names.

### 10a - the command, the pid, and the kill

**Command line (verbatim, the task's verify command).**

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 40 \
    --kill-after 20
```

The kill is the probe's own line, `taskkill /f /pid 22592 -> exit 0; SUCCESS: The process with PID 22592 has been terminated.`, and `22592` is **the pid the probe printed for the sample it launched** (`[probe] launched pid=22592`) - not a pid looked up by image name, so the kill cannot land on a different process. Operator log with wall-clock timestamps: `docs/uat-logs/S05/t05-live-teardown.ops.txt`; probe capture: `docs/uat-logs/S05/t05-hard-kill.txt`.

### 10b - the presence series before the kill, and the after-exit verdict

The full capture holds **19 consecutive `icon=present` readings** for `pid=22592`, `t=1s` through `t=19s` (GDI `13 -> 15 -> 17` over the first three seconds, then flat). The tail around the kill, verbatim:

```
[probe] t=17s pid=22592 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=18s pid=22592 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=19s pid=22592 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] taskkill /f /pid 22592 -> exit 0; SUCCESS: The process with PID 22592 has been terminated.
[probe] t=20s pid=22592 icon=absent hr=0x80004005 gdi=0
[probe] sample-exited at t=21s with exit code 1
[probe] identity: hwnd=0x2790652 uID=1 title="(no title)" (Trustsoft.NotifyIcon.TrayMessageWindow is the expected title)
[probe] icons-in-notification-area: 1 (every window x icon-id pair the shell located; a resource whose deferral failed would show up here as a second count)
[probe] observed-present: yes
[probe] sample-alive-at-end: False; exit-code: 1
[probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x2790652 uID=1)
probe-exit=0
```

**`icon-after-exit: gone` on the delivered revision, for a force-killed process.** Exit code **1** is the sample's own death-by-signal code: no `Dispose`, no `ProcessExit` handler, no finalizer, no managed code of any kind ran after `t=19s`. The same `Shell_NotifyIconGetRect` call that answered with a rectangle for `hwnd=0x2790652 uID=1` throughout the run now answers `E_FAIL` (`0x80004005`). The `gdi=0` reading is the dead process's handle being unreadable, which is itself consistent with the process being gone.

**`icons-in-notification-area: 1` is not a post-exit presence count, and must not be read as one.** The probe computes it inside `TryResolveIdentity` at the moment it resolves the identity - i.e. while the icon was alive - and prints it at the end as a deferral check: one `(window, icon-id)` pair was located for this process, so a second registration never stacked. The authoritative after-exit answer is the `icon-after-exit:` line, which is a fresh call made after the process exited. Both appear in the same capture, and they are answers to different questions.

### 10c - the independent notification-area observation

`scripts/tray-inventory.ps1` (documented under *The instrument*) opens the notification area's own overflow flyout through UI Automation and reports what the shell's tray UI exposes. It was run three times around the kill, in the same session, using the sample's tooltip text as the needle. Operator log timestamps are wall clock; the probe started at `12:06:08`, so probe `t=` is the same second minus 8.

```
[t05 12:06:14] independent tray inventory BEFORE kill -> verdict: PRESENT - 1 element(s) in the notification area match 'Trustsoft.NotifyIcon'
[t05 12:06:29] [probe] taskkill /f /pid 22592 -> exit 0; SUCCESS: The process with PID 22592 has been terminated.
[t05 12:06:32] independent tray inventory IMMEDIATELY after kill -> verdict: ABSENT - the notification area exposes no element matching 'Trustsoft.NotifyIcon'
[t05 12:06:42] independent tray inventory SETTLED after kill -> verdict: ABSENT - the notification area exposes no element matching 'Trustsoft.NotifyIcon'
```

What was actually seen, from `docs/uat-logs/S05/t05-tray-before-kill.txt` (`12:06:14`, the flyout open, the icon alive):

```
host TopLevelWindowForOverflowXamlIsland : present hwnd=0x1C80552 name='System tray overflow window.'
  Button 'Trustsoft.NotifyIcon sample - the icon changes every second' rect=1368,1042 60x60
  named-elements: 4
named-elements-total: 25
verdict: PRESENT - 1 element(s) in the notification area match 'Trustsoft.NotifyIcon'
```

and from `t05-tray-after-kill-immediate.txt` (`12:06:32`, three seconds after the kill):

```
host TopLevelWindowForOverflowXamlIsland : present hwnd=0x1C80552 name='System tray overflow window.'
  named-elements: 3
named-elements-total: 24
verdict: ABSENT - the notification area exposes no element matching 'Trustsoft.NotifyIcon'
```

The settled scan at `12:06:42` repeats it exactly: same host, overflow `named-elements: 3`, total **24**, `verdict: ABSENT`. The inventory is quoted with its element counts on purpose, so that "the scanner found nothing" is distinguishable from "the scanner looked at nothing": the flyout really opened (the line above it says so), the taskbar still exposed its 21 named elements, and the overflow still held three other icons - one fewer than before, and the missing one is the sample's.

**No ghost icon was observed, at any point after the kill.** The immediate scan is the interesting one, because that is the window in which the shell might have kept drawing a stale slot: it did not. The absence is a change in the shell's answer rather than a constant, because the same scan with the same needle found the icon three seconds earlier and never reported `ABSENT` while the process was alive.

### 10d - the two oracles agreeing on the same slot

There is a place where the two instruments can be compared directly, and it was captured by accident: while the flyout was open (probe `t=6s`, wall clock `12:06:14`), `Shell_NotifyIconGetRect` returned the icon's rectangle **inside the open flyout** rather than at its usual taskbar slot:

```
[probe] t=5s pid=22592 icon=present rect=(1494,1128,1542,1200) gdi=17
[probe] t=6s pid=22592 icon=present rect=(1369,1053,1429,1113) gdi=17
[probe] t=7s pid=22592 icon=present rect=(1369,1043,1429,1103) gdi=17
[probe] t=8s pid=22592 icon=present rect=(1494,1128,1542,1200) gdi=17
```

`(1369,1043)-(1429,1103)` is 60x60, and UI Automation puts the sample's button at `rect=1368,1042 60x60` - the same slot, one pixel apart in the two rounded coordinate systems. Two independent mechanisms therefore agree not only on *whether* the icon is there but on *where* it is drawn, which is a stronger check than either one alone could make.

The rectangles also explain themselves, and the reason is worth writing down because a reader will notice it: with the flyout **closed**, `Shell_NotifyIconGetRect` answers `(1494,1128,1542,1200)` - 48x72, the taskbar slot that UI Automation independently reports for the `Show Hidden Icons` chevron - i.e. the shell names the overflow indicator's slot for an icon it is hiding. With the flyout **open**, it answers `(1369,1043,1429,1103)` - 60x60, the flyout's own slot, which is where UI Automation finds the sample's button. The two sizes are the two containers, not two icons.

### 10e - the counter-case in the same session, and what it does and does not show

A second sample instance was then run with `--run-seconds 12` so that it exits through its own normal shutdown path, and observed the same way:

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 20 \
    --sample-arg --run-seconds --sample-arg 12
```

```
[t05 12:06:44] counter-case: probe launched sample pid=17088
[t05 12:06:49] counter-case: independent tray inventory BEFORE graceful exit -> verdict: PRESENT - 1 element(s) in the notification area match 'Trustsoft.NotifyIcon'
[t05 12:07:00] counter-case: independent tray inventory AFTER graceful exit -> verdict: ABSENT - the notification area exposes no element matching 'Trustsoft.NotifyIcon'
[t05 12:07:00] counter-case: probe-exit=0 | [probe] icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x3FC066E uID=1)
```

The sample's own account of the same moment, from `docs/uat-logs/S05/t05-graceful-exit.txt`: `[sample] tray icon disposed - it must have left the notification area.`, then `[probe] sample-exited at t=13s with exit code 0`, and `icon-after-exit: gone` for `hwnd=0x3FC066E uID=1`.

**Both after-exit verdicts are `gone`, and that is stated plainly rather than dressed up.** The killed path and the disposed path give the same answer, by both oracles. The purpose of the counter-case is to show that the comparison was made and that the oracle can distinguish the two states it is being asked about - it says nothing about a difference between them, because there is none to find. What the four runs together establish is that the verdict follows the process ending in both ways, and that the icon is genuinely present in the notification area up to the moment it ends.

### 10f - why no library code is involved, and the evidence that none ships

**The mechanism, as the OS guarantee plus the live proof above.** A `NOTIFYICONDATA` registration is owned by the window named in `hWnd`. Process termination destroys that window, so the shell drops the icon with it: the disappearance is a consequence of the window's death, not of anything the library does at teardown time. The live proof is this check, made twice on the delivered revision - once against a force-killed process (10b) and once against a disposed one (10e) - with the shell's API and the shell's tray UI answering independently and agreeing.

**Consequently there is no fallback, on purpose, and this is checkable rather than asserted** (`docs/uat-logs/S05/t05-library-no-fallback.txt`):

```
$ grep -rnE 'ProcessExit|~TrayIcon|Finalize|GC.SuppressFinalize' src/Trustsoft.NotifyIcon --include='*.cs' --exclude-dir=obj --exclude-dir=bin
src/Trustsoft.NotifyIcon/TrayIcon.cs:101:/// the icon with it (R006). That is why there is deliberately no <see cref="System.AppDomain.ProcessExit"/>
--- matches excluding /// doc-comment lines, i.e. anything executable ---
(none: no executable reference in the library)
```

The only mention is the remark that documents the decision: `TrayIcon.cs` states that no `ProcessExit` handler and no finalizer exist because neither can run in the case they would have to cover. A `ProcessExit` handler or finalizer would be dead code in exactly the failure it claimed to handle, since a force-killed process runs no managed code at all.

The seam half of the same contract is a test, not a comment: `SliceContractTests.Teardown_declares_no_finalizer_anywhere_in_the_library` reflects over **every type in the library assembly** and asserts that none declares a `Finalize` method, with the reasoning for asserting over the assembly rather than over `TrayIcon` alone written into its remarks.

### 10g - the second instrument's own two-way control

A scanner that always answered `ABSENT` would produce this check's headline result for free, so the scanner was made to answer both ways in one run, against one live icon (`docs/uat-logs/S05/t05-scanner-discrimination.txt`, 12:07:35):

| Run | Needle | Named elements searched | Verdict |
|---|---|---|---|
| A | `no such tray tooltip exists` | 25, with the sample's real tooltip visible in the dump | **ABSENT** |
| B | `Trustsoft.NotifyIcon` | 25, same flyout, same icon | **PRESENT** - 1 match |

Run A shows the verdict is not "everything matches"; run B, three seconds later on the same live icon, shows it is not "nothing ever matches". Together they show that `ABSENT` in 10c is a real negative and not a stuck scanner. This is the counter-case the task asks for, applied to the instrument itself.

---

## Check 11 - the whole demo in one session (the slice's exit condition)

Checks 1-10 measured the pieces separately: 1-3 and 9 the restart with the post-recovery balloon and menu, 10 the teardown. This check is the exit condition the roadmap states for S05 - **one** sample process, in **one** session, that hosts the icon with no window, loses it when Explorer is replaced, gets it back without the application doing anything, still takes a balloon and opens a menu on the recovered registration, and then leaves nothing behind when it is force-killed. The run is `docs/uat-logs/S05/t06-e2e-demo.txt`, the operator log is `t06-e2e-demo.ops.txt`. The last run recorded there is the one from 13:03:09; earlier runs of the same driver were discarded (see "Two discarded attempts of this same check" below) and their logs were overwritten by this one.

**The driver asserts its own verdict rather than inheriting one.** `t06-e2e-demo.ops.txt` ends with fourteen `ASSERT <name>: PASS` lines and a `T06-DEMO-VERDICT:` line, and the command exits 0 only when every assertion passed - so the exit code is a statement about the six legs, not the exit code of whatever ran last. The assertions are greps over the captured probe log and the two tray scans: the window-free line, `observed-present: yes`, an `icon=absent` reading, a `present` reading at t=20..29, both scan verdicts, the `NIN_BALLOONSHOW` callback, the menu opening and dismissing, the `taskkill` line, `icon-after-exit: gone`, and `icons-in-notification-area: 1`.

**Command** (the plan recorded in the operator log: kill Explorer at t=16, start it again at t=20, scan the tray at t~26, balloon at t=40, menu at t=50, kill the sample at t=70):

```
dotnet run --project scripts/probe-live -c Release --no-build -- \
    samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe 100 \
    --balloon-after 40 --menu-after 50 --kill-after 70 \
    --sample-arg --run-seconds --sample-arg 200
```

**The six legs, in order, out of one process (pid 3960).**

| # | Leg | Evidence |
|---|---|---|
| 1 | a window-free application hosts the icon | `sample\| [sample] no window is shown - check the notification area, not the taskbar.` and `sample\| [sample] tray icon registered, rotating 3 frames every 1s.`, with `[probe] t=1s pid=3960 icon=present rect=(1446,1128,1494,1200) gdi=13` |
| 2 | the icon goes when Explorer is replaced | operator log `taskkill //f //im explorer.exe` at 13:03:25 (the shell reports the terminated pid 17976, exit 0); `icon=absent hr=0x80004005` for t=15..t=22, with nothing touching the sample |
| 3 | it comes back by itself | `[probe] t=23s pid=3960 icon=present rect=(1446,1128,1494,1200)` - no click, no `Visible` toggle, no process action of any kind; the only producer-side output in the window is the sample's own `Modify` failure line while the shell was down |
| 4 | the recovered icon is the shell's icon, not just an answer to an API question | tray inventory at 13:03:36, mid-recovery: `verdict: PRESENT - 1 element(s) in the notification area match 'Trustsoft.NotifyIcon'`. The probe's own reading at t=27 names `rect=(1321,1043,1381,1103)` - the flyout's rectangle, while that scan had the flyout open |
| 5 | a balloon and a menu still work on it | `sample\| [sample] --show-balloon-after: showing a balloon now, with no click injected.` -> `sample\| [sample] raw callback hwnd=0x90578 msg=0x0401 event=0x0402` (the shell's `NIN_BALLOONSHOW`, at t=40); `sample\| [sample] menu opened: popup=0x72023A ... rect=1446,1045 296x83 dpi=144`, with GDI rising 17 -> 29 as the popup's surface appears, then `sample\| [sample] menu dismissed.` at t=56 |
| 6 | a force kill leaves nothing behind | `[probe] taskkill /f /pid 3960 -> exit 0` at t=70; `[probe] t=71s pid=3960 icon=absent hr=0x80004005 gdi=0`; `icon-after-exit: gone (Shell_NotifyIconGetRect hr=0x80004005 for hwnd=0x90578 uID=1)`; and after the kill the tray inventory answers `verdict: ABSENT - the notification area exposes no element matching 'Trustsoft.NotifyIcon'` at 13:04:26. The probe exits **0**, which it only does when the icon was observed present |

**What this check proves, and what it does not.**

- It proves the four claims in one continuity: the restart cost the consumer neither its balloon nor its menu, and the recovered registration died with the process that owned it.
- The balloon and the menu in leg 5 are the sample's own switches (`--show-balloon-after`, `--open-menu-after`), exactly as in Checks 9 and 10e, and the sample says so: "No shell click is injected for this." What they prove is that the **recovered registration** still accepts a balloon and can still be opened at the icon. The click-driven path is Check 10's (`--click-after`, a real right click on the icon's own rectangle) and the seam half is `TrayIconMenuActivationTests`.
- Leg 2 is the one leg that has to be live: the absence is the shell's own answer (`E_FAIL`) while Explorer restarts, not an inference from a log line the application produced.
- One reading is unintelligible taken alone and is called out rather than left to puzzle the reader: the final `identity:` line prints `title="(no title)"` because the probe reads the host window's title after the sample has died. The handle in that line (`0x90578`) is the one resolved while the process was alive, and it is the same handle the presence series and the after-exit verdict use - so the identity is intact and only the title text is missing.
- This check does not repeat the teardown counter-case, the positive control or the scanner's two-way discrimination (Checks 6, 7 and 10g); it cites them instead.

### Two discarded attempts of this same check, and one fix to the instrument that survives them

Recorded because both attempts failed for reasons that have nothing to do with the library, and because the second one changed a committed script.

- **A driver assertion typo.** One run of the driver exited non-zero with thirteen of its fourteen assertions passing: the assertion for leg 1 matched `^\[sample\] no window is shown` against a log in which the sample's own lines are relayed with the `sample| ` prefix, so a correct run was reported as a failed leg. The assertion was fixed to match the relayed form, and the run cited above is the one made with the fixed driver.
- **A crash in the scanner, now fixed.** On a second run, the after-kill scan printed no `verdict:` line at all: `scripts/tray-inventory.ps1` died at its formatting line with `Cannot convert value "∞" to type "System.Int32"`, because a UI Automation node - the overflow host's own `PopupHost` pane - reported an infinite extent and the script cast it to `int`. That is the one failure mode the script exists to make impossible: a scan that prints no verdict cannot be told from a scan that looked at nothing. The script now renders a non-finite or out-of-range extent as its raw value instead of casting it, and refuses to click a non-finite chevron rectangle rather than throwing on the way to its verdict line. The fix is in `scripts/tray-inventory.ps1`; it parses clean and reaches a `verdict: ABSENT` line with no icon present, and the run cited above is the first end-to-end demo made with it. Nothing in `src/` or `tests/` changed for it.

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

**Full suite and build** (the closing measurement for the slice, all three target frameworks, recorded in `docs/uat-logs/S05/t06-suite.txt` for the earlier T06 measurements and in `docs/uat-logs/S05/t06-acceptance-rerun.txt` for the T06 acceptance command itself).

| Measurement | Result |
|---|---|
| `dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore` | succeeded, **0 warnings, 0 errors** - net8.0-windows, net9.0-windows and net10.0-windows |
| `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | **394 passed, 0 failed, 0 skipped**, 51 s |
| The task's own acceptance command, both halves in one invocation, made alone with nothing else running (`docs/uat-logs/S05/t06-acceptance-rerun.txt`) | build **exit 0**, then test **exit 0**: **394 passed, 0 failed, 0 skipped**, 51 s |
| The same command issued while other commands were in flight (`docs/uat-logs/S05/t06-acceptance.txt`) | build **exit 0**, test **exit 1**: **392 passed, 2 failed** - the S03 F5 popup class, see below |
| Baseline at the start of this slice (`docs/UAT-S04.md`, S04 complete) | **373 passed, 0 failed, 0 skipped** |
| Delta | **+21 tests**, and every one of them is named below |
| Public surface | unchanged: recovery is a private branch, adds no public type and no exception operation constant. `tests/Trustsoft.NotifyIcon.Tests/PackagePurityTests.cs` is **not in the diff between the baseline commit and this revision**, so `Public_surface_is_only_the_documented_types` and `TrayIcon_exposes_the_context_menu_property_without_adding_a_public_type` were neither edited nor relaxed; the seven documented public types from D034 are still seven |

**The 21 tests added since the baseline, by name.** Measured by diffing the test method names of `tests/` between the baseline commit `15694bd` and this revision, and corroborated by per-class counts from `dotnet test --list-tests`, which sum to the same 394. The name diff yields 23 names; `OnTrayLeftClick` and `OnPreviewTrayLeftClick` are markup event handlers in the XAML test file, not tests, and are excluded.

| # | Test | File | Owner |
|---|---|---|---|
| 1 | `TaskbarCreated_broadcast_re_adds_with_the_retained_handle` | `TrayIconRecoveryTests.cs` | S05 |
| 2 | `Re_add_carries_the_registration_flags_and_the_tooltip_but_never_a_balloon_flag` | `TrayIconRecoveryTests.cs` | S05 |
| 3 | `An_instance_that_never_showed_an_icon_has_no_host_to_recover_into` | `TrayIconRecoveryTests.cs` | S05 |
| 4 | `Re_add_of_an_icon_without_an_image_keeps_showtip_and_omits_the_icon_flag` | `TrayIconRecoveryTests.cs` | S05 |
| 5 | `Broadcast_after_the_icon_was_removed_is_a_silent_no_op` | `TrayIconRecoveryTests.cs` | S05 |
| 6 | `Broadcast_after_disposal_is_a_no_op` | `TrayIconRecoveryTests.cs` | S05 |
| 7 | `Each_broadcast_recovers_and_nothing_is_destroyed` | `TrayIconRecoveryTests.cs` | S05 |
| 8 | `Failed_re_add_retries_once_then_raises_TrayError_without_throwing` | `TrayIconRecoveryTests.cs` | S05 |
| 9 | `Failed_SetVersion_during_recovery_rolls_back_and_keeps_the_handle` | `TrayIconRecoveryTests.cs` | S05 |
| 10 | `ShowBalloonTip_still_reaches_the_shell_after_recovery` | `TrayIconRecoveryTests.cs` | S05 |
| 11 | `Successful_recovery_writes_one_verbose_line_naming_the_broadcast` | `TrayIconRecoveryTests.cs` | S05 |
| 12 | `Explorer_restart_recovery_re_adds_and_touches_nothing_else` | `SliceContractTests.cs` | S05 |
| 13 | `Teardown_declares_no_finalizer_anywhere_in_the_library` | `SliceContractTests.cs` | S05 |
| 14 | `An_explorer_restart_while_the_menu_is_open_leaves_the_menu_and_its_anchor_alone` | `TrayIconMenuActivationTests.cs` | S05 |
| 15 | `A_parsed_parentless_instance_disposes_cleanly` | `TrayIconXamlContractTests.cs` | S06 |
| 16 | `Declarative_property_markup_creates_a_configured_instance_without_registering` | `TrayIconXamlContractTests.cs` | S06 |
| 17 | `Markup_event_attributes_bind_and_fire_for_bubble_and_preview` | `TrayIconXamlContractTests.cs` | S06 |
| 18 | `Markup_event_attributes_in_a_resource_dictionary_are_a_pinned_boundary` | `TrayIconXamlContractTests.cs` | S06 |
| 19 | `Markup_resolves_the_library_owned_context_menu_property_and_the_menu_by_identity` | `TrayIconXamlContractTests.cs` | S06 |
| 20 | `One_resource_key_yields_one_instance` | `TrayIconXamlContractTests.cs` | S06 |
| 21 | `The_consumer_namespace_is_declared_and_markup_resolves_through_it` | `TrayIconXamlContractTests.cs` | S06 |

Arithmetic: 373 + 11 (`TrayIconRecoveryTests`) + 2 (`SliceContractTests`) + 1 (`TrayIconMenuActivationTests`) + 7 (`TrayIconXamlContractTests`) = **394**. Per-class counts agree: 11 / 8 / 16 / 7 at this revision against 0 / 6 / 15 / 0 at the baseline.

**Two honest notes about that table.** *(a) It is 21 added tests, not 21 of this slice's tests.* Rows 15-21 live in S06's file, which is already on the shared `milestone/M001` branch - one worktree per milestone, so a later slice's file can be present while an earlier slice is still finishing. They are part of the delta against the baseline and are therefore named here rather than absorbed into an S05 total; S05's own contribution is the 14 in rows 1-14. *(b) An earlier revision of this table carried an interim `384` reading and attributed "the ten added in between" to T03 and T04.* That attribution does not survive the by-name accounting above and is replaced by it: the interim number measured an intermediate working tree, while this table is measured against the baseline commit and against names.

**Two flaky measurements, recorded rather than smoothed over.** The first full-suite run in this task reported **3 failed / 391 passed / 394 total**, all three in `TrayIconMenuActivationTests` (`An_unregistered_icon_falls_back_to_the_cursor_and_records_that_the_shell_was_not_asked`, `Replacing_the_menu_between_two_clicks_opens_the_new_one`, `A_second_right_click_while_the_menu_is_open_opens_nothing_new`), each failing with `popupWindows=[]`, `menuIsOpen=False` and `foreground=0xBF056E(Notepad3)`: the popup never opened while a third-party window held the foreground. Re-run immediately, same command and same binaries (`--no-build`), it is **394 passed, 0 failed**; the same three tests run alone are **16 passed, 0 failed**. A second full-suite run, made while other commands were in flight, reported **2 failed / 392 passed**: `A_monitor_whose_work_area_and_dpi_cannot_be_read_still_places_the_menu_inside_the_icon_rectangle` (Activation) and `The_menu_state_is_per_instance_and_no_static_field_can_hold_a_menu_or_an_anchor` (Contract), both again with `popupWindows=[]` and `menuIsOpen=False`, this time with `foreground=0x401D2(Shell_TrayWnd)`. The same command re-issued alone, nothing else running, is **394 passed, 0 failed** - and that is the run the acceptance row above records. None of the five failures is an S05 test and no library file differs between any of these runs, so this is the full-suite popup interference `docs/UAT-S03.md` already records as finding F5, not a regression from this slice. The flaky runs, the re-runs and the isolation run are all in `docs/uat-logs/S05/t06-suite.txt` and `docs/uat-logs/S05/t06-acceptance.txt`. **Operationally:** the failures correlate with other processes being spawned while the suite runs - the mechanism is the menu tests' dependence on taking the foreground window (see `Win32TestInput.GrantLastInputToThisProcess`), and a machine that is busy at that moment is a machine where the synthetic input nudge can lose the race. Run this suite alone.

---

## What this slice deliberately leaves open

Recorded so the next slice inherits them as open items rather than rediscovering them.

| Item | What stays true after S05 | Where it is pinned |
|---|---|---|
| A **failed** recovery leaves the icon absent | The library retries once, traces and raises `TrayError`, and then stops. There is no timer, no backoff and no second attempt: the icon stays absent until the shell broadcasts again or the consumer toggles `Visible`. Recovery never re-broadcasts on its own behalf. This is a scope boundary rather than an oversight - a retry loop inside a window procedure the shell invoked is a decision for a later slice. | `Failed_re_add_retries_once_then_raises_TrayError_without_throwing`, `Failed_SetVersion_during_recovery_rolls_back_and_keeps_the_handle` |
| S03's finding **F1** is unchanged | A disposal-driven menu close still does not deliver `Closed`. S05 changed the recovery branch and the teardown-free contract and touched no menu-close path, so F1 sits exactly where S03 left it. | `docs/UAT-S03.md` (F1); the S05 menu test asserts the menu is left *alone* across a restart, which is a different claim from closing it |
| **S06 hand-off** | Recovery is a private branch on the same `TrayIcon` both surfaces use, and its guard is `IsRegistered`, which every registration path sets - so nothing in it is declaration-specific. What is worth watching at the S06 boundary is the timing of a declared instance's first registration: a declarative instance that registers later simply has a later `IsRegistered` edge, and a broadcast before it is a no-op by construction (`An_instance_that_never_showed_an_icon_has_no_host_to_recover_into`). | `TrayIconXamlContractTests`, `docs/UAT-S06.md` |
| Recovery's **Verbose line was never observed from the sample** | The sample's own `sample|`/`sample!` stream in Checks 9-11 shows the `Modify` failure line but no `Verbose` recovery line, because the sample never raises the library's trace level above its `Warning` default. The library's line is proven by the seam test, not by the live runs - which is what the note in "What this slice does not claim" above says. | `Successful_recovery_writes_one_verbose_line_naming_the_broadcast` |
| The **full-suite popup flake** (S03 F5's class) | A third-party window holding the foreground can stop WPF popups from opening during a full-suite run; it did so once here, in three pre-existing tests. Not fixed in S05 - it is a test-harness interaction, and hiding it behind retries would trade a visible flake for an invisible one. | `docs/uat-logs/S05/t06-suite.txt`, `docs/UAT-S03.md` (F5) |

---

## Reproducing this

Raw, unfiltered logs from the runs cited above are kept with this record, so the excerpts above can be checked against the full output (including the sample's own lines, which are interleaved):

| Run | Log |
|---|---|
| Check 1, 2, 3 (Explorer restart) | `docs/uat-logs/S05/check1-3-explorer-restart.log` |
| Check 9 (T04 acceptance run: Explorer restart, balloon, menu, delivered revision) | `docs/uat-logs/S05/t04-live-recovery.txt` |
| Check 9 operator log (the two commands issued, with wall-clock timestamps) | `docs/uat-logs/S05/t04-live-recovery.ops.txt` |
| Check 4, 5 (hard kill, graceful) | `docs/uat-logs/S05/check4-5-hard-kill-and-graceful.log` |
| Check 6 (positive control) | `docs/uat-logs/S05/check6-positive-control.log` |
| Check 7 (guard) | `docs/uat-logs/S05/check7-guard.log` |
| Check 8a (harness re-verification, the acceptance run) | `docs/uat-logs/S05/t03-plan-verify.txt` |
| Check 8b (negative controls, including the discarded attempt) | `docs/uat-logs/S05/t03-negative-controls.txt` |
| Check 8c (probe-triggered balloon/menu, and the positive control) | `docs/uat-logs/S05/t03-alias-triggers.txt` |
| Check 10b (T05 acceptance run: the hard kill, the presence series, the after-exit verdict) | `docs/uat-logs/S05/t05-hard-kill.txt` |
| Check 10c (independent tray inventories: before, immediately after, settled after the kill) | `docs/uat-logs/S05/t05-tray-before-kill.txt`, `t05-tray-after-kill-immediate.txt`, `t05-tray-after-kill-settled.txt` |
| Check 10d (the second live icon, where the two oracles were compared on the same slot) | `docs/uat-logs/S05/t05-tray-before-graceful.txt` |
| Check 10e (counter-case: graceful exit, both oracles) | `docs/uat-logs/S05/t05-graceful-exit.txt`, `docs/uat-logs/S05/t05-tray-after-graceful.txt` |
| Check 10f (no-fallback proof: the grep and its result) | `docs/uat-logs/S05/t05-library-no-fallback.txt` |
| Check 10g (the scanner's own two-way control) | `docs/uat-logs/S05/t05-scanner-discrimination.txt` |
| Check 10 suite re-run (build plus 394 tests on the T05 revision) | `docs/uat-logs/S05/t05-suite.txt` |
| Check 10 operator log (every command issued and every scan timestamp) | `docs/uat-logs/S05/t05-live-teardown.ops.txt` |
| Check 11 (the whole demo in one session: window-free host, Explorer restart, balloon, menu, force kill) | `docs/uat-logs/S05/t06-e2e-demo.txt` |
| Check 11 operator log (every command issued, with wall-clock timestamps) | `docs/uat-logs/S05/t06-e2e-demo.ops.txt` |
| Check 11 independent tray inventories (during recovery, and after the kill) | `docs/uat-logs/S05/t06-tray-during-recovery.txt`, `docs/uat-logs/S05/t06-tray-after-kill.txt` |
| T06 build and suite measurement (three TFMs, 394 tests, the flaky run, the isolation run, the per-class composition) | `docs/uat-logs/S05/t06-suite.txt` |
| T06 acceptance command, the flaky run made with other commands in flight (build exit 0, test exit 1, 392/394) | `docs/uat-logs/S05/t06-acceptance.txt` |
| T06 acceptance command re-issued alone (build exit 0, test exit 0, 394/394) | `docs/uat-logs/S05/t06-acceptance-rerun.txt` |

The logs for Checks 1-8 were written to `/tmp` while the runs were made and copied here afterwards; the Check 9 and Check 10 logs were written straight into `docs/uat-logs/S05/` by the runs themselves. The `[sample]` lines in them are the sample's own stdout/stderr, which the probe relays verbatim. Log lines are prefixed `[probe]` for the observer, `sample|` / `sample!` for the sample's standard output and error, and `[t05 ...]` for the Check 10 operator log.

**Note for anyone tidying the repository:** these files are tracked on purpose even though the repository's `.gitignore` excludes `*.log`. They are the raw evidence this document cites, so removing them as "stray logs" breaks the record. The Check 1-7 logs predate that rule and were added with `git add -f`; the Check 8, Check 9 and Check 10 logs carry the `.txt` extension instead so they are committed by the ordinary add rather than by a hand-forced one, because evidence that depends on remembering a special flag is evidence that can be lost by forgetting it.

The commands that make a check reproducible are the probe line above plus, for the Explorer-restart checks, the shell restart (`taskkill //f //im explorer.exe`, then start `explorer.exe`). Check 9's operator log records both commands with wall-clock timestamps, so the restart can be timed against the series. Everything else is self-contained in the probe invocation. Restarting Explorer is disruptive - the taskbar restarts, open File Explorer windows close, the overflow flyout resets - so the restart checks are once-per-session measurements rather than loops (Check 1 and Check 9 are the two that were made).

Check 10 needs no Explorer restart and is therefore cheap to repeat: two commands per case, the probe line for the case and `powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File scripts/tray-inventory.ps1 -OpenOverflow -All -Needle Trustsoft.NotifyIcon` at the timings recorded in `t05-live-teardown.ops.txt` (before the kill, immediately after it, and once it has settled). The scanner is committed as a tracked script alongside the probe for exactly that reason.
