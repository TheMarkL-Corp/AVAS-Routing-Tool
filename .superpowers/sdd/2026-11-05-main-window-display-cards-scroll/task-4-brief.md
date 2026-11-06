### Task 4: Drag versus pan, and keyboard

**Files:**
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs` only if the drag threshold needs it
- Test: Main Window smoke test

- [ ] **Step 1: Write the failing tests:** `Touch_Tap_OnCard_StillArms`; `Keyboard_Tab_ToCardOffscreen_ScrollsItIntoView` (focus a card beyond the right edge, assert it is inside the viewport).
- [ ] **Step 2: Run to verify fail,** then **Step 3:** fix (`BringIntoView` on card `GotKeyboardFocus`; keep the existing copy-drag threshold, pan only on a drag that starts outside the copy handle). **Step 4:** pass. **Step 5:** commit `fix(main-window): tap, drag and keyboard focus work in the sideways row`.

