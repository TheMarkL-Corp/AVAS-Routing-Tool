### Task 5: Screenshots and the full suite

**Files:** Create `ui_screenshots/UI-v2-round3/` (`before-main-1920.png`, `after-main-1920-5displays.png`, `after-main-1920-12displays.png`, `after-main-1920-4displays.png`, `after-main-1024.png`).

- [ ] **Step 1:** Save the before screenshot; produce the after shots with the env-gated `AVAS_UIV2_SHOTS` renderer (add 4, 12 and 1024x720 cases if missing).
- [ ] **Step 2: Run the full non-live suite:** `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r3/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" --blame-hang-timeout 120s -- RunConfiguration.DisableParallelization=true`. Expected: 0 failed (rerun once for the known STA flake).
- [ ] **Step 3: Commit** `docs(ui-round3): main window display cards before and after screenshots`.

---

## Assumptions (tell me if wrong)

- The empty 64 px status row on idle cards stays as it is (locked chrome, stable height); you did not answer decision 2.
- The sources strip stays fixed above the displays; you did not answer decision 3. With one short row of displays it now has more room.
