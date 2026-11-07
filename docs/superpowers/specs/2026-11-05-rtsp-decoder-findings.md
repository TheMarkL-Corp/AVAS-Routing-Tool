# RTSP decoder spike (T0) findings

Date: 2026-11-07. Plan: `docs/superpowers/plans/2026-11-05-rtsp-view-source.md`. The stream address was read from `AVAS_RTSP_URL` in the shell only; it is not written here ("the user's camera").

## Setup

Throwaway console app (scratchpad, not in the repo): `LibVLCSharp 3.9.1` + `VideoLAN.LibVLC.Windows 3.0.21`, .NET 8, `win-x64` natives. Frames are taken through `SetVideoFormatCallbacks` / `SetVideoCallbacks` (RV32 into a memory buffer, no window), which is the same mechanism planned for the `WriteableBitmap`. Each run plays 12 s and counts frames.

## Measured

| Run | First frame | Frames in 12 s | Rate | Size reported |
|---|---|---|---|---|
| `:rtsp-tcp`, `network-caching=150` | 224 ms | 669 | 55.8 fps | 1920x1090 |
| UDP (default), `network-caching=150` | 254 ms | 691 | 57.6 fps | 1920x1090 |
| `:rtsp-tcp`, `network-caching=300` | 256 ms | 637 | 53.1 fps | 1920x1090 |
| trimmed plugins, `:rtsp-tcp`, 150 | 268 ms | 688 | 57.3 fps | 1920x1090 |

- Codec of the camera's video track: H.264 (fourcc `h264`). Playback state `Playing` in every run.
- Both TCP and UDP transport work with this camera. TCP is the safer default for a hospital network (no UDP port or firewall surprises); UDP showed one `decoder error: buffer deadlock prevented` line in one run.
- Rates of 53-58 fps (not exactly 60) are consistent with a 12 s window that includes start-up and, in the 300 ms run, more buffering. The camera's true frame rate was not read from the stream.
- The decoder prints `Failed to set on top` and a swscaler "deprecated pixel format" warning to its own console. Both come from VLC's default video output and the RV32 conversion; they do not affect the callback path. The log lines never contained the address in these runs, but T4 must still filter the log callback (plan Review Focus 1).

## Size

- Whole spike output folder: 198 MB, because `VideoLAN.LibVLC.Windows` ships both `win-x64` and `win-x86` natives.
- `win-x64` alone: about 100 MB (plugins 96 MB; `codec` 38.8 MB, `access` 14.3 MB, `demux` 9.1 MB are the largest groups).
- After deleting unneeded plugin groups (`stream_out`, `visualization`, `lua`, `text_renderer`, `access_output`, `services_discovery`, `meta_engine`, `spu`, `audio_filter`, `mux`, `video_splitter`, `audio_mixer`) and the `lua` and `hrtfs` folders: **80 MB** for the whole app folder including the managed DLLs, still playing at 57 fps. The `codec` plugin (the FFmpeg decoder bundle) is the floor and cannot be split further by deleting files.
- Package for the product: reference only the `win-x64` natives (about 100 MB untrimmed, about 80 MB trimmed). Not tested: whether `plugins.dat` must be regenerated after trimming (the trimmed run started without it).

## Not measured (and why)

- **Latency, glass to glass:** needs a running clock in front of the camera and a person comparing it with the picture. First-frame time (about 0.25 s) is not latency. Still open; do it with the real tile in T7, or ask the user to read the delay off a stopwatch.
- **H.265:** the user's camera sends H.264 only, so H.265 was not tested. VLC's FFmpeg bundle normally decodes H.265, but this is unverified here.
- **Clean user profile, other PC, single-file exe:** not tested. The user confirmed on 2026-11-05 that the final package need not be one exe, so the app folder with `libvlc` beside the exe is the target.
- **Reconnect:** LibVLC has none of its own that we rely on; T3 owns retry.

## Result for D-3

LibVLC passes the spike on every number that could be measured: it connects to the user's camera in about a quarter of a second, decodes 1920x1090 H.264 at roughly camera rate over TCP or UDP, and works with trimmed plugins. Recommendation stands: **proceed with LibVLCSharp, `win-x64` natives only, plugins trimmed as above, `:rtsp-tcp` by default.** The one open number is latency, to be read off with the real tile.
