### Task 3: Horizontal scroller with arrow buttons

**Files:**
- Modify: `src/AvasRoutingTool/Views/VisualRoutingView.xaml` (`DisplaysScroll`, lines 215-234) and `VisualRoutingView.xaml.cs`
- Modify: `src/AvasRoutingTool/Resources/Styles/Buttons.xaml` (one `Style.Button.CarouselArrow`, 48 x 48, round, icon only)
- Test: the Main Window smoke test that builds `VisualRoutingView`

**Interfaces:**
- Produces: `VisualRoutingView.ScrollDisplaysBy(int cards)` (positive = right, one step = one card width plus gap, animated or snapped; clamps at both ends). Arrow buttons `DisplaysScrollLeft` and `DisplaysScrollRight` named for tests.

- [ ] **Step 1: Write the failing tests:** `Displays_FiveAt1920_HorizontalScrollOnly` (`HorizontalScrollBarVisibility=Hidden/Auto`, vertical `Disabled`, `ScrollableWidth > 0`); `Displays_Arrows_StepOneCard_AndDisableAtEnds`; `Displays_FourAt1920_ArrowsHidden`; `Displays_MouseWheel_PansSideways`.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement:** `HorizontalScrollBarVisibility="Auto"` (thin bar, existing theme), `VerticalScrollBarVisibility="Disabled"`, `PanningMode="HorizontalOnly"`; arrows overlay the left and right edges (48 px, vertically centred on the pictures, visible only when scrollable, disabled at the ends via `ScrollChanged`); `PreviewMouseWheel` turns wheel delta into horizontal offset.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(main-window): displays pan sideways with arrow buttons`.

