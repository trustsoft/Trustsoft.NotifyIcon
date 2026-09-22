# Test environment: the desktop-foreground-sensitive menu/popup tests

Status: **RETIRED as an environment dependency — S08/T04, 2026-09-22, revision `996c9b0`.** The five
failures this file used to accept are now repaired inside the library, are reproduced on purpose by
in-repo tests, and no longer depend on the session. The skip-with-reason guard this file named as a
follow-up is **explicitly rejected: a skip is masking.** No test in these classes is skipped, and none
carries a conditional guard.

## What this file used to accept

Five tests in four classes failed **deterministically in a session where Windows denies the
foreground to the test process**, and passed when the process may take the foreground:

| Test | Class | Failure signature |
| --- | --- | --- |
| `A_second_right_click_while_the_menu_is_open_opens_nothing_new` | `TrayIconMenuActivationTests` | `anchorIsForeground=False`, popup never opens |
| `A_failing_shell_rectangle_takes_the_cursor_fallback_and_records_the_hresult` | `TrayIconMenuActivationTests` | `anchorIsForeground=False`, `foreground=0x…(Shell_TrayWnd)` |
| `A_dismissable_popup_is_owned_by_the_anchor_window_not_by_the_registration_host` | `SliceContractTests` | `owner=0x0`, `setForegroundWindow=False` |
| `A_menu_already_open_on_another_icon_is_reported_and_the_live_popup_is_not_moved` | `TrayIconMenuContractTests` | popup `owner=0x0` instead of the anchor |
| `A_menu_anchored_to_the_anchor_window_is_owned_by_it_and_dismissed_by_an_outside_click` | `TrayMenuDismissalTests` | `owner=0x0`, outside click does not dismiss |

All five exist at this revision with those names and all five pass (see the totals below).

## The mechanism, and what replaced it

WPF's `Popup.BuildWindow` decides the popup's owner from the **foreground window at the instant the
popup is created**: `param.ParentWindow` is set only when the placement target resolved to an
`HwndSource` and `ConnectedToForegroundWindow(parent)` is true at that instant. In a session that
refuses the foreground claim the popup was created **ownerless** (`owner=0x0`), and an ownerless popup
was not dismissed by an injected outside click. That was the whole mechanism behind the five failures
(S03 findings F4 and F5; `docs/REMEDIATION-S08-MEASUREMENT.md` carries the four-variant measurement).

**S08 moved the value out of WPF's hands.** After `menu.IsOpen = true`, `TrayIcon.OpenMenu` resolves
the popup from the menu's own presentation source (`PresentationSource.FromVisual(menu)`, never a size
or class heuristic over the process's windows), reads `GW_OWNER`, writes the anchor's handle when it
differs and reads it back, and then re-claims the foreground through the attach-thread sequence
(`AttachThreadInput` → `BringWindowToTop` → `SetForegroundWindow` → detach), with `SwitchToThisWindow`
as the recorded last resort when no window holds the foreground. Both halves are conditional on the
measured state, so the ordinary click-driven path writes nothing and makes exactly one claim. The
decision is D044; the disposal-driven close notification the same slice repaired is D045 (superseding
D030). `docs/UAT-S08.md` carries the claim-per-instrument table and `docs/UAT-S03.md` F1/F4/F5 now
carry their closure status.

## Raw totals: before and after

| When | Session | Command | Result | Evidence |
| --- | --- | --- | --- | --- |
| S06 UAT, revision `c57446a` (2026-09-22) | agent shell, **foreground refused** | `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore` | **`Failed: 5, Passed: 399, Skipped: 0, Total: 404`**, exit 1, 55 s — reproduced again in 56 s | gsd_uat_exec `cb18d05d-24ff-4015-8373-e84ba41e6d4d`, `7e1dda82-6312-4f98-8d15-54871eb12319` |
| M001 validation round 1, revision `c57446a` | interactive desktop | same command | `404 passed / 0 failed` | `01-VALIDATION.md` round 1 |
| S08/T04, revision `996c9b0` (2026-09-22) | agent shell, **foreground refused** (measured, below) | `dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build` | **`Passed! - Failed: 0, Passed: 416, Skipped: 0, Total: 416`**, exit 0, 79 s — twice in a row | gsd_exec `9a9e274d-b545-4f4a-a37e-480af5227424`, `1fed151a-2d61-4bb9-821f-502e27c38dc1` |
| S08/T04 retry, same revision plus the uncommitted doc/test edits (2026-09-22) | agent shell, **foreground refused** (re-measured in the same exec) | same command | **`Passed! - Failed: 0, Passed: 416, Skipped: 0, Total: 416`**, exit 0, 1 m 16 s and 1 m 15 s — twice in a row | gsd_exec `90e93238-8b21-47c3-927f-38d30cb00d37` |

The 416 is the 404 baseline plus the eight tests S08/T01–T02 added (4 probe + 4 determinism) and the
four S08/T03 added (close notification). None of the five previously failing tests changed its name and
none was removed, skipped or narrowed to "the anchor or absent" where the value is the library's own
write.

The S08/T04 session is hostile by measurement, not by assertion (scratch PowerShell probe, gsd_exec
`e8604f6e-9ae8-4446-8ad4-6c58ecf6732e`):

```
probe pid=10032
foregroundBeforeAnyWindowOfOurs=0x2260A4E class=CASCADIA_HOSTING_WINDOW_CLASS pid=14372 process=WindowsTerminal
foregroundBeforeHeldByThisProcess=False
windowCreated=0x1D3056C
setForegroundWindow=False
foregroundAfterClaim=0x2260A4E class=CASCADIA_HOSTING_WINDOW_CLASS pid=14372 process=WindowsTerminal
windowIsForegroundAfterClaim=False
```

`setForegroundWindow=False` with a foreign window (the terminal, `0x2260A4E`) holding the foreground is
exactly the shape the S06 diagnostics recorded. The same session's live sample run reads
`owner=0x0` on its own open line and still reports `menu dismissals=1` (gsd_exec `9ea72e4d`, raw output
in `docs/UAT-S08.md`).

## What is deterministic now

1. **Owner identity.** With the foreground claim refused, the popup's `GW_OWNER` equals the library's
   anchor window and is never the shell registration host.
   `TrayIconMenuOwnerDeterminismTests.With_the_foreground_claim_refused_the_popup_is_owned_by_the_anchor_and_never_by_the_registration_host`
   (product path: registered `TrayIcon`, injected version-4 `WM_CONTEXTMENU`, real popup),
   `TrayMenuOwnerMechanismProbeTests.The_delivered_open_writes_the_anchor_into_the_owner_slot_and_reads_it_back`.
2. **Dismissal.** In that same refused state an injected outside click dismisses the menu and leaves no
   popup window behind; the click-driven path is unchanged (one granted claim, no repair, the pinned
   rectangle and offset round trip).
   `TrayIconMenuOwnerDeterminismTests.With_the_foreground_claim_refused_an_outside_click_dismisses_the_menu_and_leaves_no_popup_window`,
   `...The_click_driven_path_claims_once_needs_no_repair_and_is_placed_unchanged`,
   `TrayMenuDismissalTests` (6/6).
3. **The close notification.** A disposal-driven close delivers `ContextMenu.Closed` to a consumer
   handler exactly once before `Dispose` returns, in the refused state too, and the close trace line
   says whether it was delivered.
   `TrayIconMenuCloseNotificationTests` (4/4).
4. **The suite.** 416 passed / 0 failed / 0 skipped, twice, in the session above — where the same
   command produced `Failed: 5, Passed: 399, Total: 404` twice before this slice.

## What still depends on the OS

- **A click the OS never delivers to this process cannot be observed by any instrument here.** The
  Windows 11 overflow flyout owns this session's tray icon, so a synthesised click at the icon's own
  shell-reported rectangle reaches nothing (S06 finding F1, unchanged). The dismissal claims therefore
  rest on the in-repo tests, which inject the shell's version-4 `WM_CONTEXTMENU` callback into the real
  host window and then inject a real outside click at a real popup: hermetic for the *shell seam*, real
  for the *popup window, the click routing and the dismissal*. No row anywhere claims a live user-path
  outside click.
- **Keyboard dismissal (Escape), the live Display Scale matrix, and the two-monitor mixed-scale case**
  remain the human follow-ups S03 recorded (`docs/UAT-S03.md`, "What was NOT observed"); S08 did not
  change that surface.
- **The normal (non-hostile) session** was not available to S08/T04: this unit only ever had the agent
  shell, so the normal-session leg rests on the hermetic "claim granted" tests plus the pre-S08
  interactive-session total recorded above, and it is written as such in `docs/UAT-S08.md`.

## The rejected guard

The earlier version of this file named "a contention guard (skip-with-reason when
`SetForegroundWindow` fails)" as a follow-up. **It is rejected.** The refusal is now the *tested* state
(a scripted seam makes it reproducible on demand), so a skip keyed on it would remove exactly the
coverage that proves the repair works; and a suite that skips its own hardest case cannot be the exit
measurement for the milestone. The repair is conditional on measured state, not on which session it
runs in, so there is nothing left for a guard to guard.

## How to run

```
dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --no-build
```

Run the whole suite; do not exclude any class. In this worktree `dotnet` must run with a Windows temp
directory (git-bash exports `TEMP=/tmp`, which breaks NuGet's temp path with `NETSDK1060`): either use
`cmd //c`, or export `TEMP`/`TMP` to `C:\Users\<user>\AppData\Local\Temp` first.

The agent shell needs the rest of the shell folders too, and must keep the CLI off the reused build
servers:

```
export USERPROFILE="C:\Users\<user>"
export APPDATA="C:\Users\<user>\AppData\Roaming"
export LOCALAPPDATA="C:\Users\<user>\AppData\Local"
export ProgramData="C:\ProgramData"
export ALLUSERSPROFILE="C:\ProgramData"
export TEMP="C:\Users\<user>\AppData\Local\Temp" TMP="$TEMP"
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Without them the CLI reuses an MSBuild or Roslyn compiler server that was started from a stripped
environment, and NuGet's folder resolution throws `ArgumentNullException (path1)`: `dotnet restore`
fails outright and any project whose restore graph is re-evaluated (the sample's WPF temp project) fails
with `error NETSDK1060`. It is the session, not this repository - a trivial project outside the worktree
failed identically (gsd_exec `715f29ac`). The evidence rows in `docs/UAT-S08.md` were produced with the
recipe above.
