# Multiview without breaking streams: bandwidth and admission study

Date 2026-11-07 · Revision 3 (checked against the PDFs in `docs/sdvoe_api_docs` and calibrated with a third-party field-notes table, §9) · Branch `claude/multiview-bandwidth-optimization-20d30b`.
**Study only: no source, project or device setting was changed.** Live access was read-only: `GET /api/device/ALL`, `GET /api/multiview/layout`, `POST /api/device/{mac} {"op":"get","subset":"settings"}`, and `netstat read` (a measurement; it changes nothing) against the Control Server at `:8090`.
Calculator: [`multiview_bandwidth_model.py`](multiview_bandwidth_model.py).

Labels: `documented` (page cited), `observed-live` (this lab today), `inference`, `pending-validation`. Source IDs: **API** = PDS-062489 Rev 3.9.0.0, **APP** = PDS-062491 Rev 2.9, **INTRO** = PDS-062488, **QSG** = PDS-062568 (AV Manager Quick Start) , **ADM** = PDS-062492. Page numbers are PDF pages.

---

## 1. Answer first

The "everything goes fuzzy until I delete scaled streams" problem is a **bandwidth budget problem on a transmitter or receiver 10G link**, and the vendor states exactly two rules the app does not fully enforce today [APP §6.3.2.4 p.33]:

1. Everything a decoder (RX) receives for a layout, plus audio and auxiliary data, must fit its 10G link.
2. Native stream + scaled stream + other data leaving an encoder (TX) must fit its 10G link.

The app's guard checks neither correctly (§4). Ranked causes:

| # | Cause | Evidence | Confidence |
|---|---|---|---|
| H1 | **TX egress over budget when stream 1 is added to a 4K60 source.** A 4K60 native stream is compressed to "close to 8.5 Gbps, leaving little room" [APP §6.3.2.3 p.33]; adding a 1080p30 scaled stream (1.61 Gbps) gives ~10.2 Gbps. | Vendor figure, plus live: Boom_Camera_A 4K60 → Left Wireless TX measures **8.57 Gbps** at the RX. VEGA_1100 reports `hdmi_stream_0` **dropped frames = true** on its TX egress (§3). | high that the budget is exceeded by design; the drop flag is `observed-live` but undated |
| H2 | **Unregistered-multicast flooding at the switch**: each started stream 1 then loads every port, which fits "deleting scaled streams fixes everything". Requires filter/drop of unregistered multicast [APP §2 p.11]. | Pattern only; switch unknown. | `pending-validation`, needs switch access |
| H3 | **RX ingress over budget** from too many or too large scaled sources. | Needs > 5 distinct 1080p30, or > 2 at 1080p60, on one decoder (§5). Not reachable with 3 transmitters. | low in this lab, real in bigger systems |
| H4 | **Compression version mismatch → macroblocking on the original stream.** | The docs contradict each other (§3.3); mismatched pairs exist in the lab. | open; test, don't assume |

Not yet reproduced: nothing was in multiview when sampled.

---

## 2. Live inventory (`observed-live`)

| Device | Role / chip | Input now | Stream 0 | Stream 1 | Compressor / decompressor |
|---|---|---|---|---|---|
| VEGA_1100 | TX · AVP2000T · vendor 192 · FW 2.1.0.0 | 3840x2160@**60** RGB 8-bit | `224.1.1.3` STREAMING, **reaches RX as 3840x2160@30** | `224.1.1.5` STREAMING, 1080p reporting 30 fps | v1 |
| Boom_Camera_A | TX · AVP2000 | 3840x2160@60 YCbCr **4:2:2 12-bit** | `224.1.1.4` STREAMING (compressed, §3) | stopped; scaler 1080p30 4:4:4 | v1 |
| PTZ_Camera_SFPA | TX · AVP2000 | 1920x1080@60 RGB | `224.1.1.2` STREAMING | stopped; scaler 0x0 | v2 |
| PAX 327 Left, PAX_AVAS_Right | RX · AVP2000 | none, frame buffer 3840x2160@60 | | | v2 |
| zzPresentation | RX · AVP2000 | `224.1.1.3` (VEGA), 1080p60 output | | | v1 |
| Left Wireless TX (an RX) | RX · AVP2000 | `224.1.1.4` (Boom), 4K30 output | | | v1 |

All ports run 10 Gb/s (`NETWORK_PORT`). Switch model, uplinks and VLANs are not visible to the API. 14 layouts are stored on the server, some for MACs now offline. Offline devices also reported by netstat: PTZ Camera (…07bc), Boom Camera_B, PAX355S, A35, RX1_c059e, F8228500FFFF.

---

## 3. What the documents and the live counters show

### 3.1 Netstat read gives a direct, per-stream answer
`POST /api/device/{ALL_TX|ALL_RX|mac} {"op":"netstat","option":"read"}` returns asynchronously (fetch `GET /api/request/{id}`) [API §4.12.2 p.49]. A TX reports bandwidth per channel: `total`, `hdmi_stream_0`, `hdmi_stream_1`, audio, `usb_extender`, `network_1g`, plus `dropped_frames` flags per channel and `ethernet_flow_control_frames` [API Tables 21-25 p.51-53]. An RX reports ingress `total` and CRC/PHY error counters [API p.56]. Each channel measurement takes 2 s, one at a time, only for **selected** channels [API §4.12.1 p.48].

Today's read (`observed-live`):
- **All TX bandwidth channels are deselected**, so TX usage reads 0. Selecting them is `netstat select` [API §4.12.4 p.66], which writes a mask on the device, so it needs your approval.
- **VEGA_1100: `dropped_frames.hdmi_stream_0 = true`**. Boom_Camera_A: `network_1g = true` (1G data path, probably unrelated). PTZ: none. The documents say "at least one frame was missed" and do not say whether the flag is cleared on read or latched since boot (up ~6 h), so it shows loss has happened at some point on VEGA stream 0, not when.
- No flow-control frames, no PHY/CRC/frame errors on any TX or RX ingress. The links themselves are clean.
- RX ingress `total`: zzPresentation 792,096,404, Left Wireless TX 1,070,618,386. Units are not stated in the extracted text. Read as **bytes/s** they equal **6.34** and **8.57 Gbps**, which match independent calculations (4K30 RGB 5.97 Gbps ÷ 0.93 = 6.42; vendor "close to 8.5 Gbps" for compressed 4K60). Read as bits/s the numbers would be implausible for 4K. `inference`, strongly supported.

### 3.2 The model was wrong on blanking; corrected here
My first draft (and the Handover note on BAVM) used blanking-inclusive rates. The vendor says one 1080p60 RGB stream uses "approximately 3.2 Gbps" [APP §6.3.2.1 p.32]. Active pixels give 2.99 Gbps; ÷ 0.93 gives **3.21**. So the model is `wire = active pixels × fps × bpp ÷ 0.93`, no blanking (0.93 reproduces the field-notes table exactly, §9; the live 4K30 reading fits 0.94 slightly better, so treat efficiency as 0.93 to 0.94). The old 9.0274 Gbps figure is not in any supplied document; the documented limit is **9 Gbps** per TX and per RX [API §5.1.1 p.145; compression is applied "to keep the video bandwidth under 9Gbps", API Table 186].

### 3.3 Compression: what is certain and what is not
- Native 4K60 8-bit RGB / 4:4:4, 4K60 10 and 12-bit 4:2:2 are compressed automatically [APP §4.2 p.20]. Both 4K60 sources here qualify. A compressed stream 0 **cannot** be used by an RX in multiview, and multiview needs progressive full-chroma sources [INTRO §3.2 p.29; API §5.1.1]. Boom is 4:2:2 and 4K60, so it can only be a multiview tile via **stream 1**.
- **Conflict:** APP p.20-21 says ASIC FW ≥ 1.5.0.1 on both ends gives automatic decoding of either version, and in the next lines warns that an RX must be set to the same version as the TX or "macroblocking is likely". All devices here run FW 2.1/2.2, yet versions are mixed (Boom/VEGA v1 vs PAX v2; PTZ v2 vs zz/Left Wireless v1). The documents do not say which statement wins on FW 2.x. **Resolve by test**, not by assumption.
- Stream 1 is always uncompressed, 8-bit, progressive, and keeps the source colour model (4:2:2 becomes 4:4:4) [APP §6.3.2.2 p.32-33].
- The vendor's own remedy for a 4K60 TX is the **2x frame-rate divider on stream 0, stream 1 or both** [APP §6.3.2.3 p.33]. VEGA already shows stream 0 arriving at 4K30, so a divider seems active on it. The data sheet says the AVP2000T cannot divide on its HDMI input (per the source register, not re-read), so confirm which mechanism produced it.

### 3.4 Switch requirements the symptoms point at
IGMPv2 snooping, **filter/drop of unregistered multicast**, Fast Leave on endpoint ports; some L2 switches reportedly fail the unregistered-multicast filter [APP §2 p.11]. Check `status.igmp_reporting_mode` on devices (`QUERY` on the Boom sampled).

---

## 4. Why the app's check misses this
`MultiViewCanvasPolicy.EstimateGbps` multiplies **window pixels × canvas fps × 24** for one display. Reality, per the vendor rules:

| Quantity | Real driver | App models |
|---|---|---|
| TX egress | stream 0 (compressed or not) + **one** stream 1 at its single scaler size and fps + audio/aux | stream 1 only, via window area (Handover/04 §4.2 item 6) |
| RX ingress | sum of **distinct subscriptions** (one source in several windows through one surface = one subscription) | sum of window area, fps = canvas fps |
| Switch links | unique multicast flows per link | not modelled |
| Stream 0 in multiview | compressed → unusable | not checked |

It therefore overestimates repeated sources and ignores stream 0, which is the bigger share on a 4K60 TX.

---

## 5. Budget model (calibrated; see calculator)

`wire = w × h × fps × bits-per-pixel ÷ 0.93`; compressed ≈ wire ÷ 1.5. Limit 9.0 Gbps per TX and per RX, audio/aux/USB extra.

| Scaled stream (24 bpp) | Gbps |
|---|---|
| 720p30 | 0.71 |
| 1080p30 | 1.61 |
| 1080p60 | 3.21 |
| 4K30 | 6.42 |
| native 4K60 compressed | 8.56 (vendor ≈ 8.5, measured 8.57) |

**Max distinct sources into one decoder:** 720p30 → 12 · 1080p30 → **5** · 1080p60 → **2** · 4K30 → 1.

**TX egress, this lab:**

| TX | S0 | S1 (1080p30) | Total | Verdict |
|---|---|---|---|---|
| VEGA_1100, native 4K60 | 8.56 | 1.61 | 10.2 | **over** |
| VEGA_1100, stream 0 at 4K30 (as seen now) | 6.42 | 1.61 | 8.0 | ok |
| Boom_Camera_A, native 4K60 | 8.56 | 1.61 | 10.2 | **over** |
| PTZ, 1080p60 | 3.21 | 1.61 | 4.8 | ok |

Reading: Boom is the unprotected case. If a Take starts its stream 1 while its stream 0 runs at 4K60, it overruns the link; this fits the "both original and scaled go fuzzy" report. Verify with §7.

**Not in the sources, so not claimed:** switch buffer depth, IGMP group scale, oversubscription tolerance, optic types. A 10G Bandwidth Usage Calculator is referenced [APP §4.1 p.20] but was not in the folder.

---

## 6. Admission rules (the "proper check")

Evaluate against **live state** before every Take, route or stream start:

1. **TX egress**: `S0 + S1 + audio/aux ≤ 9 Gbps`. S0 from the live input format and compressor state (`HDMI_DECODER`, `VIDEO_COMPRESSOR`) or from a measured `hdmi_stream_0`; S1 from its scaler size × its fps. If over: shrink S1 → ÷2 divider on S1 → ÷2 on S0 (only if no Genlock/wall user needs it) → lower the source format.
2. **RX ingress**: sum of distinct subscriptions across **all** layouts and routes on that decoder (not only the draft) `≤ 9 Gbps`.
3. **One scaler size per encoder, system-wide** (already enforced by `DraftValidator`); size it to the largest window that truly needs it, and prefer 1080p30 for a ¼-of-4K tile.
4. **Never use a compressed or non-4:4:4 stream 0 as a tile**; use stream 1.
5. **Compression-version check** as a warning until §7 test #3 settles whether it matters on FW 2.x.
6. **Start stream 1 only for a subscriber**, and stop it when the last subscriber leaves; orphan scaled streams are the first suspect for H2.
7. **Order operations**: remove load (leave, stop) before adding load; wait for the 2 s netstat window, then read the TX/RX `dropped_frames` and RX `total` and **roll back the Take** if a drop flag is set or the measured total exceeds budget.
8. **Fabric**: sum unique flows per switch link once switch/uplink data exists; otherwise label results "fabric unverified".

---

## 7. Controlled test plan (needs your authorization)

| # | Step | Mutates? | Rollback |
|---|---|---|---|
| 1 | `netstat select` `["total","hdmi_stream_0","hdmi_stream_1"]` on the three TX, then `netstat read` while idle | mask only | `select NONE` |
| 2 | Baseline: clear nothing; read drop flags before and after each step below | no | n/a |
| 3 | Route Boom (v1) alone to PAX 327 Left (v2) at 4K, look for macroblocking, repeat after matching versions | route | `leave` |
| 4 | Start Boom stream 1 (1080p30) while stream 0 runs 4K60; read `hdmi_stream_0/1` and `dropped_frames` | stream | `stop` stream 1 |
| 5 | Same with ÷2 on Boom stream 0; then ÷2 on stream 1 | scaler/divider | restore |
| 6 | Three-source quad on PAX 327 Left, then the same on PAX_AVAS_Right; read RX ingress | layout | delete layout, `leave` |
| 7 | Switch: confirm IGMP snooping, drop unregistered multicast, Fast Leave, querier, per-port drop counters | no | n/a |
| 8 | Reproduce the field-notes PIP asymmetry: 4K60 source in the big window and 4K30 source small, then reversed, on PAX 327 Left | layout | delete layout, `leave` |

`netstat clear` is not needed and should be avoided (it resets the evidence). Run only one TX channel group at a time.

**Needed from you:** switch model and port map; the 10G Bandwidth Usage Calculator; confirmation of where the old "9.0274 Gbps" came from.

**Product follow-ups (nothing started):** replace `EstimateGbps` with the per-TX/per-RX model above, add a stream 0 + stream 1 TX guard, read netstat after a Take and roll back on drops, surface a compression-version warning. Do it on a separate branch with tests; hardware validation is the gap the Handover already flags.

## 8. External research
The web turned up little beyond the vendor documents: Semtech confirms a native plus a scaled stream within 10G and a decoder compositing up to 32 sources [Semtech multiviewers blog](https://blog.semtech.com/blueriver-applications-part-3-video-walls-multiviewers); SDVoE switches must run full 10G line rate with IGMPv2 and a reliable querier [Christie switch requirements](https://preproduction.christiedigital.com/globalassets/resources/public/terra-sdvoe-network-switch-configuration--connectivity-requirments.pdf), [SDVoE four commandments](https://sdvoe.org/the-four-commandments-of-an-sdvoe-network-switch/); microbursts overflow shallow switch buffers when several ports feed one egress, even at low average rate [Juniper note](https://community.juniper.net/blogs/parthipan-ts/2025/08/22/microburst-detection-and-avoidance-on-qfx5k). General networking, not SDVoE-measured.

## 9. Third-party field notes (user-provided, source unverified)

A colleague's lab notes (Traditional Chinese, translated here; author, hardware and firmware unknown, so `user-provided`, not `observed-live`). They matter because they contain **failure** data the documents lack.

**Bandwidth table (Gbps).** 720p60 4:4:4 = 1.43 · 1080p60 4:2:2 = 2.14 · 3840x2160 60p 10-bit 4:2:0 = 8.03 · 4096x2160 60p 8-bit 4:4:4 = 9.13 with compression at maximum.
The model `pixels × fps × bpp ÷ 0.93` reproduces the first three **exactly** and the fourth at a **1.5:1** compression ratio, which also reproduces the live Boom reading (8.57). This is the best calibration available: **efficiency 0.93, compression 1.5:1**, replacing my earlier 0.94 and 1.4:1 guesses. A native 4096x2160 60 source alone is therefore 9.13 Gbps, over the 9 Gbps guideline with nothing else on the link.

**Test results and what the model says about each:**

| Field result (notes) | Model total | Explained? |
|---|---|---|
| TX native 3840 + divider, stream 1 1920 + divider → RX Fast Switch shows up to 3840p60. **Without a divider the link overloads and the picture is abnormal.** | divided 6.42 + 1.61 = 8.0 ok · undivided 8.56 + 3.21 = 11.8 over | yes |
| Native 3840 + divider, **stream 1 3840 + divider** → abnormal, line-skipping (RX 4K30) | 6.42 + 6.42 = 12.8 over | yes |
| Native with divider but no link, stream 1 1920 + divider → normal | stream 1 alone | yes |
| Native not sent, stream 1 3840 → OK | one stream | yes |
| Native 3840 (4K60) + stream 1 720x480 → Fast Switch, Genlock, Genlock-Scaling all fine | 8.56 + ~0.5 ≈ 9.1 | yes; shows ~9.1 Gbps **still works** |
| Multiview PIP: TX1 (4K60 source) big + TX2 (4K30 source) small → **TX1 abnormal, TX2 fine**; TX2 big + TX1 small → fine (RX 4K60) | about 7 Gbps either way | **no** |

Reading: failures at about 10 to 12.8 Gbps and a pass at about 9.1 put the real cliff between 9.1 and 10, consistent with a hard 10G link minus overhead and with 9 as the safe design limit. The PIP result is **not a bandwidth problem** (both arrangements are about 7 Gbps); it depends on which source is in the large window, and the 60 Hz source fails as the big window. Candidate causes: frame-rate mismatch between a divided 60 Hz stream 0 and the 4K60 frame buffer, or how a divided stream 0 is handled in the large surface. `pending-validation` (test #8); the budget model does not cover it.

**Operational findings to build around:**
1. **Sticky multiview surface.** In a PIP layout, after the background used a 4K stream 0 and was then changed to a 720p stream 1, the old stream 0 stayed on screen. Device reset and factory default did not clear it; only **re-powering the RX** did. Do not switch a live surface between stream 0 and stream 1 in place: tear the layout down (`leave`, delete) and rebuild, and treat a power cycle as the last resort. This may be part of "it only recovers when I delete things".
2. **Transceiver (TR) mode has no frame-rate divider**, and a decoder inside a TR cannot show its own encoder's video in video-wall or multiview. This matches the data-sheet note on the AVP2000T. VEGA_1100 is an AVP2000T (reported `device_mode` TRANSMITTER) yet its stream 0 arrives at 4K30, so whether a divider is really active there stays open.
3. Stream 1 as large as stream 0 is almost never right: with dividers on both it still overflows. Size scaled streams to the window, not to the source.

**Added admission rules:**
- Treat a native 4096x2160 60 source as stream-0-only unless a divider is applied.
- Require a divider (or a smaller stream 1) before adding stream 1 to any compressed stream 0; for 4K60 sources prefer ÷2 on stream 0 over a bigger stream 1.
- Never put stream 1 at 4K, even divided, next to a 4K stream 0.
- Rebuild layouts instead of editing a surface's stream index in place.
- Do not apply a divider to a device reporting transceiver mode.

## 10. Theory check: 4K big frame + 1080p small frame while other displays run 1080p scaled layouts

Theory (yours): PiP with frame 1 at 4K and frame 2 at 1080p stays usable while other displays run 2x2 and side-by-side from 1080p scaled streams. **Verdict: sound for the receivers, and for the transmitters only under two conditions.** Not tested on hardware (`inference` from the calibrated model).

**Why it mostly holds.** Multicast means a transmitter sends each stream once however many displays join it. Extra displays add **no** transmitter load and cannot overload each other's receiver; each receiver only counts its own subscriptions.

| Display | Subscriptions | Receiver ingress | Ok (≤ 9)? |
|---|---|---|---|
| PiP: 4K30 big + 1080p30 small | 6.42 + 1.61 | **8.03** | yes, little margin |
| PiP: 4K30 big + 1080p60 small | 6.42 + 3.21 | 9.63 | **no** |
| PiP: 4K30 big + 720p60 small | 6.42 + 1.43 | 7.85 | yes |
| 2x2, four 1080p30 | 4 × 1.61 | 6.42 | yes (this lab has 3 sources: 4.8) |
| Side-by-side, two 1080p30 | 2 × 1.61 | 3.21 | yes |

**Condition 1: the 4K frame must be 4K30 or lower.** A stream 1 is always uncompressed, so 4K60 cannot be a stream 1 (12.8 Gbps). A 4K60 stream 0 is compressed and unusable in multiview. So the big frame is either a stream 0 that is uncompressed full-chroma 4K30 (a divided 4K60 source), or a stream 1 at 4K30 or less. This also keeps the field-notes PIP failure (a 60 Hz source as the big window, §9) out of the first test.

**Condition 2: one scaler size per transmitter.** Each transmitter has one stream 1 at one size. If the PiP's 4K source is also a source the other displays show at 1080p, you cannot have both sizes:
- Feed the big frame from that source's **stream 0** (4K30) and leave its stream 1 at 1080p for the other displays. Transmitter egress is 6.42 + 1.61 = **8.03 Gbps**, fits. This is the intended design, and VEGA_1100 already runs that way (stream 0 arrives at 4K30).
- If instead the 4K frame uses a 4K30 stream 1, the other displays also receive that 4K stream: a 2x2 with one 4K30 and three 1080p30 is 11.24 Gbps, **over**.

**Where the lab breaks it.** Boom_Camera_A is 4K60 **4:2:2**, so its stream 0 cannot be a multiview tile at all (multiview needs full chroma) and its compressed stream 0 is 8.56 Gbps; a 4K30 stream 1 on top is ~15 Gbps. Boom cannot be the 4K frame while its stream 0 runs, and cannot share a 1080p stream 1 with other displays at the same time. Either set its source to a 4:4:4 4K30 output, or keep Boom as a small-frame/1080p source only. PTZ (1080p60 RGB) is fine as the small frame at 1080p30 (use a divider on its stream 1).

**Design rules that follow:**
1. Pick one shared stream 1 size per transmitter (1080p30 here) and make every display that uses its scaled stream accept that size.
2. A 4K frame comes from an uncompressed 4K30 stream 0 of one designated source, never from a bigger stream 1.
3. Keep the small frame at 1080p30 or 720p60; 1080p60 pushes the PiP receiver past 9 Gbps.
4. A small PiP window does not need 1080p: if no other display depends on that transmitter's 1080p, a 720p stream 1 saves 0.9 Gbps at the receiver.
5. Do not change the frame-1 stream index on a live PiP (sticky-surface finding, §9); rebuild it.

**Test #9 (needs authorization):** VEGA as the 4K30 stream-0 big frame and PTZ as the 1080p30 small frame on PAX 327 Left; then start a 2x2 on PAX_AVAS_Right from VEGA, PTZ and Boom stream 1 at 1080p30. Read `netstat` on VEGA (`hdmi_stream_0`, `hdmi_stream_1`, `dropped_frames`) and RX ingress on both displays. Expect VEGA ≈ 8.0 and PiP receiver ≈ 8.0 Gbps, no drop flags. Rollback: delete both layouts, `leave` both receivers, `stop` stream 1 on each transmitter.

### Test #9 result (run 2026-11-07, `observed-live`; picture quality not yet confirmed by eye)
Setup: PAX 327 Left = PiP, big window 3840x2160 from VEGA **stream 0** (divided, 4K30 RGB), small window 1920x1080 at bottom-right from PTZ **stream 1** (1080p30, divider on). PAX_AVAS_Right = 2x2, VEGA stream 1 + PTZ stream 1 at 1080p30, two black windows. Both on at once. Counters on the three transmitters were cleared first.

| Measured (Gbps, bytes/s × 8) | Predicted | Measured | Drop flags |
|---|---|---|---|
| VEGA TX egress (S0 + S1) | 8.0 | **7.90** (6.28 + 1.57) | none |
| PTZ TX egress (S0 + S1) | 4.8 | **4.72** (3.15 + 1.57) | none |
| Boom TX egress (S0 only, still feeding Left Wireless) | 8.56 | **8.57** | none |
| PAX 327 Left ingress (PiP) | 8.0 | **7.90** | none, CRC 0 |
| PAX_AVAS_Right ingress (2x2) | 3.2 | **3.13** | none, CRC 0 |

- The model is accurate to about 2 %, slightly pessimistic (real efficiency ≈ 0.95 on TX egress). The netstat unit is confirmed as bytes/s.
- Running both layouts together changed nothing on the transmitters: multicast added no load, as predicted. Condition 2 is satisfied (VEGA S0 for the big frame, VEGA S1 stays 1080p30 for the 2x2).
- VEGA's `hdmi_stream_0` drop flag, true before the test, did **not** return at 7.9 Gbps after the counters were cleared. So that earlier flag was latched from an earlier, heavier state (e.g. before the divider was on stream 0); it is not an ongoing fault.
- VEGA sends its stream 1 (1.57 Gbps) even with no subscriber, so an idle scaled stream still costs transmitter egress.
- Picture quality: Mark confirmed both screens looked clean (user-reported, not measured).

### Boom stream-1 case (run 2026-11-07, `observed-live`)
Boom's scaler was already 1080p and its divider already on; only `start` stream 1 was needed. Boom kept its 4K60 compressed stream 0 running for Left Wireless TX.

| Boom TX egress (Gbps) | Predicted | Measured |
|---|---|---|
| Stream 0 (compressed 4K60) | 8.56 | **8.56** |
| Stream 1 (1080p scaled) | 1.6 (1080p30) | **0.78** |
| Total | ≈ 10.2 (over 9.0) | **9.30** (over 9.0) |
| Drop flag | expected | `network_1g` only; no 10G stream drop flag, pause frames 0 |

- The total is over the 9 Gbps vendor limit, so the rule holds: Boom cannot add a scaled stream while its compressed 4K60 stream 0 runs. The 10G drop flag did not appear within ~8 s, so the damage is not shown as a hard failure here; the limit is a margin, not a cliff. I did not look at pictures during this case.
- Stream 1 measured half the predicted size (0.78 vs 1.6 Gbps). Likely the source is 4:2:2 or the divider acts on stream 1 as well. Not explained; do not rely on it for budgeting.
- VEGA (7.90), PTZ (4.72) and both receivers (7.90, 3.13) were unchanged during the Boom case.

### Rollback (done 2026-11-07)
Both receivers returned to FAST_SWITCHED 3840x2160@60; Boom and PTZ stream 1 stopped with `free`; PTZ divider back to 0; layouts `study_pip_4k` and `study_2x2_1080` deleted (HTTP `DELETE /api/multiview/layout/{name}`); netstat selection cleared on VEGA, Boom, PTZ. The PTZ scaler stays 1080p (it was 0x0 before).

### Side-by-side and 1+3 (run 2026-11-07, `observed-live`; pictures not looked at)
Both on PAX_AVAS_Right (3840x2160@60 canvas), geometry from `MultiViewPresets` (side-by-side: two 1920x1080 at y=540; 1+3: Focus 2560x1440 plus three 1280x720). Counters cleared, 10 s window.

| Case | Predicted | Measured | Flags |
|---|---|---|---|
| Side-by-side: VEGA S1 + PTZ S1, both 1080p30 → RX ingress | 3.2 | **3.13** (VEGA 7.90, PTZ 4.72 total) | none |
| 1+3 with two live sources: PTZ S1 2560x1440@30 + VEGA S1 1280x720@30 → RX ingress | 3.6 | **3.50** (PTZ S1 2.80, VEGA S1 0.70; PTZ total 5.95, VEGA total 7.04) | none |
| 1+3 with all four windows live (extrapolated, **not measured**): 2.85 + 3 × 0.71 | 5.0 | n/a | n/a |

- Model error ≤ 2 % again (PTZ 2.80 vs 2.85, VEGA 0.70 vs 0.71). Both layouts are bandwidth-light compared with PiP; the limiting factor is the transmitter, not the receiver.
- **New rule, found by the API:** subscribing the same source stream to two windows of one receiver is refused: `"Source stream is duplicated"`. So one multiview can show each (source, stream) once. With three transmitters a full 1+3 needs 4 distinct streams, which this lab cannot make without Boom's stream 1 (over budget, see above) or stream 0 at a size that does not match the window. The 1+3 with four live tiles was therefore not run.
- Each transmitter has one scaler size, so 1+3 forced the PTZ to 2560x1440 and VEGA to 1280x720 while it ran; the layouts could not run at the same time as a 1080p layout using the same transmitters.
- Rollback (done): receiver back to Fast Switch 4K60, layouts deleted, PTZ stream 1 stopped with `free`, PTZ scaler 1080p and divider 0, VEGA stream 1 restored to its original state (1080p scaler, running, divider 1), netstat selection cleared.

### BAVM output-format selector (user screenshot, 2026-11-07)
The BAVM multiview page has a per-receiver output format dropdown: "EDID (Recommended)" or an explicit mode (720p50/60, 1080p24 to 120, 4K24 to 60, 4096x2160 and more); one receiver there was set to 3840x2160@60.
- Per SDVoE docs the receiver output format is a command argument, not something fixed by the display: `set:video`, `join` (optional) and `set:multiview` (`"video":{width,height,fps}`) all take it [INTRO §5.1 p.37, API §2.5 p.14]. "EDID (Recommended)" is the display's preferred timing, which is what our app already chooses (`MultiViewCanvasPolicy.Choose`).
- So **BAVM can run a multiview canvas lower than the panel's native size** (for example 1920x1080@30 on a 4K panel) and the app can too: `set:multiview` with a smaller `video`. The display, not the Control Server, must accept that timing; a 4K panel normally lists 1080p in its EDID. This answers open question #6 of the UX spec as "the receiver does output it", but it is **not tested on our lab displays**.
- Why it matters for bandwidth: canvas size and fps do not set the stream size (stream size = scaler size and source fps), but they set the layout's window geometry (window size = scaler size, so a smaller canvas means smaller windows and smaller scaled streams) and which surfaces are needed. A 1080p canvas makes 2×2 tiles 960x540 (0.4 Gbps each) instead of 1080p (1.6 Gbps each).
- Not found in the supplied docs: whether BAVM filters that list by the display's EDID. The screenshot list includes 1080p@120, so probably not filtered.

### Lower canvas test (run 2026-11-08, `observed-live`, picture confirmed clean by Mark)
PAX_AVAS_Right (a 4K panel) driven at **1920x1080@60** with a 2×2 of 960x540 windows (two live: VEGA S1 and PTZ S1 with 960x540 scalers, dividers on; two black).

| Measured (Gbps) | 4K canvas, 1080p tiles (side-by-side/2×2 tests) | 1080p canvas, 960x540 tiles |
|---|---|---|
| RX ingress | 3.13 | **0.78** |
| VEGA scaled stream / TX total | 1.57 / 7.90 | **0.39 / 6.73** |
| PTZ scaled stream / TX total | 1.57 / 4.72 | **0.39 / 3.55** |

- No drop flags, CRC 0, pause frames 0. Mark confirmed the picture is clean (user-reported).
- Confirms the lever: the canvas sets window size, window size must equal the scaler size, so a smaller canvas gives a proportionally smaller scaled stream. Stream bandwidth ≈ canvas pixels per tile × tile fps × 24 ÷ 0.93.
- It does **not** reduce a transmitter's original stream (VEGA 6.28, Boom 8.56), so it cannot fix a TX that is over budget because of stream 0.
- Rollback (done 2026-11-08): receiver back to Fast Switch 4K60, layout deleted, PTZ stream 1 stopped, scaler 1080p, divider 0; VEGA stream 1 restored (1080p, running); netstat selection cleared.

## Evidence gaps
Fuzzy state not reproduced · netstat units inferred · drop-flag latching unknown · version-mismatch effect on FW 2.x unresolved · switch unknown · AVAS-specific (L4) divider and compression behaviour is vendor-dependent. QSG (AV Manager) only says it uses the scaled stream for multiview and sets the frame-rate divider automatically; it states no bandwidth rule.
