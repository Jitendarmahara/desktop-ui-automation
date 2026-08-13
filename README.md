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
