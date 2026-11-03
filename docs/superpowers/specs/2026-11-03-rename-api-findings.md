# Rename API findings (task R1)

Status: **proven on the lab Control Server on 2026-11-03** (one TX, one RX, names restored afterwards, with the user's explicit OK to write to those two devices). `documented` = vendor PDFs, `observed` = measured on the lab server.

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
- Telnet equivalent (not used): the sources describe the `set property` command for the Telnet shell but this research did not find a documented one-line Telnet syntax for it; REST is the documented form and is what the rest of `SdvoeClient` already uses (`SendRestPostAsync`, `RequestAsync`), so **REST is the chosen path**.

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

## Live proof (observed, 2026-11-03, lab Control Server BlueRiver-Control/3.9.0.1, REST port 8090)

Request (one per device, the MAC as target), the same body for TX and RX:

```
POST http://localhost:8090/api/device/74fe488b07bb
{ "op": "set:property", "key": "configuration.device_name", "value": "R1-test-TX" }
```

| Step | Device | Request value | HTTP | Response | Read-back (`POST /api/device/ALL {"op":"get","subset":"identity"}`, 1 s later) |
|---|---|---|---|---|---|
| set | TX 74fe488b07bb | `R1-test-TX` | 201 | `{"status":"PROCESSING","request_id":555,"result":null,"error":null}` | `device_name` = `R1-test-TX` |
| set | RX 74fe483c059e | `R1-test-RX` | 201 | `{"status":"PROCESSING","request_id":558,...}` | `device_name` = `R1-test-RX` |
| probe, 19 chars | RX | `0123456789012345678` | 201 | `PROCESSING`, request_id 561 | applied |
| probe, 20 chars | RX | `01234567890123456789` | **400** | `{"status":"ERROR","error":{"reason":"ILLEGAL_ARGUMENT","message":"Device name too long, must be 19 characters at most"}}` | unchanged (still the 19-char value) |
| probe, colon | RX | `R1:colon` | **400** | `{"status":"ERROR","error":{"reason":"ILLEGAL_ARGUMENT","message":"Device name contains unsupported character ':'."}}` | unchanged |
| restore | TX | `TX2_DUAL LINK_SFPA` | 201 | `PROCESSING`, request_id 564 | restored |
| restore | RX | `RX1_c059e_SFPA` | 201 | `PROCESSING`, request_id 567 | restored |

After the restore `get all identity` returned exactly the four original names (`PAX355S_SFP A`, `TX2_DUAL LINK_SFPA`, `VEGA_1100`, `RX1_c059e_SFPA`).

What this means for the code:
- A valid rename answers **HTTP 201 with `status: PROCESSING` and a `request_id`**, i.e. it is asynchronous, like the other set commands (`SdvoeClient.RequestAsync` already follows `/api/request/{id}` while the server says PROCESSING). The new name was visible in `get all identity` within 1 s.
- A rejected name answers **HTTP 400 with `status: ERROR` and `error.reason` / `error.message`**: use `message` as `RenameResult.Reason`. The two server messages above are the exact strings for the too-long and colon cases.
- The limit is **19 characters** (19 accepted, 20 rejected), confirming the documentation. ASCII-only was not probed live (documented only).
- The read-back was done over REST (`get subset identity`); the Telnet `get all identity` used for discovery reads the same device list (checked earlier in the session before any write).
