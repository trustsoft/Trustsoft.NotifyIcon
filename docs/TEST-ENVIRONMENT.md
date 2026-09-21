# Test environment: the desktop-foreground-sensitive menu/popup tests

Status: **accepted as a documented environment dependency** (recorded 2026-09-21, milestone M001
validation round 1 findings, user-approved disposition). A contention guard (skip-with-reason when
`SetForegroundWindow` fails) is a named follow-up, not shipped in v1.

## What is affected

Five tests in four classes fail **deterministically in a session where Windows denies the
foreground to the test process**, and pass when the process may take the foreground:

| Test | Class | Failure signature |
| --- | --- | --- |
| `A_second_right_click_while_the_menu_is_open_opens_nothing_new` | `TrayIconMenuActivationTests` | `anchorIsForeground=False`, popup never opens |
| `A_failing_shell_rectangle_takes_the_cursor_fallback_and_records_the_hresult` | `TrayIconMenuActivationTests` | `anchorIsForeground=False`, `foreground=0x…(Shell_TrayWnd)` |
| `A_dismissable_popup_is_owned_by_the_anchor_window_not_by_the_registration_host` | `SliceContractTests` | `owner=0x0`, `setForegroundWindow=False` |
| `A_menu_already_open_on_another_icon_is_reported_and_the_live_popup_is_not_moved` | `TrayIconMenuContractTests` | popup `owner=0x0` instead of the anchor |
| `A_menu_anchored_to_the_anchor_window_is_owned_by_it_and_dismissed_by_an_outside_click` | `TrayMenuDismissalTests` | `owner=0x0`, outside click does not dismiss |

## Mechanism (not a library defect)

WPF's `Popup.BuildWindow` decides the popup's owner from the **foreground window at the instant the
popup is created**. The tests therefore have an environment precondition: the test process must be
allowed to make its anchor window foreground. In an automation session (agent shell, CI runner,
remote desktop without interactive desktop) Windows denies foreground transitions to background
processes: `SetForegroundWindow` returns false, the popup is created **ownerless** (`owner=0x0`),
and the ownerless popup is not dismissed by an injected outside click. The same S03 findings (F5
"the anchor or nothing, never the registration host" and F4 "a menu opened for a hidden icon is
ownerless and not dismissed") document this mechanism from the product side.

The library behaviour under test is unchanged by the environment: the anchor window, placement
arithmetic and dismissal contract are the tested subject, and the pure placement arithmetic
(`TrayIconPlacementTests`, 82 fixtures) plus all seam, marshalling, purity and packaging tests are
unaffected.

## Evidence

- Same binary, same source revision (`1b497c7`, clean tree), **green**: scoped suite 387/0/0 and
  `TrayIconMenuActivationTests` isolated 16/16 twice (2026-09-21, gsd_exec `a36b39c9`); three of
  five S07 closeout full-suite runs green at 403.
- Same binary, **red in the constrained session**: 398/403 with exactly the five tests above
  failing (gsd_exec `b7f3c82c`, reproduced twice gsd_exec `707aaaaa`), each failure carrying
  `setForegroundWindow=False` and a foreign window (`Shell_TrayWnd` / terminal window) holding the
  foreground (diagnosis gsd_exec `90b21c25`: `popupOwner=0x0`,
  `foregroundBeforeOpen=foregroundAfterClick=0x893094C`).

## How to run

Run the suite in an interactive desktop session where the test process may take the foreground
(plain local console, not an agent shell or headless CI). Where that is impossible, exclude the
five tests above and treat the exclusion as the documented environment limitation — do not report
a run that never opened a popup as menu-interaction coverage.
