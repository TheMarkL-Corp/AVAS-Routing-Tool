# Device Rename and Advanced Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Operators rename TX sources and RX displays from the Main Window (the name is written to the BlueRiver Control Server), and an "Advanced" tab, hidden by default, carries AVAS maintenance: link mode, temperature, reboots and factory resets.

**Architecture:** Two independent parts on branch `ui-v2/advanced` (cut from tag `ui-v2-round2-cp3b`). Part R (rename) touches the Main Window card files and is built by the Lane A lead. Part A (Advanced page) is mostly new files and is built by a nested session. Both share `SdvoeClient`; each adds methods in its own partial file (`SdvoeClient.Rename.cs`, `SdvoeClient.Maintenance.cs`) so the two parts never edit the same lines.

**Tech Stack:** .NET 8 WPF MVVM, WPF-UI 4.3.0, xUnit STA view tests, BlueRiver REST (port 8080) and Telnet (port 6970).

**Spec:** the **Decisions** section below (the user's answers, 2026-11-03). Wrapper reference code: `C:\Users\POC-615\Documents\GitHub\AVAS-BRV-wrapper-win\src\AvasRoutingApp\Sdvoe\` (`SdvoeClient.cs` lines 1150-1320 reboot/factory, `MultiLinkService.cs`, `TemperatureService.cs`, `DeviceTemperatureInfo.cs`).

## Decisions (fixed by the user; do not reopen)

- **D-R1** No local alias. The Control Server holds the only name. The card shows the new name only after the server confirms and discovery returns it. A failed write keeps the old name and shows a banner "Couldn't rename <old name>: <reason>".
- **D-R2** A visible pencil icon on every TX card header and RX card header opens a rename dialog. Rules: 1-32 characters, no leading or trailing spaces, no duplicate of another device name (case-insensitive). OK stays disabled with a one-line reason until valid.
- **D-A1** The tab is gated by `ShowAdvanced` in `appsettings.json` (default `false`), toggled by a Settings switch, wired like `ShowLogs`. Switching it off while the tab is open returns to Visual Routing. No PIN.
- **D-A2** Two stages. Stage 1 is read-only plus the link switch (temperature, link mode, link switch). Stage 2 is the four destructive actions (chip reboot, MCU reboot, MCU factory reset, chip factory reset), each behind a confirmation naming the device; both factory resets also need a typed confirmation.
- **D-A3** A link switch is allowed while the device is routed. The confirmation lists what is affected and offers "Unroute and switch" or "Cancel". After the switch is applied, unroute that TX from every multi-view (single-view routes stay), then wait for rediscovery. Nothing is re-routed automatically.
- **D-A4** Destructive actions apply to AVAS devices only (`VendorId 105`). MCU commands go through chip 0.

## Global Constraints

- WPF-UI controls only; vector icons; touch targets 48 px (36 px compact); font floor 12 px; accent roles: blue = selection/primary, red = errors only, amber = staged only.
- Status is a dot plus a word, never colour alone.
- Every destructive action sits in a "Danger zone" group, is never the primary button, and names the device in its title.
- Subagents are Haiku (escalate to Sonnet after two failures), never Opus. The lead diff-reviews every task and records red-then-green evidence.
- Commands come from the wrapper or the SDVoE Developer API. Where this plan marks a command "verify", the first task proves it on the API docs and a live device before any code uses it.
- Test command: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/<lane>/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" --blame-hang-timeout 120s -- RunConfiguration.DisableParallelization=true`. A known flaky STA test: rerun once before treating a failure as real.
- No merge to `main`, no push.

## Review Focus

Each line has its test in the owning task.

1. Rename while the device is routed or in a multi-view: names are identifiers in routing lookups; routes survive the rename. (R4)
2. Rename when the Control Server is unreachable or rejects the name: old name stays, banner appears. (R2, R4)
3. Names differing only by case or by trailing space, and a name equal to the device's own current name. (R3)
4. Link switch on a dual-link TX whose companion chip is a separate discovered device. (A5)
5. Temperature missing (`null`): the row shows "n/a", never 0. (A3)
6. A destructive action on an RX or a non-AVAS device is refused. (A6-A9)
7. `ShowAdvanced` switched off while the Advanced tab is open. (A1)

---

## Part R: Rename (Lane A lead)

### R1: Prove the rename write path

**Files:** Create `docs/superpowers/specs/2026-11-03-rename-api-findings.md`.

**Completion criterion:** the findings file holds the exact request (Telnet command or REST call), the response, and a read-back from `get all identity` showing the new `device_name`, from one TX and one RX on the lab Control Server. If no write path exists, stop and report to the master; no rename code is written.

- [ ] **Step 1:** Search the SDVoE Developer API docs (use the `sdvoe-blueriver-avas-expert` skill) for the call that sets `device_name`; list candidates.
- [ ] **Step 2:** Try each candidate against the lab server, rename a device, read it back, rename it back.
- [ ] **Step 3:** Write the findings file and commit.

### R2: `SdvoeClient.RenameDeviceAsync`

**Files:** Create `src/AvasRoutingTool/Sdvoe/SdvoeClient.Rename.cs`; test `tests/AvasRoutingTool.Tests/SdvoeClientRenameTests.cs`.
**Interfaces:** Produces `Task<RenameResult> RenameDeviceAsync(string mac, string newName)` where `RenameResult` holds `bool Ok` and `string? Reason`.

- [ ] **Step 1:** Write tests against the fake REST/Telnet server: `Rename_SendsFindingsCall`, `Rename_ServerRejects_ReturnsReason`, `Rename_ServerUnreachable_ReturnsReason`. Run: red.
- [ ] **Step 2:** Implement `RenameDeviceAsync` using the call recorded in R1. Run: green.
- [ ] **Step 3:** Commit.

### R3: `NameRules.Validate`

**Files:** Create `src/AvasRoutingTool/ViewModels/NameRules.cs`; test `NameRulesTests.cs`.
**Interfaces:** Produces `static string? Validate(string name, string currentName, IEnumerable<string> otherNames)`; `null` means valid, otherwise the one-line reason.

- [ ] **Step 1:** Tests: empty, 33 chars, leading space, trailing space, duplicate differing only by case, equal to `currentName` (valid), 32 chars (valid). Red.
- [ ] **Step 2:** Implement. Green. Commit.

### R4: Pencil, dialog, and refresh

**Files:** Modify `Views/SourceCardView.xaml`, `Views/DisplayCardView.xaml`, `ViewModels/SourceCardViewModel.cs`, `ViewModels/DisplayCardViewModel.cs`; create `Views/RenameDialog.xaml(.cs)`; test `ViewSmokeTests.UiV2Rename.cs`.
**Interfaces:** Consumes R2, R3. Produces `RenameCommand` on both card view models.

- [ ] **Step 1:** Tests: pencil is 48 px and in tab order with spoken name "Rename <device>"; dialog OK disabled with reason for each invalid case; success shows the new name only after rediscovery; failure keeps the old name and shows the banner; a routed device keeps its route after rename. Red.
- [ ] **Step 2:** Implement pencil (icon-only, neutral, blue on hover/focus, tooltip "Rename"), dialog (48 px field, name pre-selected, Enter confirms, Esc cancels, buttons "Rename" / "Cancel"), command, banner. Green.
- [ ] **Step 3:** Shoot main-window 01 and 02 plus the dialog, both themes and sizes, into `ui_screenshots/UI-v2-advanced/rename/`. Run the full suite. Commit.

---

## Part A: Advanced page (nested session)

### A1: `ShowAdvanced` setting and tab

**Files:** Modify `Configuration/AppConfig.cs`, `appsettings.json`, `ViewModels/MainViewModel.cs`, `MainWindow.xaml(.cs)`, `Views/SettingsDialog.xaml(.cs)`; create `Views/AdvancedView.xaml(.cs)` (empty shell); test `ViewSmokeTests.UiV2AdvancedTab.cs`.

- [ ] **Step 1:** Tests: default `false` in both code and `appsettings.json`; tab hidden when off; shown when on, placed after Logs; switching off while open returns to Visual Routing. Red.
- [ ] **Step 2:** Mirror the `ShowLogs` wiring (`MainWindow.xaml.cs` lines 47 and 68). Green. Commit.

### A2: Device rows

**Files:** Create `ViewModels/AdvancedViewModel.cs`, `ViewModels/AdvancedDeviceRowViewModel.cs`; modify `Views/AdvancedView.xaml`.
**Interfaces:** Produces `AdvancedDeviceRowViewModel` with `Name`, `Mac`, `Role` (TX/RX), `LinkMode`, `TemperatureText`, `IsAvas`.

- [ ] **Step 1:** Test: one row per discovered device, AVAS flag from `VendorId 105`, rows follow discovery refresh. Red, implement, green, commit.

### A3: Temperature (read-only)

**Files:** Create `Sdvoe/TemperatureReader.cs`; test `TemperatureReaderTests.cs`.
**Interfaces:** Produces `Task<int?> ReadAsync(string mac)`. Thresholds from the wrapper: warning 61 degC, critical above 70 degC. Port the REST read from `TemperatureService.cs` (`/api/device`).

- [ ] **Step 1:** Tests: value parsed, missing value gives `null`, row shows "n/a" for `null`, state word "Normal / Warm / Hot" beside the dot. Red, implement, green, commit.

### A4: Link mode status (read-only)

**Files:** Create `Sdvoe/LinkModeReader.cs`; test. Port the read side of `MultiLinkService.cs` / `MultiLinkInfo.cs` (`LinkMode`, `CompanionMac`, `Capabilities`).

- [ ] **Step 1:** Tests for SINGLE, DUAL, UNKNOWN. Red, implement, green, commit.

**Stage 1 gate (link switch comes after this review):** shoot the Advanced tab (both themes, 1920x1080 and 1024x720) into `ui_screenshots/UI-v2-advanced/stage1/`, run the full suite, report to the master. The user reviews before A5.

### A5: Link switch

**Files:** Create `Sdvoe/SdvoeClient.Maintenance.cs` (`SetLinkModeAsync`), `Views/LinkSwitchDialog.xaml(.cs)`; test.
**Interfaces:** Produces `Task<bool> SetLinkModeAsync(AvasDevice tx, LinkMode target)`. Port `set:multi_link` then the primary-chip reboot from `MultiLinkService.cs` near lines 255-300 (REST `/api/device/<mac>`, `op reboot`).

- [ ] **Step 1:** Tests: dialog lists routed RXs and multi-view layouts using the TX (D-A3); "Unroute and switch" stops them first; after apply the TX is unrouted from every multi-view and single-view routes stay; the row shows "Rebooting" until rediscovery; Cancel changes nothing. Red, implement, green.
- [ ] **Step 2:** Measure the reboot wait on the lab device and record it in the findings file. Commit.

### A6-A9: Destructive actions (stage 2)

Each task: add one method to `SdvoeClient.Maintenance.cs` ported from the wrapper, one confirmation dialog, one view-model command, tests. Order: A6, A7, A8, A9.

| Task | Method | Command (wrapper source) | Confirmation |
|---|---|---|---|
| A6 | `RebootChipAsync(device, mac)` | Telnet `reboot <mac>`, REST fallback `/api/device/<mac>` `op reboot` (`SdvoeClient.cs` near line 1250) | "Reboot <device>?" |
| A7 | `RebootMcuAsync(device)` | `send <chip0Mac> rs232:1 set system reboot` (near line 1300) | "Reboot the MCU of <device>?" |
| A8 | `FactoryResetMcuAsync(device)` | `send <chip0Mac> rs232:1 set factory default` (near line 1225) | typed device name |
| A9 | `FactoryResetChipAsync(device, mac)` | Telnet `factory <mac>`, REST fallback `op factory_default` (near line 1155) | typed device name |

- [ ] **Step 1 (each):** Tests: refuses non-AVAS and RX where the command is TX-only; MCU commands target chip 0; dialog title names the device; typed-confirm button stays disabled until the name matches exactly; success and failure show a banner. Red, implement, green, commit.
- [ ] **Final step:** shoot stage 2 into `ui_screenshots/UI-v2-advanced/stage2/`, run the full suite, report. Live checks on a real device (a person at the machine) are listed in `docs/superpowers/specs/2026-11-03-advanced-live-test.md`, written in A9.
