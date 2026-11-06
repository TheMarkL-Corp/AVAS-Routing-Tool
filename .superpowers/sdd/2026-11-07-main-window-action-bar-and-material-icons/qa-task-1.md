# QA review: Task 1 (Material icon pipeline and MaterialIcon control), c5c2e63..3cbfe49

Read-only review: no build, no tests run, no git changes. Static read of the diff, App.xaml, TestResources.cs, UiV2ThemeBridgeTests.cs, csproj, Tokens.xaml; one PowerShell probe (Geometry.Parse returns a frozen StreamGeometry).

**Spec verdict: OK**
**Quality verdict: Changes needed** (two small Important items; nothing Critical)

## Spec compliance
- 22 icons: the name list in the script and in `MapNames` equals the plan's icon map (22 distinct names, checked one by one). All are `Geometry` keys `MIcon.<name>`, parsed as `StreamGeometry`, prefixed `F1` (NonZero, asserted per icon), coordinates inside 0..24 (asserted with 1 px slack).
- Control API: `Kind` DP (string), `Foreground` via TemplateBinding to the inherited `Control.Foreground`, default Width/Height by `SetResourceReference(..., "Size.Icon.M")` (22 in Tokens.xaml), unknown Kind throws `InvalidOperationException` naming the Kind and the resource key (MaterialIcon.cs:178-179), decorative peer (Image, not control/content element until `AutomationProperties.Name` is set; not hit-testable, not focusable, not a tab stop).
- Import script: skips existing SVGs (idempotent), `-Force` refreshes, the only host is raw.githubusercontent.com/google/material-design-icons, rejects other viewBoxes and `evenodd`. Output is deterministic (fixed name order, LF, no BOM).
- Notice: THIRD-PARTY-NOTICES.md names source, copyright, Apache-2.0 and the derivation (see Minor 4 on "shipped").
- `tools/` sits at the repo root, outside `src/AvasRoutingTool`, so the SDK globs do not pull it into the build output.
- Dictionary order: `MaterialIcons.xaml` is merged after `Theme.xaml` and before `Icons.xaml` in App.xaml and in `TestResources.Merged`; `UiV2ThemeBridgeTests` still sees `Icons.xaml` last (line 70). `MaterialIconTests` swaps `MergedDictionaries[2]`, which is `Theme.Dark.xaml` (verified against `Merged`).

## Converter review (tools/icons/SvgPathConverter.ps1)
Correct on a read-through:
- Absolute commands translate y by +960 then /40; relative ones only scale; H/h x only, V absolute translates, v scales.
- Arc: rx, ry, x scale; rotation and flags are untouched; y translates only for absolute `A`. Compact flags (`011`) work because flags are read as single characters.
- The first `m` is promoted to `M` and its implicit repeats stay relative `l` (lines 576, 617). A later `m` after `Z` stays relative. Implicit repeats re-emit the command letter.
- Number regex handles `.5-.5`, `1.5.5`, `10-5` and exponents (`1e-3`). Unknown letters throw; numbers without a command throw.
- Output rounding to 4 decimals; source values are multiples of 1/40 for integer input, so no visible drift. -0 is normalised to `0`.
- Smell, not a bug: it shares `$script:d/n/pos` with the dot-sourcing script (the import script also has a `$d`); it is overwritten each loop, so harmless today.

Untested by the unit tests (no real icon uses them, per the report: only H L M T Z h l m q t v appear): exponent numbers, `.5-.5` forms, `S/s`, `Q`, `C` implicit repeats, `Z` followed by relative `m`, arc with decimals. The existing test covers abs/rel `M L H V Z A`, compact arc flags and the first-`m` rule.

## Findings

### Important
1. **MaterialIcon.cs:128-129, local value shadows Styles.** `SetResourceReference(WidthProperty/HeightProperty, "Size.Icon.M")` in the constructor sets a local-priority value. A `Style` or `Setter` that sets `Width`/`Height` on `MaterialIcon` (for example a `Style.Icon.Section` or `Style.Icon.Action` targeting `MaterialIcon`, which Task 4 lists) is silently ignored; only an inline attribute wins, and Style/Trigger/VisualState changes of size cannot work. Failure scenario: Task 4 writes `<Style TargetType="ctl:MaterialIcon" x:Key="Style.Icon.Section"><Setter Property="Width" Value="{StaticResource Size.Icon.S}"/>...` and every section icon stays 22 px with no error; the screenshot is the only signal. Fix: give the control a default style (`DefaultStyleKey` + a Generic/implicit style or a style registered in MaterialIcons.xaml with `Width`/`Height` `DynamicResource Size.Icon.M`), which sits below Style and local values; add a test that a Style with Width 16 applies.
2. **Test gaps on the control's runtime behaviour (MaterialIconTests.cs).** Nothing changes `Kind` after the template is applied, nothing re-tints one live instance when the theme dictionary is swapped (the test builds a fresh icon per theme), and nothing asserts the geometry is shared/frozen. The theme toggle in Task 4 depends on exactly the first of these. Add: `Kind = "light_mode"` then `"dark_mode"` on a hosted icon and assert `PathOf(icon).Data` is `FindResource("MIcon.dark_mode")` (reference-equal, which also proves sharing and frozenness); and swap the palette under the same icon and assert the fill follows.

### Minor
3. **Silent blank for empty Kind** (MaterialIcon.cs:176): `Kind=""`/null (unset, or a binding that resolves to null) renders nothing and never throws, which is the "silent blank icon" the plan wants to avoid for typos. Consider throwing for an empty Kind when the control is loaded, or at least testing that an unset Kind is intentional.
4. **"Shipped" notice**: THIRD-PARTY-NOTICES.md exists at the repo root only; no packaging script in the repo copies it, there is no About text, and Apache-2.0 section 4(a) asks for a copy of the licence text, while the file gives only the URL. Add it to the release package checklist (release-default-settings memory item) and consider embedding the licence text.
5. **Generated XAML is not verified against its sources**: no test regenerates MaterialIcons.xaml from `tools/icons/svg/*.svg` (or compares), so the "idempotent, do not edit by hand" claim is untested; a hand edit or stale SVG is not detected. `MapNames` is also a second hand-kept copy of the script's list.
6. **Script sources `master`** (import-material-icons.ps1:665) and records no upstream commit; `-Force` later can silently change shapes. Pin a commit SHA in the URL and in the notice. Under Windows PowerShell 5.1 `Invoke-WebRequest` may need TLS 1.2 forced (the header says `pwsh`).
7. **Foreground test is partly tautological** (MaterialIconTests.cs:321-344): `expected` is read from the same Application after the swap, so if the swap silently failed the Dark/Light halves would still pass. The inherited-Red half and the dark/light colour difference carry the real value; asserting that Dark and Light expected colours differ would close it. Index-based `MergedDictionaries[2]` is brittle (a reorder breaks it quietly).
8. **Converter tests spawn powershell.exe** (4 process starts, about a second each; single-quoted path breaks if the repo path contains an apostrophe). Acceptable for a Windows-only app. A C# port (shared converter class in a tools project or the test assembly) would be better only as a Minor: in-process unit tests with many cases (arcs, exponents, S/Q/C) and no host dependency, and the generator could be tested against the committed XAML.
9. Performance note, no action: geometry resources are shared (default `x:Shared`) and `Geometry.Parse` output is frozen. Each icon costs Control + Viewbox + Canvas + Path; fine for the tens of icons on the Main Window. No `SnapsToDevicePixels`/`UseLayoutRounding` handling is added (same as today's hand-drawn paths); judge crispness in Task 5 screenshots.

## Process: would the committed tests catch a broken control or geometry?
Caught (expected to fail):
- Removing the `MIcon.` lookup throw, or returning null for unknown Kind (UnknownKind test).
- Wrong scale (for example /960 or missing /40: bounds test requires width and height > 6) and missing y translation (bounds leave 0..24).
- Dropping the `F1` prefix (FillRule assertion; the rendered hole test also guards the info ring and the gear ring).
- Fill baked to a colour or `TemplateBinding` removed (Foreground tests, including the inherited Red case).
- Removing the default size reference (default icon would be 24, not 22), removing the Viewbox (drawn bounds exceed 16), making the icon hit-testable/focusable/always exposed to UI Automation.
- First-`m` rule, relative/absolute scaling and compact arc flags in the converter (known-path tests); unknown command (loud-failure test).
- A name missing from the dictionary (`EveryMapIcon`).

Would be missed:
- Removing `OnKindChanged` (runtime Kind change): the template is applied on first read after `Kind` is set, so no test notices. (Important 2.)
- A style-priority regression such as Important 1.
- A single wrong sub-path or one mis-converted relative segment that stays inside 0..24 (only the 2 rendered hole samples and the manual contact sheet cover shapes); S/Q/C implicit-repeat bugs; exponent number parsing.
- Drift between SVGs, script output and MaterialIcons.xaml.
- Theme swap on a live instance; geometry not shared or not frozen.

The missing clean RED run is a process gap but not a quality gap here: the committed tests would fail against an absent type/resources (compile error) and against most of the mutations above, so the evidence is equivalent in effect; the implementer's account (first run failed three ways, then GREEN) is plausible given the tests exercise real behaviour.
