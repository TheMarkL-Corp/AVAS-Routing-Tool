# Rename API findings (task R1)

Status: **the write path is documented and a candidate is chosen; the live write and read-back are NOT yet proven.** The live rename of lab devices was blocked by the session's permission classifier (it is a write to real devices), so no rename was sent. Everything below marked `documented` comes from the vendor PDFs, everything marked `observed` was a read-only check against the lab Control Server on 2026-11-03.

## The call (documented, API 3.9, layer L2)

Rename = a **set property** request on the key `configuration.device_name`.

REST (candidate chosen for `SdvoeClient.RenameDeviceAsync`):

```
POST http://<host>:8090/api/device/{target}
Content-Type: application/json

{ "op": "set:property", "key": "configuration.device_name", "value": "<new name>" }
```

- `{target}` is the device id (MAC, lower case, no separators), or, since API 3.5, the device name (case sensitive). Use the MAC.
- Sources: API Reference PDS-062489 Rev 3.9.0.0 section 4.19.6 "Command set property" p.106 and section 7.1 Table 168 `configuration.device_name` p.254; Application Note Library PDS-062491 Rev 2.9 section 8.2 "Managing Device Name" p.149-151 (the same request body is shown there).
- Applies to BlueRiver (AVP) and UEX devices, since API 3.0. The SDK (VOIPS V2.30) wraps the same thing as `SET_DEVICE_NAME` (SDK section 2.3.39 p.73); this app does not use the SDK, it talks REST/Telnet.
- Telnet equivalent: the sources describe the `set property` command for the Telnet shell but this research did not find a documented one-line Telnet syntax for it; REST is the documented form and is what the rest of `SdvoeClient` already uses (`SendRestPostAsync`, `RequestAsync`), so **REST is the chosen path**.

## Rules the Control Server enforces (documented) - they differ from the plan

| Rule | Source | Consequence for D-R2 (1-32 chars) |
|---|---|---|
| **Maximum 19 characters** | API 3.9 Table 168 p.254 | **The plan's 32 is wrong; the limit is 19.** `NameRules` must cap at 19 (decision for the master / user). |
| All displayable ASCII allowed: a-z, A-Z, 0-9, space and symbols | same | Non-ASCII (accents, CJK) is not allowed; `NameRules` should reject it with a reason. |
| **Colon (:) not allowed**; the server answers `ILLEGAL_ARGUMENT` "Device name contains unsupported character" | App Note p.150 | `NameRules` must reject `:`. |
| The name is case sensitive when used as a request target; names should be unique per device: if several devices share a name the server only logs a warning and uses the first one registered | App Note p.150 | Duplicate check stays (case-insensitive is stricter than the server and fine). |
| Setting a name equal to a *disconnected* device's name sets that disconnected device's name to NULL | App Note p.151 | The duplicate rule must also consider disconnected/known devices. |
| The name is persistent after a factory reset (AVP firmware 1.5+) and can be reverted to the MAC with the keyword value `mac_address` | API Table 168 p.254, App Note 8.2.1 p.151 | A rename-back test can restore the exact old value. |
| Only one device per request, except `mac_address` (ALL / ALL_RX / ALL_TX accepted) | API Table 168 p.254 | One request per device. |
| A disconnected target fails with `DEVICE_DISCONNECTED` | App Note p.151 | `RenameResult.Reason` carries the server's `message`. |
| Spaces and reserved characters in a *name used in the URL* need `%` escaping | App Note p.149 | Not needed when the MAC is the target. |

Not found in the supplied sources: a documented limit on minimum length (the plan's 1 is reasonable), a documented rule on leading/trailing spaces (the app rule "no edge spaces" is our own and stricter than the server), and the exact error string for a name that is too long.

## Lab Control Server (observed, read-only, 2026-11-03)

- The Control Server is `controlserver.exe`, `Server: BlueRiver-Control/3.9.0.1`. **REST listens on 8090** (and Telnet on 6970); both are held by `controlserver.exe`. Port 8080 on the same machine is a *different* ASP.NET service (Kestrel, Swagger v2.6.1), it answers 404 for `/api/device/...`. `appsettings.json` ships `RestPort: 8080` while `SdvoeClient` defaults `_restPort` to 8090: check which one the running app really uses before R2 relies on it (this is a separate finding).
- `POST /api/device/ALL {"op":"get","subset":"identity"}` on 8090 returns 200 and the four lab devices; Telnet `require api 3.0.0.0` then `get all identity` returns the same list (this is how the app discovers):

| device_id | device_name (now) | vendor_id | role (by product) |
|---|---|---|---|
| 74fe488b07bb | TX2_DUAL LINK_SFPA | 105 | TX |
| 74fe483c059e | RX1_c059e_SFPA | 105 | RX |
| 74fe488b22ca | PAX355S_SFP A | 105 | RX |
| 0016c0589c1e | VEGA_1100 | 192 | non-AVAS |

## Still to prove (needs authorization to write to the lab devices)

The plan's completion criterion is the request, the response and a read-back showing the new `device_name`, from one TX and one RX, then the rename back. Prepared test (not run):

1. Record the current names (above). 2. `POST /api/device/74fe488b07bb` and `/api/device/74fe483c059e` with the body above and value `R1-test-TX` / `R1-test-RX` (10 characters). 3. Read back with `get all identity` over Telnet and REST. 4. Probe the limit on the RX only: a 19-character name (expect SUCCESS), a 20-character name (expect rejection, to capture the exact error text) and a name with a colon (expect `ILLEGAL_ARGUMENT`). 5. Restore the original names (`TX2_DUAL LINK_SFPA`, `RX1_c059e_SFPA`) in a `finally`, read back again, and compare with step 1.

Rollback if the restore request fails: send the original value again; a name can always be reset to the MAC with the value `mac_address`, then set again.
