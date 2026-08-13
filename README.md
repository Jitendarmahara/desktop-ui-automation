# No-Code Desktop UI Automation Tool — MVP

## What I understood the problem to be
The assignment asks for a no-code tool that can
automate/test a .NET Framework desktop app by identifying and driving legacy Win32 controls
without image/screenshot recognition — essentially a minimal Tosca/Ranorex-style tool. It's
graded on how the problem is scoped and how AI tooling is directed, not on feature completeness.

## What I built
Two projects. **SampleTargetApp** — a WinForms app on classic .NET Framework 4.8: a sample
insurance application with three tabs (Personal Information, Vehicle Details, Coverage & Review),
mixing textboxes, a checkbox, tab navigation, and a submit button with a confirmation message.
**AutomationTool** — a WPF app on modern .NET using FlaUI/UI Automation, with two flows:

- **Record** — attach to the running target app; most actions are captured automatically as you
  just use the app normally (typing, checkboxes, tab switches); a small element picker is used
  only for things that can't be captured live (button clicks — see below) and for assertions
  (Verify Exists / Text Equals / Enabled / Visible). Finish & Save writes the steps to JSON.
- **Run** — pick a saved test from a list, see a full preview of its steps before running, then
  replay it with live pass/fail output per step.

Control identification prefers `AutomationId`, falling back to `ControlType + Name + position`
when a control doesn't expose a stable one — both paths are exercised in the sample test.

## High-level architecture

```
┌──────────────────────────────────────────────┐
│      SampleTargetApp.exe — net48 WinForms      │
│                                                  │
│         Insurance form (app under test)         │
└───────────────────────┬──────────────────────────┘
                         │
                         │   UI Automation (UIA3 via FlaUI)
                         │   cross-process COM
                         ▼
┌──────────────────────────────────────────────-─┐
│        AutomationTool.exe — net9 WPF            │
│                                                  │
│                    MainWindow                   │
│                        │                        │
│           ┌────────────┴─────────────┐          │
│           ▼                          ▼          │
│    RecordTestWindow            RunTestWindow    │
│    + LiveRecorder               + ReplayEngine  │
│           │                          │          │
│           └────────────┬─────────────┘          │
│                         ▼                        │
│                 Shared engine:                   │
│                 ElementInspector                 │
│                 LocatorBuilder / LocatorResolver │
│                 ActionExecutor                   │
│                 AssertionEvaluator               │
└───────────────────────┬──────────────────────────┘
                         ▼
              ┌───────────────────────┐
              │  sample-tests/*.json   │
              └───────────────────────┘
```

### Record flow

```
┌─────────────────────────────┐
│         Click Record          │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│  ElementInspector attaches    │
│      to the target window     │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│  LiveRecorder subscribes to   │
│      UI Automation events     │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│      User just uses the app:  │
│  • typing      → Value changed│
│  • tab switch  → SelectionItem│
│  • checkbox    → Toggle,      │
│      polled (no reliable event)│
│  • button click / assertion   │
│      → manual picker           │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│  LocatorBuilder builds a       │
│         TestStep               │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│        Finish & Save           │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│    sample-tests/*.json         │
└─────────────────────────────┘
```

### Replay flow

```
┌─────────────────────────────┐
│       Click Run Test           │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│      Pick a saved test         │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│  Preview steps                 │
│  (TestSerializer.Load)         │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│      Run Selected Test         │
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│  LocatorResolver re-resolves   │◀─┐
│        the control              │  │  repeat for
└───────────────┬───────────────┘  │  every step
                ▼                   │
┌─────────────────────────────┐    │
│  ActionExecutor /               │    │
│  AssertionEvaluator            │    │
└───────────────┬───────────────┘    │
                ▼                     │
┌─────────────────────────────┐    │
│    Report PASS / FAIL          │────┘
└───────────────┬───────────────┘
                ▼
┌─────────────────────────────┐
│       Final summary            │
└─────────────────────────────┘
```

## Deliberately left out (and why)
- Control types beyond textbox/button/label/checkbox/tab, multi-window/dialog flows,
  branching/looping test logic, retry strategies beyond a fixed 250ms/5s poll, packaging,
  elevation handling, and multi-test suites/CI — none needed to demonstrate the core loop
  end-to-end.
- A merged single-executable design (automation controls embedded directly in the target app)
  was tried and deliberately reverted — it worked, but collapsed the "external tool tests an
  app it doesn't control" premise the assignment is actually about, so the two-app split is
  intentional, not an oversight.

## Assumptions
- No target app was provided, so I built one.
- "No-code" is satisfied by not writing scripts/code to define a test, not necessarily a literal
  input recorder — though live capture (below) gets close to that.

## Findings that only surfaced through actual testing, not planning
- A window that's never been shown in the foreground/was launched minimized exposes *no*
  client-area controls to UIA — the tool now restores and focuses the target window on attach.
- WinForms controls with no `Control.Name` still get a numeric AutomationId from the OS's native
  UIA bridge, but it **changes between app restarts** — the locator builder treats purely-numeric
  AutomationIds as untrustworthy and uses the Name/ordinal fallback instead.
- Windows can silently deny foreground focus to a window created on a background thread (which
  every recorder/runner window here is, deliberately, to keep UIA calls thread-safe) — it opens
  fully functional but invisible behind other windows, which looks exactly like a hang. Every
  window now forces itself to the foreground on load.
- **UIA's native `InvokedEvent` (button clicks) and `ToggleStatePropertyChanged` (checkboxes)
  do not fire for WinForms controls in this environment** — confirmed with both simulated and
  genuine physical mouse input. `Value` property-changed events (typing) and `SelectionItem`
  events (tab switches) do fire reliably. Consequence: live capture handles typing, tab
  switches, and checkboxes (the last via lightweight state polling, since it has no reliable
  event either, but does have durable state to compare); button clicks are still added via the
  element picker — a real platform constraint, not an oversight.
- A `string.GetHashCode()`-based demo reference number in the sample app would have made its own
  confirmation text non-deterministic across app restarts (.NET randomizes string hashes per
  process) — replaced with a simple deterministic computation so the recorded assertion is
  actually repeatable.
- If a stale `SampleTargetApp` instance from an earlier session is still running alongside a
  fresh one, `Attach()`'s original "first process found" selection wasn't guaranteed to pick the
  one actually being worked with — it could silently attach to the wrong instance, and the only
  visible symptom was an unrelated-looking "Element was not found within the timeout" several
  steps into a run. Fixed by checking every live candidate process against the title filter: if
  exactly one matches, attach; if more than one does, fail immediately with the PIDs involved
  instead of guessing. Multi-instance ambiguity is now a same-second, actionable error, not a
  five-second mystery timeout.
- Closing the target app before clicking "Finish & Save" let the name prompt and save-file
  dialogs both complete normally, then failed the actual save with a cryptic COM error (0x80040201)
  shown only in the recorder's small status label — easy to miss, so it looked like Finish & Save
  "didn't save" even though every dialog worked. Cause: the save step re-read the target window's
  `.Title` live via UIA at save time, which throws once the underlying process/window is gone.
  Fixed by capturing the title once at attach time and reusing that cached value at save time,
  instead of re-querying a possibly-dead window.
- Not a bug, but worth calling out: **the tool never restarts or resets `SampleTargetApp`
  between runs** — it automates whatever's already running. Traced a report of a replayed test
  "showing previous data right after a tab switch, before any typing happened" to exactly this:
  running one test, then running another against the *same* still-open target instance without
  closing it in between, leaves whatever the first test typed sitting in those fields until the
  second test's own steps overwrite them. Confirmed the replay engine itself is correct — reran
  the same test against a freshly-launched target and checked every field's actual value after
  every step; nothing stale, nothing appended, exact values only. Close and relaunch
  `SampleTargetApp` before a run if a clean slate matters.
- `TypeText` originally wrote a field's full value in one `ValuePattern.SetValue` call, which is
  reliable but visually instant — watching a replay, the text just appears rather than looking
  typed, which read as "the text never gets written, it just pops up." Confirmed step order and
  field targeting were never actually wrong (traced a full before/after snapshot of every field
  and the active tab around every step of an 8-step test against a fresh target — text only ever
  appeared exactly on its own step, never early, never on the wrong field) before concluding the
  complaint was about the *pacing*, not correctness. Changed `ActionExecutor.ExecuteTypeText` to
  call `SetValue` once per additional character (35ms apart) instead of once for the whole
  string — still exactly as reliable, since every call is still a UIA property write and never a
  simulated keystroke, just paced to look like typing instead of appearing instantly.
- A real data-loss bug, found via a scripted repro: if the target app closes while a just-typed
  field's live capture is still "pending" (no focus change or Finish click has finalized it yet),
  the edit was silently discarded — not even saved with an empty value — and if it was the only
  thing captured so far, Finish & Save stayed permanently disabled with no way to save anything
  else in the session either. Root cause was two-fold in `LiveRecorder`: (1) the debounce-timer
  focus check held its lock during a live COM property read, which can stall for a dead target
  and blocks `Flush()` (called from Finish & Save) from ever running; (2) once finalization did
  run, the failed re-resolution against a dead window silently swallowed the edit entirely. Fixed
  by moving the COM reads outside their locks (in both the focus check and the checkbox-toggle
  poller, which had the same pattern), snapshotting each field's value as it's typed so there's a
  fallback if the live re-read fails at finalization time, and surfacing an explicit "a pending
  edit was lost" status message instead of silence when a value truly can't be recovered.
  `Finish & Save` is now enabled as soon as recording starts (not just once a step exists), since
  it's also what flushes a pending capture — so the button is never stuck unusable.
- The most significant bug found this way: typing into one field, then switching tabs before
  that field's capture had a chance to finalize (a completely normal recording flow: fill Name
  and Email, then move to the next tab) could silently drop the edit entirely — reliably, with
  the target app fully healthy the whole time, no crash involved. Root-caused by driving
  `LiveRecorder` directly and logging every capture with a timestamp: `TryCapture`'s finalization
  step re-resolves the field against a *fresh* walk of the UI tree, and WinForms genuinely removes
  an inactive `TabPage`'s controls from that tree the moment you switch away — not just marks them
  offscreen, they're unwalkable — so by the time the tab-switch's own handler flushed the pending
  capture (which happens right after the switch, since the event only fires once the switch has
  already taken effect), the field to re-resolve was already gone. No amount of "search harder"
  or "also check offscreen elements" fixes this, since the element isn't in the tree to find at
  all by then. Fixed at the source instead: `LiveRecorder` now builds the full `Locator` eagerly,
  in `OnValueChanged`, while the field is still guaranteed present — not deferred to finalization
  time — so finalizing later needs no re-resolution and can't fail this way. Verified with a
  before/after trace of the exact reported sequence (switch tabs with no data yet, type Name and
  Email, switch tabs again, type more) end-to-end through the real GUI: all 6 steps captured in
  the correct order with exact values, and the saved test replayed cleanly with no premature data
  anywhere. This also explains why some earlier "sees data before it should" reports weren't fully
  resolved by the app-not-reset-between-runs explanation alone — this bug could produce that same
  symptom (or a missing/misordered step) even on a freshly-restarted target.
- A recorded test (`test4.json`) replayed correctly for its first 6 steps, then failed trying to
  toggle a checkbox that only exists on the Coverage & Review tab — while the recording was still
  sitting on Vehicle Details. The saved file had "Toggle chkRoadside" as step 7 and "Select
  Coverage & Review" as step 8 — swapped from the order replay actually needs. Root cause: step
  order is assigned as `_steps.Count + 1` at the moment each capture *finishes processing*, not
  at the moment the underlying action actually happened — and different capture paths finish at
  different speeds (a manual "Add Step" click is synchronous and immediate; a live tab-switch is
  captured via a UI Automation event whose COM delivery latency isn't bounded). If the checkbox
  was toggled via the manual picker right after a live tab-switch click, the manual capture can
  win the race and end up numbered first even though the switch happened first chronologically.
  Fixed this specific file directly (swapped the two steps, verified 8/8 passes cleanly). The
  underlying race in the recorder itself is a known, harder architectural fix — reordering
  capture-time processing to key off when the action was *detected* rather than when it finished
  being handled — noted as follow-up work rather than made in-session, since it touches every
  capture path for a same-session-only edge case with a low-risk, one-line workaround (record a
  little slower right after a tab switch, or fix the file order directly as done here).

## What I'd do next with more time
A scoped low-level mouse hook (hit-tested against actionable elements) specifically to close the
button-click gap, since global UIA events don't cover it; broader control support (ComboBox/
ListBox); a proper retry/backoff policy instead of a single fixed poll; multi-window support;
and a small integration test suite that drives SampleTargetApp headlessly in CI.
and an chorme extension to record the test steps from the browser and send it to the desktop app.

## Running it
```
dotnet build Assignment.sln
# Terminal 1:
SampleTargetApp\bin\Debug\net48\SampleTargetApp.exe
# Terminal 2:
cd AutomationTool && dotnet run
# Click "Record New Test" (fill out the form normally — most steps capture themselves) or
# "Run Test" to replay a saved test from sample-tests\
```
