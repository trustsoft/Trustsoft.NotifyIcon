# M002 UAT: the owner's subjective acceptance record

**Milestone:** M002 - WinRT toast notifications
**Recorded:** 2026-09-24, in the interactive session, against worktree HEAD
`7fae1dd4e313aee9d40d6fa8a2c63f494a67269`
**Owner answers:** authenticated in the session and persisted in the GSD database as milestone human
acceptances. The database is authoritative for the revision binding (each acceptance carries the
tested source revision); the `criterionId` column below is the binding key that ties this document to
the database rows one to one.

Why this document exists as well as the database rows: the four criteria below are subjective by
construction - they are about how the toast looks and how the API reads - so no instrument in this
project can assert them. The database holds the authenticated acceptance; this file holds the same
acceptance in the form a human reviewer reads, with the evidence each recommendation rested on, so a
later auditor can tell what was judged and why it was judged acceptable.

(Read me from `docs/uat-logs/`, the tree this project already keeps its UAT captures in: it is
gitignored, so recording the acceptance here does not move the source revision the acceptances are
bound to.)

## The four subjective criteria and the owner's answers

| # | Criterion key | What the owner judged | Owner's verbatim answer | Disposition | criterionId (DB) |
|---|---|---|---|---|---|
| 1 | `native-look-and-behaviour` | Does the toast look and behave like a native Windows notification - title and body wrapping, the two action button labels, the severity mapping - rather than reading as foreign or clipped? | `Accept (Recommended)` | accepted | `8f5c63f4-e7db-42ff-889f-7598ea828b4c` |
| 2 | `image-scaling-and-placement` | Does a toast's image appear at the documented placement (`appLogoOverride` / `hero`) and look correctly scaled rather than stretched, cropped, squashed or absent? | `Accept (Recommended)` | accepted | `b3a5a51d-0bc0-4015-9046-df978e9ec2e3` |
| 3 | `identity-registration-side-effect` | Is it acceptable - and clearly reported - that the first toast makes the library create a Start-menu shortcut carrying the AppUserModelID outside the application directory, and that `Dispose` removes it (a hard-killed process leaves it until the next run's own teardown)? | `Accept (Recommended)` | accepted | `4222d753-7734-4dad-9813-0e1426d6aeb9` |
| 4 | `api-ergonomics` | Is the public surface ergonomic for a consumer - build a toast from `ToastContent`/`ToastImage`/`ToastButton`, show it with one call, handle activations through three typed events? | `Accept (Recommended)` | accepted | `15accd71-64d6-41e6-8e83-f01a0c2ea1da` |

## Interactive click capture (owner-observed)

The milestone's Integration class asks for "a real click on the toast body and on an action button
... delivered back into the process". No unattended instrument can credit a delivered activation to a
specific click (S01's measurement, recorded in `docs/TOAST-MEASUREMENT.md`), so this clause was
measured the only way it can be: by the owner, at the keyboard.

**Command** (from the milestone worktree):

```
samples/Trustsoft.NotifyIcon.Sample/bin/Release/net8.0-windows/Trustsoft.NotifyIcon.Sample.exe --toast --toast-buttons --toast-after 2 --run-seconds 30
```

**Result, as reported by the owner on 2026-09-24:** clicking Button 1, then Button 2, then the banner
body each came back into the process as its own classified activation - one `element=button-1`
delivery carrying the first button's argument, one `element=button-2` delivery carrying the second
button's, and one `element=body` delivery - so a delivered activation is attributed to the element
the owner actually pressed, and the three clicks are distinct rather than collapsed into one.

**Provenance and limits, stated plainly.** This row is a human observation recorded by the agent at
the owner's word; it is not an instrument transcript and no raw terminal capture of the three lines
is attached. What it establishes is exactly what the clause asks and nothing more: a real click on a
real banner is delivered back into the process and classified by element. The classifier itself
(`button-1`, `button-2`, `body`, otherwise `unknown` - never a guess) is static-checked and the
delivery path underneath it is covered by the suite (S03's
`Activated_carries_the_delivered_argument_verbatim` and
`Each_show_subscribes_callbacks_that_reach_the_notifier`). What it does not establish is anything
about a click arriving on a different machine or desktop state.

## What the owner ran, and what the machine had already measured

**1. Native look and behaviour.** Owner ran the two-button sample command above and looked at the
banner. Machine side: the payload is the platform's own `ToastGeneric` template with no custom
template or styling; the exact-string payload contract filter is 33/33 green (`gsd_uat_exec
4e855752-3362-481a-b939-b3867a0cb8f7`); every live run had `LoadXml`, `CreateToastNotification` and
`Show` at `0x00000000` with the title and body verbatim in the `<text>` elements (`gsd_uat_exec
a3e325bd-93b3-440b-bb4c-46f9ffc1384b`).

**2. Image scaling and placement.** Owner ran `powershell.exe -NoProfile -ExecutionPolicy Bypass
-File scripts/probe-toast/run-image-demo.ps1` and looked at the banner. Machine side (`gsd_uat_exec
24c2402f-4cbe-4ca6-bdd4-52daaf423bf0`): the `ImageSource` was rasterized to a 256x256 PNG and traced
with its byte length, the shell was handed an absolute `file:///` reference, and a separate observer
process saw `files=1 present=True` while the toast was live and `files=0 present=False` after
teardown, with `refused=0` and the runner's own `runner verdict: PASS`. Rendering itself is the part
no instrument here can see, which is why this criterion exists.

**3. Identity registration side effect.** Owner ran the sample's toast once, inspected the Start-menu
entry and read the sample's registration / read-back / removal lines. Machine side: the identity
lifecycle was measured from a second process per framework - clean slate absent (`0x80070002`),
mid-run `success=True value='Trustsoft.NotifyIcon.ConsumerProof.<tfm>'` while the consumer was still
alive, post-teardown absent again (`gsd_uat_exec 00c8ccba-cc7f-4ec3-a339-07317182a90d`) - and the
kill case was measured on 2026-09-24 (`gsd_uat_exec 6b7282d2-9e4d-4d3d-9bc5-ff8bdd78ef4d`): the
shortcut is still registered immediately after a hard kill, and the next run re-registers the same
identity, shows and is accepted (`shows=1 accepted=1 refused=0`), and removes it on its own teardown.
Design rationale: D060.

**4. API ergonomics.** Owner read the README's toast usage section and the consumer proof's toast code
(`samples/consumer-proof/App.xaml.cs`, the `--toast` path). Machine side: a package-only consumer
builds against the packed artifact on net8.0, net9.0 and net10.0-windows, asserts the documented
twenty-type surface (`surface-only` exit 0 on 3 of 3) and shows a real toast with the three typed
events subscribed (`gsd_uat_exec 00c8ccba-cc7f-4ec3-a339-07317182a90d`, `gsd_uat_exec
39ee02e6-9a06-48cf-afe1-5c52dd91c965`); the README's guarded facts are pinned and fail when mutated
(five mutations, each failing the real guard, `gsd_uat_exec c5d58454-09bb-4155-91cf-088b140ccd6e`).

## Honest limits of this record

- Three of the four criteria are visually judged; a reviewer cannot re-derive them from this file
  alone, only see what was looked at and what the machine had already established. That is the nature
  of a subjective criterion, and it is why the disposition is recorded per criterion rather than
  summarised.
- The acceptances are bound to the tested revision recorded by milestone validation for M002. Any
  later source change moves that revision and requires a fresh acceptance rather than inheriting
  these.
- Criterion 3's acceptance covers a documented consequence, not an absence: a process killed without
  `Dispose` does leave its shortcut behind until the next run's teardown. The owner accepted that as
  acceptable for a library whose activator is the shortcut itself.
- The click capture is the owner's word, not a transcript; S05's unattended three-framework runs
  measured `toast activations=0` on all three TFMs precisely because no click was injected, and that
  reading is unchanged by this row.
