### Task 3: Display card icons to Material

**Files:**
- Modify: `DisplayCardView.xaml` (header edit/info, copy-source and lock icons, check marks in the layout menu, monitor placeholder), `Resources/Styles/DisplayCard.xaml` (`Style.Icon.Button*` Path styles)
- Test: `ViewSmokeTests.UiV2DisplayCard.cs` additions; a guard test below

**Interfaces:** Consumes `MaterialIcon`.

- [ ] **Step 1: Write the failing tests:** `DisplayCard_UsesNoFluentOrHandDrawnIcons` (no `SymbolIcon` and no `Icon.<name>` Path except `Icon.Logo.*` in `DisplayCardView` visual tree; every `MaterialIcon.Kind` resolves); `DisplayCard_IconSizes_FollowTokens` (header icons 16, action icons 20).
- [ ] **Step 2: Run to verify fail. Step 3: Implement** (replace per the icon map). **Step 4: Run to verify pass** (`ViewSmokeTests|UiV2Guards`). **Step 5: Commit** `feat(display-card): Material icons`.

