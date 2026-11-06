### Task 1: Material icon pipeline and the `MaterialIcon` control

**Files:**
- Create: `tools/icons/import-material-icons.ps1`, `src/AvasRoutingTool/Resources/MaterialIcons.xaml`, `src/AvasRoutingTool/Controls/MaterialIcon.cs`, `THIRD-PARTY-NOTICES.md` (or append if present)
- Modify: the resource dictionary merge list where `Icons.xaml` is merged (find it in `App.xaml`)
- Test: `tests/AvasRoutingTool.Tests/MaterialIconTests.cs`

**Interfaces:**
- Produces: `Geometry` resources keyed `MIcon.<name>` (24 x 24 coordinate space) for every name in the icon map; `Controls.MaterialIcon : Control` with `string Kind` (dependency property), `Foreground` (inherited), default `Width`/`Height` `Size.Icon.M`; looks up `MIcon.<Kind>` from resources; throws `InvalidOperationException` naming the Kind when the resource is missing.

- [ ] **Step 1: Write the failing tests** in `MaterialIconTests`: `EveryMapIcon_HasAGeometryInA24Box` (each name in the map: resource exists, `Bounds` inside 0..24 with at most 1 px of overshoot, non-empty); `MaterialIcon_UnknownKind_ThrowsWithTheKindName`; `MaterialIcon_FollowsForeground_InBothThemes` (render, read the fill brush from the template path); `MaterialIcon_SizeComesFromWidthAndHeight`.
- [ ] **Step 2: Run to verify fail:** `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r3/ --filter "FullyQualifiedName~MaterialIconTests" -- RunConfiguration.DisableParallelization=true`. Expected: FAIL (type and resources missing).
- [ ] **Step 3: Implement** the import script: downloads `https://raw.githubusercontent.com/google/material-design-icons/master/symbols/web/<name>/materialsymbolsrounded/<name>_24px.svg` for each name in the map, converts each path (viewBox `0 -960 960 960`) to a 24 x 24 `StreamGeometry` string (scale 1/40, y + 960 then /40), and writes `MaterialIcons.xaml`; commit the generated XAML and the downloaded SVGs under `tools/icons/svg/`. Implement `MaterialIcon` and merge the dictionary. Add the Apache-2.0 notice.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(icons): Material Symbols Rounded geometry set and MaterialIcon control`.

