# UI v2 critique, round 2 (Video Routing + MultiView Composer)

Scope: Visual Routing main window and MultiView Composer. Settings and BAVM console are out of scope.
Evidence: `ui_screenshots/UI-v2/main-window/01,02` and `ui_screenshots/UI-v2/composer/a,b` at 1920x1080 dark; competitor reference `competitor_screens/jones-av/MyOR-03.jpg` (the second file is an identical copy).
Lenses: design-critique framework; design-taste-frontend app-screen rules (quiet borders, no glow, no rainbow, no tinted icon squares, fill the frame, one primary action per view).
Not measured: contrast ratios and live-app behaviour. Contrast claims below are visual estimates and must be measured before being treated as fact.

## What the competitor does that we do not

| Competitor (Jones AV MyOR) | Us today |
|---|---|
| Each display tile shows its real picture, with four layout presets (single, PiP, side-by-side, quad) as one-tap icon buttons directly under it | Layout is a dropdown (`Layout v`) plus a `Copy layout` button, two taps and a menu |
| Display name sits above the tile as plain text | Name, info icon, status pill, layout badge all packed in a card header |
| Source tray is one dense bottom strip of named thumbnails | Sources are two big 420px cards, centred, black preview, with empty bands left and right |
| Empty display reads "Ready to route a source" with the brand mark in the tile | Large black box, grey monitor icon, two lines of 12px grey text |
| One calm surface, one accent (cyan), soft raised buttons, big labelled left rail for modes | Navy with blue, cyan, teal, green, red and yellow all in play |
| Utility actions (clock, volume, screen cleaning) at top right, large | Dark / Refresh / Settings at top right, same weight text |

## Main window findings

| # | Finding | Severity | Recommendation |
|---|---|---|---|
| M1 | Arming a source inserts a 64px banner and pushes the displays down, which makes a vertical scrollbar appear (shot 02). The layout moves under the user's finger at the moment they are about to tap a target. | Critical | Reserve the banner row permanently or overlay it. Nothing may move when a source is armed. |
| M2 | Source row is centred with two cards; the left and right thirds are empty. Black previews dominate. | Moderate | Left-align into one dense thumbnail strip (competitor tray) sized by count; show the last frame or a name plate when the preview is idle. |
| M3 | Layout needs a dropdown. Gloved operators need a layout change in one tap. | Moderate | Inline row of four preset icon buttons under each display (reuse `LayoutSchematic`), `Copy layout` moves into an overflow or sits at the end of the row. |
| M4 | `TAP OR DRAG TO ROUTE` chip repeats on every source card; `SOURCE: NONE` plus a second line `... shows nothing to copy` repeats on every idle display. | Moderate | Say it once in the section hint. Idle footer shows `Ready to route a source` only. Drop the "nothing to copy" line; disable Copy instead. |
| M5 | Accent overload: cyan ring (armed), blue ring (selected display), teal banner, green ONLINE pill, blue DUAL LINK pill, blue DISCOVERED pill, red power, yellow elsewhere. Selection and status are conflated. | Moderate | One accent (selection/armed). Status is a dot plus plain text, not a coloured pill. Counters (`DISCOVERED: 2`) become plain text. |
| M6 | Section headers are 12px caps with a grey hint on the same line; hierarchy between section title, card title and hints is flat. | Minor | Section title at card-title size in sentence case, hint below or dropped. |
| M7 | Empty display tile is large (about 580x330) and carries almost no information. | Minor | Show the display's name plate and a dashed drop target with the brand mark and "Ready to route a source". |
| M8 | Top bar: `Connecting...` grey dot has no failure/success distinction visible; Dark, Refresh, Settings are equal text buttons. | Minor | Icon buttons with tooltips for Refresh and Theme; connection state as a coloured dot with a label only on problems. |

## Composer findings

| # | Finding | Severity | Recommendation |
|---|---|---|---|
| C1 | Step 1 canvas is a 1140x635 black void with 13px text in the middle. | Moderate | Preselect the last-used layout, or draw a ghost schematic that follows the hovered layout tile. Never a blank canvas. |
| C2 | Two primaries compete: cyan `Done placing` above the canvas and blue `Take` in the rail. | Moderate | `Done placing` becomes secondary (outline); Take stays the only filled button. |
| C3 | Colour overload in the staged state: orange dashed frame, cyan block outlines, blue Take, yellow `STAGED` and `CHANGES`. | Moderate | Staged = one amber frame only; block outlines neutral until armed; cyan reserved for the armed block. |
| C4 | Right rail has a dead zone between `Changes` and the banner/Take cluster, and `ON SCREEN NOW` is a black box with 12px text when nothing is routed. | Moderate | Stack Changes under the preview and let it grow; put the banner above Take only when it has content. Empty preview shows the display name plate. |
| C5 | `Changes (1)` reads `1: - -> TX1_c07bb_SFPA`, which is cryptic. | Moderate | `Block 1: empty -> TX1_c07bb_SFPA`. |
| C6 | Left rail steps 2 and 3 are instruction text, not controls, and take 130px of the rail. | Minor | Fold them into the headings of the canvas and the Take area; keep the rail for layouts only. |
| C7 | Source strip: four cards leave the right 40% empty and the gradient test pattern reads as decoration, not as a signal. | Minor | Same dense strip as M2 so both screens share one source component. |
| C8 | Overlay labels on blocks (`Replace with TX2_DUAL LINK_SFPA`) are long and sit on busy video. | Minor | Scrim behind the label; truncate the name with the full name in the tooltip. |

## Accessibility (estimates, measure before acting)

- Grey hint text on navy looks near 4:1; likely below 4.5:1 at 12px. Run the contrast check across `Brush.Text.Secondary` and `Brush.Text.Muted` on every surface in both themes.
- Touch targets look right (48px, 36px compact); the 36px strip stays an accepted exception.
- Colour is carrying meaning (green/blue pills, cyan/amber outlines). Each state also needs a word or an icon (M5, C3).

## What works well

- Block numbers at 28px, the staged/on-screen split, and the Take/Undo flow are clear and match how the room is actually used.
- Display cards show the real multi-view layout with source names on each block, which is the competitor's best idea and we already have it.
- Spacing, rails and button sizes are now consistent across both screens; theme switching is centralized.

## Priority order

1. M1 (layout shift on arm). Quick, high impact, safe.
2. M3 plus M4 (inline layout presets, de-duplicated microcopy).
3. M5 plus C3 plus C2 (one accent, one primary): a token-level change in `Resources/Styles`, applied to both screens.
4. M2 plus C7 (one shared dense source strip).
5. C1, C4, C5, C6 (Composer canvas and rail).
