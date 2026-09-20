# UAT-S04 — Live balloon-notification checks on the notification-area icon

These checks are **manual** and they are the only evidence for the part of S04 that a GitHub Actions
Windows runner cannot produce. Those runners have no interactive desktop session, so no user ever
clicks the icon, no shell ever renders a balloon for it, and no `NIN_BALLOONUSERCLICK` callback ever
arrives. The automated suite does the other half honestly — over `FakeShellApi` it asserts the exact
`NOTIFYICONDATAW` the library hands the shell (the `uFlags`/`dwInfoFlags` bits, the truncated
strings, the untouched union slot), and it posts a real version-4 callback message into the real host
window and watches the routed events — but it never sees the notification area, and what the shell
does with the structure it was handed is invisible to it.

**Do not report these checks as automated coverage, and do not report a check as passed that was not
measured.** Scope: S04 only (balloon show, severity, sound/realtime/quiet-time behaviour, the
balloon click routed pair, and the two boundary contracts S04 owes S05 and S06).

Two sessions wrote into this document. **T04** produced the click-driven live proof with the
`probe-balloon` instrument (captures in `.gsd/s04-evidence/`); **T05** (this document) added two
switch-variant live runs, the mutation-bite record and the boundary contracts. Both sessions ran the
same build revision, so the captures are directly comparable.

## How the results below were obtained

Every `Result:` line is a **measurement from a named instrument**, not a look at the screen: the
agent that ran this checklist has no human eye and cannot view a screenshot, so "observed" means
*recorded by the instrument named in the line*. Anything that could not be established this way is
marked **NOT OBSERVED** with its reason, never inferred into a pass.

- **The sample's console (`Trustsoft.NotifyIcon.Sample`)** - the in-process half. The sample prints
  one startup line naming the balloon configuration actually in effect
  (`[sample] balloon demonstration: severity=... sound=... realtime=... respectQuietTime=... - a
  single left click on the icon shows this balloon...`), hooks the private-message band
  (`0x0400..0x7FFF`) and prints every raw callback (`event=0x... iconId=... wParam=... lParam=...`),
  prints one `[sample] balloon preview clicked:` / `[sample] balloon clicked:` line per routed-event
  delivery, and ends with a totals line counting clicks, cancellations, balloon show requests and
  balloon deliveries. The raw callback lines are read by the sample's own `HwndSource` hook, which is
  what makes "the shell sent this code" a recording rather than a conclusion.
- **The balloon probe (`probe-balloon`, a scratch instrument under `.gsd/probe-balloon/`, not product
  code)** - real input and the out-of-process half, in the `probe-clicks` convention. It expands the
  notification-area overflow flyout through UI Automation, injects a **real** left click on the icon
  (`SendInput`), polls the bottom-right quadrant of the screen for a *new* visible window (the toast
  banner), records its class and process, and clicks it — which is what makes the shell deliver
  `NIN_BALLOONUSERCLICK`. It captures the sample's stdout into the same record. T04's three runs are
  preserved verbatim in `.gsd/s04-evidence/t04-live-click-driven-balloon.txt`,
  `t04-live-self-shown-balloon.txt` and `t04-live-cancel-preview-left.txt`.
- **The shell-acceptance callback (`event=0x0402`, `NIN_BALLOONSHOW`)** - the shell's own statement.
  The totals line counts balloon show **requests**, not confirmations; the `0x0402` callback is what
  turns "the library asked" into "the shell accepted and showed". On Windows 10/11 the shell renders
  a legacy balloon as a **banner notification** — that rendering is the OS's decision, and the
  protocol (legacy `Shell_NotifyIcon` with `NIF_INFO`) is unaffected by it, so a banner on screen is
  the expected shape of this check, not a deviation.
- **The seam (`FakeShellApi` in `tests/Trustsoft.NotifyIcon.Tests`)** - the only instrument that can
  read the *bits*. No live instrument can see the `NOTIFYICONDATAW` after it leaves the process, so
  every claim in this document about `uFlags`/`dwInfoFlags` values names its
  `TrayIconBalloonTipTests` assertion as the reading. The live runs then show the shell accepting
  what those tests pin.
- **`gsd_exec` captures (`.gsd/exec/<id>.stdout`)** - T05's own runs (the two switch variants, the
  mutation bite and the green re-run) are recorded there with their ids, as in UAT-S03.

Environment: **Windows 11 Pro, build 26200** (`10.0.26200.9457`), x64, .NET SDK **10.0.401**, test
TFM `net8.0-windows`, interactive session, one monitor 1920x1200 physical at Display Scale 150 %
(144 DPI effective, work area 1920x1128), icon in the notification-area **overflow flyout**. Build
revision for every capture in this document: commit `dd6e22b`
(`dd6e22bae88ffe4302a6da31228e07a03225345d`, "Sample now shows a balloon on a single tray click..."),
with the working tree during T05 differing only in documentation and comments (`src/` was verified
clean with `git status` before and after the mutation cycle below).

Commands (T05's own runs; the probe scenario names are T04's):

```text
dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore
dotnet build samples/Trustsoft.NotifyIcon.Sample -c Release --no-restore
dotnet run --project .gsd/probe-balloon -c Release --no-build -- click 40          # T04
dotnet run --project .gsd/probe-balloon -c Release --no-build -- self 40           # T04
dotnet run --project .gsd/probe-balloon -c Release --no-build -- cancel-left 35    # T04
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe --run-seconds 25 --show-balloon-after 6 --balloon-icon error --balloon-realtime
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe --run-seconds 25 --show-balloon-after 6 --balloon-icon info --balloon-respect-quiet-time
dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore --filter "FullyQualifiedName~The_balloon_click_events_are_a_bubble_and_tunnel_pair_with_CLR_accessors"
dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore && dotnet test tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore
```

The T05 switch-variant runs are `.gsd/exec/9c861efa-bb39-4378-8eb5-7a08efd4fb26.stdout` (realtime)
and `.gsd/exec/6d122955-cae2-4dec-911a-24947bc8fae5.stdout` (quiet time), preserved beside the
instrument outputs in `.gsd/s04-evidence/t05-live-realtime.txt` and
`t05-live-quiet-time.txt`.

## Prerequisites and how to run it yourself

- Windows 10 or 11 with an interactive desktop session and a notification area. The icon lives in
  the tray's overflow flyout unless you promote it; notifications must not be globally disabled for
  the app in Settings > System > Notifications.
- A .NET 8 (or later) SDK and a terminal in the repository root.
- Build once:
  ```text
  dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore
  dotnet build samples/Trustsoft.NotifyIcon.Sample -c Release --no-restore
  ```
- The **no-instrument run** needs nothing but the sample binary — it shows its own balloon and
  prints everything it knows:
  ```text
  samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe --run-seconds 25 --show-balloon-after 6 --balloon-icon warning --balloon-nosound
  ```
  `--show-balloon-after [seconds]` (default 5) shows one balloon with **no click injected**; without
  it, a **single left click on the icon** in the overflow flyout is what shows the balloon. The
  switches: `--balloon-icon none|info|warning|error`, `--balloon-nosound`, `--balloon-realtime`,
  `--balloon-respect-quiet-time`, `--cancel-preview left` (suppresses click *and* balloon), and the
  usual `--run-seconds N` self-exit. A startup line names the effective configuration so any capture
  records what was asked for next to what happened.
- The **click-driven run** — the exit condition's shape — needs the `probe-balloon` instrument
  (session scratch, like S03's `probe-clicks`; note the known repo-hygiene issue that a *new*
  project restore under this worktree fails while the duplicate `NuGet.config`/`nuget.config` pair
  exists, which is why T04 built the probe in `%TEMP%`):
  ```text
  dotnet run --project .gsd/probe-balloon -c Release --no-build -- click 40
  ```
  The check is green when the capture contains: the startup `balloon demonstration:` line; the raw
  left-click callbacks `0x0201`/`0x0202`; the decoded `[sample] click type=TrayLeftClick` line;
  `balloon show requests=1`; the raw `event=0x0402` (`NIN_BALLOONSHOW`); the probe's banner line
  naming the new bottom-right window; the raw `event=0x0405` (`NIN_BALLOONUSERCLICK`) with
  `wParam=0x0000000000000000`; and then `[sample] balloon preview clicked:` followed by
  `[sample] balloon clicked:` with `balloon clicked deliveries=1` in the totals.
- Any argument the sample does not understand is **rejected on stderr with exit code 2** before any
  shell state exists (eight negative invocations recorded in
  `.gsd/s04-evidence/t04-negative-parser-checks.txt`), so a typo'd switch cannot be mistaken for a
  capture that proved the opposite.

## Checklist

### 1. A single tray click shows a balloon, with the chosen configuration printed next to it

Result: **PASS** (2026-09-20, T04 `.gsd/s04-evidence/t04-live-click-driven-balloon.txt`, scenario
`click`). The probe expanded the flyout, found the icon at `rect=1272,1042 60x60`, and injected one
real left click at its centre `1302,1072`; `WindowFromPoint` there was the shell's own overflow
surface (`class='TopLevelWindowForOverflowXamlIsland' ... process=explorer`). The startup line
printed the configuration **before** anything happened:

```text
[sample] balloon demonstration: severity=info sound=on realtime=off respectQuietTime=off - a single left click on the icon shows this balloon, and clicking the balloon must print a balloon clicked line; --cancel-preview left suppresses both.
```

The shell then delivered the click, the sample asked for the balloon, and the shell accepted:

```text
[sample] raw callback hwnd=0x5640A3E msg=0x0401 event=0x0201 iconId=1 wParam=0x00000000042F0515 lParam=0x0000000000010201
[sample] raw callback hwnd=0x5640A3E msg=0x0401 event=0x0202 iconId=1 wParam=0x00000000042F0515 lParam=0x0000000000010202
[sample] click type=TrayLeftClick button=Left count=1 anchor=1301,1071
[sample] raw callback hwnd=0x5640A3E msg=0x0401 event=0x0400 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010400
[sample] raw callback hwnd=0x5640A3E msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
```

and the totals line says `balloon show requests=1 (self=0)`. On screen, the probe recorded the
rendering half independently of the sample: a **new** visible window in the bottom-right quadrant,
`hwnd class='Windows.UI.Core.CoreWindow' title='New notification' pid=13792
process=ShellExperienceHost` — the Windows 10/11 banner that legacy balloons are rendered as. That
rendering is the OS's decision (the protocol spoken is still legacy `Shell_NotifyIcon`); what the
library owes and delivered is the accepted `NIM_MODIFY`, confirmed by the shell's own `0x0402`.

The no-click control separates the halves: with `--show-balloon-after 6` and no input anywhere
(`clicks=0` in the totals), the same `0x0402` arrives
(`.gsd/s04-evidence/t04-live-self-shown-balloon.txt`), so the balloon path does not depend on the
click path.

### 2. The balloon's severity reaches the shell

Result: **PASS at the seam, PASS for the request live; the drawn glyph NOT OBSERVED** (the bits have
no live instrument; see *What was NOT observed*). The seam reading is
`TrayIconBalloonTipTests.Each_severity_is_written_into_the_low_nibble_of_dwInfoFlags`: for each
`BalloonTipIcon` value, the captured `NOTIFYICONDATAW.dwInfoFlags` carries the value cast —
`NIIF_NONE..NIIF_ERROR` are `0..3`, the low bits `NIIF_ICON_MASK` (0x0F) selects — with
`NIF_INFO` in `uFlags` on the same call, one `NIM_MODIFY` per show. The live half records what the
runs asked for and that the shell accepted each: `severity=info sound=on` (check 1),
`severity=warning sound=off` (T04 self run), and T05's own runs at `.gsd/exec/9c861efa...stdout`
(`severity=error sound=on realtime=on`) and `.gsd/exec/6d122955...stdout`
(`severity=info ... respectQuietTime=on`) — each printed at startup and each followed by the
shell's `event=0x0402` acceptance callback. What the OS *drew* for the error and warning severities
(the banner's glyph) was not measured by any instrument in this session.

### 3. The behaviour switches change what the shell is asked for

Result: **PASS at the seam for all three switches; live for the configurations T04 and T05 ran.**
The bit-level readings are the seam's, because the `NOTIFYICONDATAW` is invisible once it leaves the
process — that is exactly what `FakeShellApi` is for:

| Switch | What the shell is asked for | Seam reading (`TrayIconBalloonTipTests`) |
|---|---|---|
| `--balloon-nosound` | `NIIF_NOSOUND` (0x10) set in `dwInfoFlags` | `NoSound_and_RespectQuietTime_set_their_NIIF_bits_in_dwInfoFlags` |
| `--balloon-respect-quiet-time` | `NIIF_RESPECT_QUIET_TIME` (0x80) set in `dwInfoFlags` | same test |
| `--balloon-realtime` | `NIF_REALTIME` (0x40) set in **`uFlags`**, and **absent** from `dwInfoFlags` | `Realtime_sets_NIF_REALTIME_in_uFlags_and_never_in_dwInfoFlags` |

The live half of each: `--balloon-nosound` ran in T04's self scenario — the startup line printed
`severity=warning sound=off realtime=off respectQuietTime=off` and the shell accepted the request
(`0x0402`, `clicks=0`). T05 added the other two as live runs with no click injected:

```text
[sample] balloon demonstration: severity=error sound=on realtime=on respectQuietTime=off - ...
[sample] raw callback hwnd=0x5A407A0 msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
[sample] raw callback hwnd=0x5A407A0 msg=0x0401 event=0x0404 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010404   (x2 - NIN_BALLOONTIMEOUT)
```
(`.gsd/exec/9c861efa...stdout` / `.gsd/s04-evidence/t05-live-realtime.txt`, exit 0, stderr empty)

```text
[sample] balloon demonstration: severity=info sound=on realtime=off respectQuietTime=on - ...
[sample] raw callback hwnd=0x4B60A1A msg=0x0401 event=0x0402 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010402
[sample] raw callback hwnd=0x4B60A1A msg=0x0401 event=0x0404 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010404   (NIN_BALLOONTIMEOUT)
```
(`.gsd/exec/6d122955...stdout` / `.gsd/s04-evidence/t05-live-quiet-time.txt`, exit 0, stderr empty)

Both totals read `balloon show requests=1 (self=1), balloon clicked deliveries=0`: the switches were
in the request the shell accepted, and — a bonus live reading for the S04/T03 contract — the two
`0x0404` (`NIN_BALLOONTIMEOUT`) callbacks in the realtime run and the one in the quiet-time run
produced **no** `[sample] balloon clicked:` line and no error line, exactly the "lifecycle codes are
Verbose-traced and raise nothing" rule. The quiet-time run shows the balloon *shown* because quiet
time was not active on this system during the run; the suppressed-during-quiet-time outcome was not
observed (see *What was NOT observed*).

### 4. Clicking the balloon raises `BalloonTipClicked`, with the raw `NIN_BALLOONUSERCLICK` printed immediately before

Result: **PASS** (2026-09-20, T04 `.gsd/s04-evidence/t04-live-click-driven-balloon.txt`). The probe
clicked the banner window (`Windows.UI.Core.CoreWindow`, `ShellExperienceHost`), and the sample's
hook recorded the shell's callback and the library's decode in order, on adjacent lines:

```text
[sample] raw callback hwnd=0x5640A3E msg=0x0401 event=0x0405 iconId=1 wParam=0x0000000000000000 lParam=0x0000000000010405
[sample] balloon preview clicked: event=PreviewBalloonTipClicked phase=tunnel - the main phase must follow unless a handler sets Handled.
[sample] balloon clicked: event=BalloonTipClicked phase=bubble - the shell accepted a click on the balloon.
```

Totals: `balloon clicked deliveries=1, balloon preview deliveries=1`. Two raw details are the point
of the capture. First, `wParam=0x0000000000000000` on the `0x0405` line against
`wParam=0x00000000042F0515` on the click lines above it: the mouse callbacks carry a decoded anchor,
the balloon callback carries nothing, and the library reads none of it (the header documents the
balloon `wParam` as undefined) — the asymmetry is visible in the raw stream itself. Second, the
tunnel line precedes the bubble line, one delivery each. In-repo,
`BalloonCallbackTests.A_balloon_user_click_raises_the_preview_before_the_bubble_event_once_each`
pins the same order by posting a real version-4 callback into the real host window.

### 5. A Preview handler setting `Handled` suppresses the main balloon event

Result: **PASS in-repo, PASS live in its transitive sample form**. The direct form is
`BalloonCallbackTests.A_handled_preview_suppresses_the_balloon_clicked_event`: a `PreviewBalloonTipClicked`
handler that sets `Handled` and the `BalloonTipClicked` delivery never happens. The live form is the
sample's `--cancel-preview left` run
(`.gsd/s04-evidence/t04-live-cancel-preview-left.txt`): the probe injected the same real left click
as in check 1, and the sample printed

```text
[sample] click cancelled by Preview handler: previewType=PreviewTrayLeftClick button=Left count=1 anchor=1301,1071 (no [sample] click line for this click)
```

with totals `clicks=0, cancelled by a Preview handler=1, balloon show requests=0 (self=0)`, and the
probe recorded `BALLOON banner | NOT FOUND within the poll window - no banner click was injected`:
no balloon codes in the raw stream at all. The suppression here is *transitive by design* — the
balloon request lives inside the sample's main `TrayLeftClick` handler, so a cancelled Preview
suppresses the handler and the balloon together. That is why this row has two instruments: the unit
test proves the event-level rule (Preview `Handled` kills the balloon *event*), and the live run
proves the sample-level consequence (the balloon is never even *requested*).

### 6. An overlong title and text are truncated rather than rejected

Result: **PASS at the seam, NOT OBSERVED live.** The readings are
`TrayIconBalloonTipTests.Overlong_text_and_title_are_truncated_to_the_field_capacities`
(`szInfo` capped at its 255-character capacity, `szInfoTitle` at 63 — the `WCHAR[256]`/`WCHAR[64]`
header fields) and
`Truncation_never_splits_a_surrogate_pair` (a cut never lands between a surrogate pair, because half
a pair is an invalid string the shell renders as a replacement character). The call still performs
its one `NIM_MODIFY`; nothing throws. The live runs used fixed sample strings, and the sample has no
switch for oversized input, so no capture in this document shows truncation on screen — the claim
rests on the seam alone, which is where the marshalled strings are observable at all.

### 7. The boundary contract S04 hands to S05: a balloon is never part of registration

The roadmap's boundary map carries this edge as: "**S04 to S05** — Produces: *Balloon state that
recovery must not corrupt, so an icon re-created after an explorer restart can still show balloons.*
Consumes: Working icon lifecycle from S01." Restated as the mechanism S05 may rely on:

> Recovery re-runs `NIM_ADD` then `NIM_SETVERSION(4)` and needs nothing from S04, because a balloon
> is never part of registration. A balloon is a plain `NIM_MODIFY` on whatever registration is
> current, so an icon re-created after an explorer restart can show balloons with **no extra state
> to restore**.

Result: **PASS as a seam contract** — the two assertions that make the contract checkable rather
than asserted are `TrayIconBalloonTipTests.ShowBalloonTip_is_one_NIM_MODIFY_carrying_NIF_INFO_and_the_given_strings`
(exactly one `NIM_MODIFY` per show; no `NIM_ADD`, no `NIM_SETVERSION`, nothing else on the seam) and
`The_balloon_call_writes_neither_a_timeout_nor_a_custom_balloon_icon` (the union slot
`uTimeoutOrVersion` on the balloon call still holds `NOTIFYICON_VERSION_4` — the value registration
wrote — and `hBalloonIcon` stays 0; the balloon never writes the registration's own state). Because
the balloon call carries no registration state, S05's re-registration sequence cannot corrupt or
require balloon state: after recovery the next `ShowBalloonTip` is the same `NIM_MODIFY` on the new
registration. The explorer-restart behaviour itself is S05's to prove live; this slice's contract is
that nothing balloon-shaped enters the registration path.

### 8. The boundary contract S04 hands to S06: markup-facing routed events, and deliberately no balloon dependency properties

Result: **PASS as a metadata contract, and the assertion is proven to bite (check 9).** Both balloon
events are routed events registered with `typeof(EventHandler<RoutedEventArgs>)` as the handler type
and `typeof(TrayIcon)` as the owner, each with CLR `add`/`remove` accessors wired to its own
registered event (`TrayIcon.BalloonTipClicked`, `TrayIcon.PreviewBalloonTipClicked`). That is the
whole surface XAML's event resolver needs: markup can wire `BalloonTipClicked="OnBalloonTipClicked"`
and `PreviewBalloonTipClicked="..."` attributes directly, and a handler with the signature
`void OnBalloonTipClicked(object sender, RoutedEventArgs e)` binds — the payload is a plain
`RoutedEventArgs` whose `RoutedEvent` property names which of the pair fired, because there is
deliberately **no `BalloonTipEventArgs`** (decision D031). Equally deliberate: **no balloon
dependency properties exist** — a balloon is a transient *command* (`ShowBalloonTip`), not state,
so there is nothing for `{Binding}` or a style to target, and S06 should not invent one. The named
assertion is
`TrayIconBalloonTipTests.The_balloon_click_events_are_a_bubble_and_tunnel_pair_with_CLR_accessors`:
name, strategy (Bubble/Tunnel), handler type, owner type, CLR subscribe/unsubscribe and delivery
order, in one test.

### 9. The metadata assertion bites (the mutation record, MEM052)

A boundary-contract test that only reads registered metadata is easy to write tautologically, so the
suite's convention (MEM052, the S02 audit's precedent) requires showing it fail on a one-line
mutation of the product surface. **Mutation**: in
`src/Trustsoft.NotifyIcon/TrayIcon.cs`, the `BalloonTipClickedEvent` registration's strategy was
changed from `RoutingStrategy.Bubble` to `RoutingStrategy.Tunnel` (one line; the build stayed
green — the compiler cannot catch a metadata change, which is the point). **The named assertion
failed**, exactly and only at the strategy clause:

```text
[xUnit.net 00:00:00.48]  Trustsoft.NotifyIcon.Tests.TrayIconBalloonTipTests.The_balloon_click_events_are_a_bubble_and_tunnel_pair_with_CLR_accessors [FAIL]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: Bubble
Actual:   Tunnel
  Stack Trace:
     at Trustsoft.NotifyIcon.Tests.TrayIconBalloonTipTests.The_balloon_click_events_are_a_bubble_and_tunnel_pair_with_CLR_accessors() ...TrayIconBalloonTipTests.cs:line 407
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 16 ms
```
(`.gsd/s04-evidence/t05-mutation-failure.txt`, from
`dotnet test ... --filter "FullyQualifiedName~The_balloon_click_events_are_a_bubble_and_tunnel_pair_with_CLR_accessors"`)

**Revert**: the one line was restored byte-identically (`git status` on `src/` clean, `git diff`
empty). **Green re-run**: the full slice verification on the restored revision —
`dotnet build Trustsoft.NotifyIcon.sln -c Release --no-restore && dotnet test
tests/Trustsoft.NotifyIcon.Tests -c Release --no-restore` — build 0 warnings / 0 errors, suite
**373 passed / 0 failed / 0 skipped** (`.gsd/exec/aa6ce093-8b4c-4082-b127-2169cf9d1f36.stdout`).
The live half of this slice's proof is the capture quoted in check 4: the same routed pair observed
end to end on the real notification area, raw callback first.

## Requirements this slice owns

Traceability for the milestone validation; **neither requirement is terminalized here** — terminal
state belongs to complete-slice, and this task writes documentation only.

- **R004** — a balloon can be displayed and a click on it raises an event: proven by checks **1**
  (shown on a single click, shell accepted) and **4** (the click raises the routed pair, raw
  callback first), with check **5** proving the pair's cancellation contract.
- **R016** — chosen severity, sound, quiet-time and realtime behaviour: proven by checks **2**
  (severity at the seam and in every live request) and **3** (each switch changes the bits the shell
  is asked for at the seam, and the shell accepted the nosound/realtime/quiet-time requests live).

## Documentation corrections made by this task

Corrections to files the slice's findings proved stale, made in the file that owns the text:

- **`README.md`** still listed "balloon notifications (S04)" under **"Planned, not implemented"**
  while S04 shipped it. The balloon surface moved to *Working today* (`ShowBalloonTip`, the
  `BalloonTipIcon`/`BalloonTipOptions` enums, the cancellable
  `PreviewBalloonTipClicked`/`BalloonTipClicked` pair, truncation-not-rejection, with a link here),
  and the DPI icon/tooltip sizing follow-up that used to share the balloon bullet is now its own
  planned bullet (it is an S03-era gap, not a balloon statement). The sample description now points
  at this document as well as UAT-S01.
- **`samples/Trustsoft.NotifyIcon.Sample/App.xaml.cs`** — T04 had already added the full
  *"Balloon demonstration (S04)"* remarks paragraph (the four switches, the single-click wiring, the
  `--cancel-preview left` transitive suppression, `--show-balloon-after` as the no-click control);
  T05 extended only the class `<summary>`, which still introduced the sample as S01+S02, to name the
  S03 menu and S04 balloon demonstrations so a reader of the summary knows the switches exist. No
  behaviour changed; the sample was rebuilt after the edit.

## What was NOT observed

- **The severity glyph the OS drew.** Checks 2's live half proves the request and the shell's
  acceptance; the picture on the banner (which glyph for warning/error) was not measured — no
  instrument in this session reads rendered pixels. The *bit* half is the seam's.
- **The rendering style itself** (banner vs classic balloon) is the OS's decision and was observed
  only in T04's probe runs (the `ShellExperienceHost` banner window). T05's two switch runs prove
  acceptance via `0x0402` and did not watch the banner.
- **Quiet-time suppression.** The `NIIF_RESPECT_QUIET_TIME` bit was in the request (seam) and the
  shell accepted and *showed* the balloon (live), because quiet time was not active on this system.
  The "dismissed unshown during quiet time" outcome was never observed live. Exercising it needs a
  system in quiet time (Focus Assist) and is a reviewer exercise.
- **Realtime's "discarded rather than delayed" semantics.** The `NIF_REALTIME` bit is seam-proven
  and the realtime run was accepted and shown; whether the shell would *discard* instead of queue
  under conditions where it cannot show immediately was not measured.
- **Sound audibility.** `--balloon-nosound` is proven by the bit and the accepted request; whether a
  sound was or was not audible in the `sound=on` runs has no instrument.
- **Truncation live** (check 6): the sample has no oversized-input switch; seam only.
- **A balloon click on the taskbar copy of the icon** (not the flyout): the click-driven proof ran
  against the overflow flyout, as in S02/S03; the taskbar-surface caveat carries over.
- **`NIN_BALLOONHIDE` live.** Of the three lifecycle codes, SHOW (`0x0402`) and TIMEOUT (`0x0404`)
  arrived live with no public event; HIDE (`0x0403`) never did (the banner timed out rather than
  being explicitly hidden). Its silence is pinned by `BalloonCallbackTests` instead.
- **The library's Verbose lifecycle trace lines live.** The sample's totals read
  `library trace lines=0` even while `0x0404` callbacks arrived — the same S02 finding F2 shape
  (consumer assemblies cannot reach the library's Verbose lines in .NET 8). The trace half of the
  lifecycle contract is pinned by
  `BalloonCallbackTests.Balloon_lifecycle_codes_are_traced_at_verbose_never_as_errors_and_a_user_click_traces_nothing`.
- **Explorer-restart behaviour** (check 7's other half): this slice records and pins the *contract*
  (balloons are `NIM_MODIFY`-only, registration-free); proving that a recovered icon still shows
  balloons after a real explorer restart is S05's live check.

## Recording the result

Copy the checklist results into the slice summary together with the date, the Windows build number
and the session's display configuration, and carry the NOT OBSERVED entries over verbatim. A failed
check is a failed check: name the step, attach the capture, and do not restate it as a pass. The
sample writes runtime failures to stderr as `[sample] TrayError operation=...`; every run recorded in
this document exited with code 0 and empty stderr, and every balloon show request was confirmed by
the shell's own `NIN_BALLOONSHOW` callback rather than assumed.

Recorded for S04/T05 on 2026-09-20 (Windows 11 Pro build 26200, .NET SDK 10.0.401, x64, one monitor
1920x1200 at Display Scale 150 %, icon in the notification-area overflow flyout, build revision
`dd6e22b`) — the T04 captures were taken in the same session on the same revision:

- checks **1, 4 and 5 = PASS** (machine-measured: the sample's own raw-callback hook and totals line,
  cross-checked by the out-of-process probe's real input and banner discovery; check 5's direct form
  in-repo, its sample form live);
- check **2 = PASS at the seam, PASS for the request live** (severity glyph NOT OBSERVED);
- check **3 = PASS at the seam for all three switches**, live for the nosound (T04), realtime and
  quiet-time (T05) configurations, with the TIMEOUT callbacks arriving eventless as the lifecycle
  contract predicts;
- check **6 = PASS at the seam, NOT OBSERVED live** (no sample switch for oversized input);
- checks **7 and 8 = the S05/S06 boundary contracts, recorded and seam-pinned**, with the S06
  metadata contract's assertion proven to bite by the reverted one-line strategy mutation (check 9,
  `Expected: Bubble / Actual: Tunnel`, full suite green again at 373/0/0);
- the requirements this slice owns are **R004** (checks 1, 4, 5) and **R016** (checks 2, 3), left
  non-terminal for complete-slice.
