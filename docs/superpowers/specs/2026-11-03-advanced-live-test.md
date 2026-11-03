# Advanced page: live checks on a real device

Everything in the test suite runs against fakes (a fake REST handler and a loopback Telnet server); no command has ever been sent to a real device by the tests. These checks are for a **person at the machine** with a lab Control Server and a spare AVAS device they can afford to reboot and reset.

**Before you start**

- Use a lab device, never a room in use. Note its name, MAC (chip 0 and, for a dual-link TX, chip 1) and current link mode.
- Settings: switch on **Show Advanced tab**, Save and Apply. The **Advanced** tab appears after Logs (and the app was started with `ShowAdvanced` false, so it was hidden before).
- Keep the BAVM console open in a browser to watch the device go offline and come back.

## 1. Read-only (stage 1)

| # | Do | Expect |
|---|---|---|
| 1.1 | Open the Advanced tab | One row per discovered device, TX first. The status is read when the tab opens. |
| 1.2 | Look at an AVAS TX row | Link mode "Single link" or "Dual link", a temperature with a dot and a word (Normal under 61 °C, Warm 61-70, Hot above 70). |
| 1.3 | Look at an RX row and a non-AVAS device | RX link mode "n/a"; the non-AVAS device is listed but is not queried and has no actions. |
| 1.4 | Unplug the device's sensor read, or pick a device that reports no temperature | The row says "n/a", never 0. |
| 1.5 | Switch Show Advanced off in Settings while the tab is open | The app returns to Visual Routing. |

## 2. Link switch (A5)

| # | Do | Expect |
|---|---|---|
| 2.1 | Press "Switch to dual/single link" on an unrouted TX, then Cancel | Nothing changes. |
| 2.2 | Route that TX to an RX (Single View) and into a multi-view on another RX, press the switch | The dialog names the TX and lists both displays. The buttons are "Unroute and switch" and "Cancel". |
| 2.3 | Confirm | The row shows "Rebooting". The multi-view loses the TX's block; the Single View route stays (its picture drops while the TX reboots). Nothing is routed again by itself. |
| 2.4 | Time how long the row takes to be listed again after Refresh | **Record the number here** (the plan's A5 step 2). The row keeps saying "Rebooting" until the device has left and come back, or 90 s pass; if the measured wait is longer than 90 s, raise `AdvancedViewModel.RebootTimeout`. |
| 2.5 | Check the link mode after it is back | It is the mode you asked for. On a dual-link TX the companion chip is listed as its own device. |
| 2.6 | Try to switch from the companion chip's row | There is no switch button on chip 1. |

Measured reboot wait: ______ s (device: ______, date: ______)

## 3. Danger zone (stage 2)

Choose the device in the Danger zone list; only AVAS devices are listed. Every button asks first, the confirm button is never the blue primary button, and the dialog title names the device. Cancel (or Esc) changes nothing.

| # | Do | Expect |
|---|---|---|
| 3.1 | "Reboot chip" on the lab TX, confirm | Title "Reboot <device>?". The banner says it is rebooting, the row shows "Rebooting", the device drops off in BAVM and returns. Routes to it are not changed. |
| 3.2 | "Reboot chip" on a lab RX | Same. |
| 3.3 | "Reboot MCU" on the lab TX (primary chip) | Title "Reboot the MCU of <device>?". The video keeps running; the MCU restarts (check with the device's serial/MCU status if available). |
| 3.4 | Choose a companion chip (chip 1) and press "Reboot MCU" or "Factory reset MCU" | Refused with a banner "MCU commands go through chip 0". Nothing is sent. |
| 3.5 | "Factory reset MCU", type the name wrongly (lower case, extra space, partial), then correctly | The Factory reset button is disabled until the name matches exactly; then it sends the reset. The MCU returns to factory settings. |
| 3.6 | "Factory reset chip", same typing check | Same. The chip restarts with factory settings (name, mode and routes are lost); the row shows "Rebooting". **Use a device whose settings you can restore.** |
| 3.7 | With the Control Server unreachable (stop it, or unplug the lab network), press "Reboot chip" and confirm | A banner "Couldn't reboot <device>. Nothing was changed." and no "Rebooting" state. |
| 3.8 | Select a non-AVAS device (a VEGA camera) | It is not in the Danger zone list. |

## Commands behind the buttons (from the wrapper, for the log)

| Button | Sent |
|---|---|
| Reboot chip | Telnet `reboot <mac>`; REST fallback `POST /api/device/<mac>` `{op: reboot}` |
| Reboot MCU | REST `{stream_type: rs232, stream_index: 1, op: switch:api}`, then `{op: send:rs232, port_index: 1, data_string: "set system reboot\r\n"}` to chip 0; Telnet fallback `send <chip0Mac> rs232:1 set system reboot` |
| Factory reset MCU | Same two steps with `set factory default` |
| Factory reset chip | Telnet `factory <mac>`; REST fallback `{op: factory_default}`, then `{op: factory}` |
| Link switch | REST `{op: set:multi_link, mode: SINGLE or DUAL}` to chip 0, then `{op: reboot}` |

The app log (`Logs` tab, category ADVANCED) records each command and a refusal reason.
