# Multiview Composer: "it just works" UX spec and fix proposal (DRAFT 2, advisor-reviewed)

Status: proposal for Mark's decision. Nothing here is built. Evidence: [2026-11-07-multiview-bandwidth-study.md](2026-11-07-multiview-bandwidth-study.md). Draft 2 folds in an independent review (Sonnet advisor, read-only, checked against the study and `src/`); 17 findings, all addressed below.

## 1. Problem in user words
Users expect every multiview they apply to work. Today a layout can be applied that overflows a transmitter (TX) or receiver (RX), and then **every** stream on that TX goes fuzzy, including original streams other displays are watching. The user cannot tell why, and the only known cure (delete other scaled streams) is invisible to them.

## 2. What exists today (verified in `src/`)
| Piece | Behaviour | Gap |
|---|---|---|
| `MultiViewBandwidthBudget` (commit fcf793c) | Pure arithmetic calibrated to the study | Used in one place only |
| `Orchestrator.WarnIfOverBudgetAsync` (`MultiViewOrchestrator.cs:913`) | On Take, checks the TXs and the RX of **that** layout; **log only**, never blocks; assumes stream 0 undivided; skips sources whose video cannot be read | Not system-wide, invisible to the user, cannot block |
| `MultiViewCanvasPolicy.EstimateGbps` | Window pixels × canvas fps × 24; halves via the stream-1 divider if > 9 Gbps | Ignores stream 0 and the TX total; uses canvas fps, but stream fps follows the source |
| `DraftValidator` | Blocks offline sources and a source used at another scaler size on another display | No bandwidth check |
| Divider code | Stream 1 only (`ReadStream1FrameDividerAsync`, `SetScalerFrameDivider`); divider is TX-wide shared state; `RestoreUnusedDividersAsync` only undoes dividers this app set | No stream-0 divider read or write exists |
| Surface plan | `SurfacePlanner.PlannedSource` defaults to stream 1: the app never subscribes stream 0 | Stream 0 tiles appear only in copied external layouts |
| Canvas | Comes from the display EDID (4K panel → 4K canvas) and is baked into layout names and redraw logic | Changing it changes saved layouts |

## 3. Design principles
1. The user states intent (layout, sources, display); the app owns scaler size, divider and tile quality.
2. **Never apply a layout the app predicts will break**, once the model is validated (rollout §6).
3. Check against the live system: other displays' **live** load plus this display's **new** layout (this display's old layout is replaced, not added to). Other displays' unapplied drafts are not counted.
4. Operators see "fits / tight / won't fit"; engineers can see numbers.
5. When the model cannot decide (video unreadable, layout made outside the app), say "can't check X" and warn; do not block and do not claim it fits.

## 4. Proposed changes

### 4.1 System-wide ledger
Inputs, all read from the Control Server, not from the app's memory:
- For every TX: its actual stream 0 and stream 1 state (running or stopped), source video, stream-0 and stream-1 divider values (including ones owned by others), scaler size. An idle running stream 1 counts (VEGA sent 1.57 Gbps with no subscriber); a stopped one does not (PTZ, Boom in the study).
- For every RX: all current subscriptions, both **plain routes and multiview layouts**, including ones set up in BAVM or on displays this app has not opened. Boom's 8.57 Gbps stream 0 reaches Left Wireless by a plain route, not a layout.
- Anything the app cannot read is shown as "unverified", never as zero.
- A divider the device refused (`DividerRefused`) is treated as full rate.
Status per device, proposed: **Fits** up to 8.5 Gbps, **Tight** 8.5–9.0 (amber note only), **Won't fit** above 9.0. The study's lab ran clean at 7.9 and a 9.1 stream worked; ~10 failed. Thresholds are to be tuned in rollout step 1.

### 4.2 Source eligibility is per use, not per source
A source is judged for the tile it would become, not globally:
- "OK as a 1080p tile" / "Not OK as a 4K tile" / "Offline" / "Can't read video".
- Reasons are plain: "Boom's 4K picture is compressed, so it can only be used as a 1080p tile, and only if its transmitter has spare capacity."
- Interlaced or 4:2:2 originals are flagged for the original-stream path only (the scaled stream always works at 24-bit progressive).

### 4.3 Stream choice
- **Today and rollout steps 1–2:** the app keeps using the scaled stream (stream 1) as it does now, at the single scaler size per TX that `DraftValidator` already enforces. No new capability.
- **Later, optional (step 4):** using a source's **original** stream as a big tile. It is only offered when the TX already sends an **uncompressed** stream 0 at 4K30 or below that the app can read (it cannot change stream-0 dividers today). Its cost is whatever stream 0 already is (VEGA 6.28 Gbps); it adds no scaled stream, but the TX total still counts it. This is a new capability, validated in test #9 only by hand through the API, not through the composer.
- Default tile: 1080p30 (stream fps follows the source; the divider halves it).

### 4.4a Canvas ladder: step down before refusing (supported by test, 2026-11-08)
Measured: a 4K PAX driven at 1080p60 with 960x540 tiles took 0.78 Gbps at the receiver instead of 3.13, and the picture was clean (Mark). BAVM offers the same choice in its per-receiver output-format dropdown.
Rule for the Apply gate (§4.5), replacing "refuse" as the first answer:
1. Try the display's preferred canvas (today's behaviour).
2. If the ledger says Won't fit, re-plan on the next canvas down (1920x1080, then 1280x720) and re-run the ledger; each step makes scaled streams smaller (window size = scaler size).
3. Apply the first canvas that fits, and tell the user in plain words: "Using a smaller picture area (1080p) so every source stays clear."
4. If none fits, refuse as in §4.5. Causes the ladder cannot fix: a transmitter over budget because of its original stream (e.g. Boom, 8.56 Gbps), or no more distinct streams (§9 #9).
Constraints: all layouts on a TX share one scaler size, so stepping one display down can conflict with another display using that TX at a larger size (the existing `SizeConflictOtherDisplay` rule applies; offer to step both). The ladder only offers timings the display lists in its EDID; BAVM's own filtering is unknown. Tiles get smaller, so the notice must say so; it is never silent.

### 4.4 Quality choice (after the model is validated)
Two separate ideas, not one:
- **Output (canvas) quality** stays the display EDID by default, so existing layouts and names do not change. A lower canvas is offered **only when the gate would refuse**, and only after the open question "can the RX output a smaller canvas on a 4K display" is answered (§9 #6).
- **Tile quality**: Standard (1080p30 tiles) is the default. A "High" preset (one 4K30 original-stream tile plus 1080p30 tiles) exists only once §4.3 step 4 exists.
- "Maximum 4K60" is removed: a scaled 4K60 stream is ≈ 12.8 Gbps and a compressed original stream cannot be a tile. A single-source 4K60 picture is a plain route, not a multiview.

### 4.5 Apply gate and messages
Before Take, run the ledger with this display's new layout replacing its old one.
- Fits: apply. Tight: apply with an amber note. Won't fit: do not apply (once step 3 enables the block; before that, warn only).
- Unverified part: apply, with "can't check <device>".
- **Apply anyway** exists for engineers (hidden behind the Details panel), as the spec requires an override.
Fixes offered must come from an explicit rule, never guessed: each candidate fix is re-run through the ledger and shown only if it then fits. Candidate fixes: remove a source from this layout; use a different layout; stop another display's use of the overloaded device (named, never done automatically); lower the canvas (only if §9 #6 is resolved).

Message patterns (plain words, device display names):
- "Boom can't be added here. It is already sending 4K to Left Wireless and has no spare capacity. [Remove Boom from this layout] [Choose another source] [Cancel]"
- "Pictures on <display> would break up. [Remove one source] [Choose a simpler layout] [Cancel]"
- "Couldn't check <source>: its video can't be read. Applied anyway."

### 4.6 Layout gallery
A layout that cannot fit the chosen sources is shown disabled with the same reason text. Order is unchanged (PiP, side-by-side, 2×2, 1+3).

### 4.7 Idle scaled streams (deferred, needs ownership first)
Idle running scaled streams cost TX egress. Stopping them can break BAVM, wall or Genlock users, and the app tracks only the dividers it set (`_halvedByUs`). So this is **not** part of the first rollout. It needs a persisted record of which streams the app started, and a rule never to stop others'. Until then, the ledger counts idle streams and the Details panel lists them for an engineer to stop.

### 4.8 Post-apply health check
After Take, read `netstat` for the TXs and RX involved: dropped-frame flags, RX ingress errors, CRC.
- Reading takes about 2 s per channel and one device at a time (study §3.1); the check runs in the background and reports "Checked" or "Couldn't check".
- A drop flag latched before this Take (VEGA's in the study) is not a failure: record the state before Take and judge only changes. Counters are not cleared automatically.
- On a real problem: "<display> may be breaking up" with [Revert]. Revert can fail, and editing a surface in place can leave a sticky surface that needs a power cycle (study §9). So: "Couldn't restore the previous picture; <display> may need a power cycle."
- Netstat channel selection is switched on only for devices in the layout and switched back after.

### 4.9 Engineer view
A collapsible Details panel: per-TX and per-RX Gbps (S0, S1, total), compression flags, divider owner and state, unverified items, model version, and the "Apply anyway" button. Same arithmetic as `multiview_bandwidth_model.py`.

## 5. Behaviour examples
| Case | Outcome | Basis |
|---|---|---|
| PiP with VEGA 4K30 original stream + PTZ 1080p30 scaled, and 2×2 with VEGA + PTZ scaled on another display | Works; VEGA 7.9, PTZ 4.7 | Built by hand through the API (study §10), **not** through the composer; becomes a composer case only after §4.3 step 4 |
| Add Boom scaled stream while Boom's 4K60 original stream feeds a display | Refused; Boom 9.3 Gbps | Measured |
| 2×2 with four 1080p60 sources on a 4K60 canvas | Works today (the code already halves the sources); not an example of the new gate | Code |
| Source offline, or video unreadable | Offline: existing handling. Unreadable: "can't check", apply | Code, §3 #5 |
| Layout made in BAVM on another display | Counted as "unverified" if unreadable, otherwise counted | §4.1 |

## 6. Rollout (health check before the block)
Every step sits behind a setting `MultiView.BandwidthGuard` with values Off / Warn / Block. Default **Off** until validated, then **Warn**. It is stored with the app settings and applies to all displays.
1. **Ledger + Details panel + visible warning** (Warn). No behaviour change beyond showing text. Log predicted vs measured Gbps per Take (fields: displays, TX/RX, predicted S0/S1, measured from netstat, flags).
2. **Post-apply health check** (§4.8), because the ledger can say "fits" and the picture can still fail (study §9, field-notes PiP asymmetry).
3. **Block** (§4.5), enabled only after steps 1–2 show the model agrees with netstat on the lab.
4. Optional later: original-stream big tile (§4.3), lower-canvas offer (§4.4), idle-stream ownership (§4.7).
No step hides existing controls until the model is validated.

## 7. Acceptance criteria and test matrix
- With the guard on Block, a layout predicted over 9 Gbps on any TX or RX is not applied without "Apply anyway".
- TX and RX **totals** within 5 % of netstat on the lab for PiP, 2×2, side-by-side and 1+3 combinations, excluding sources flagged as unexplained (Boom's scaled stream measured 0.78 vs 1.6 predicted; only PiP and 2×2 were run so far, 1+3 and side-by-side still need runs).
- No change in behaviour for layouts that fit, with the guard Off or Warn.
- Every refusal names devices by display name and offers at least one fix that the ledger re-checked.
- Tests to add: two displays sharing a TX; a refused divider; a layout set outside the app; an unreadable source; a TX with an idle running stream 1; a TX going offline mid-Take; partial failure after scalers changed (the existing scaler-restore path); a latched drop flag before Take; Revert failing.
- Existing `DraftValidator` and Take tests keep passing.

## 8. Not in scope
Switch configuration, firmware changes, new SDVoE features, Composer icon work, any hiding of controls.

## 9. Open questions and risks
| # | Question | Effect if wrong |
|---|---|---|
| 1 | Does the frame divider work on AVP2000T/TR units? The study saw VEGA (AVP2000T) with dividers active | Stream-0 path (§4.3 step 4) unavailable on some units |
| 2 | Compression-version mismatch TX vs RX on FW 2.x | Compressed streams could look bad within budget; not covered |
| 3 | Switch model, IGMP snooping, unregistered-multicast flooding | A budget that fits could still break pictures |
| 4 | Boom scaled stream 0.78 vs 1.6 predicted | Model over-counts some sources; fine for a conservative gate |
| 5 | Thresholds 8.5 / 9.0 Gbps | Chosen from a few lab points; tune in step 1 |
| 6 | Can the RX output a canvas smaller than the display's native size? **Tested yes** on a 4K PAX (1080p60 canvas, clean, 2026-11-08); other display models untested | Ladder in §4.4a may fail on a display whose EDID lacks 1080p or 720p; check the EDID list first |
| 9 | One receiver's multiview refuses the same (source, stream) in two windows (`Source stream is duplicated`, measured) | Layouts need distinct streams; the ledger and gallery must count a source once per receiver, and disable layouts that need more distinct streams than the sources offer |
| 7 | Field-notes PiP asymmetry (60 Hz big window fails near 7 Gbps) not explained by bandwidth | Ledger can say "fits" while the picture fails; the health check is the safety net |
| 8 | Layouts made in BAVM or on unopened RXs | Ledger incomplete; shown as unverified |
