#!/usr/bin/env python3
"""Multiview bandwidth admission model (STUDY ONLY - not part of any build, touches no device).
Pure arithmetic, calibrated against vendor, field-note and live figures:
  wire_rate = active_w * active_h * fps * bits_per_pixel / 0.93        (no blanking)
  compressed (auto, when wire_rate > limit) = wire_rate / 1.5
  Checks: 720p60 4:4:4 -> 1.43 and 1080p60 4:2:2 -> 2.14 and 4K60 10-bit 4:2:0 -> 8.03   [user-provided field notes, exact]
          1080p60 RGB -> 3.21                                      [APP s6.3.2.1 "approximately 3.2 Gbps"]
          4096x2160 60p 4:4:4 compressed -> 9.13                   [user-provided field notes, exact at 1.5:1]
          3840x2160 60p compressed -> 8.56                         [live Boom->Left Wireless 8.57; APP s6.3.2.3 "close to 8.5"]
          4K30 RGB -> 6.42                                         [live zzPresentation 6.34, 1.3 % lower: efficiency may be 0.93-0.94]
Limit: 9.0 Gbps per TX and per RX (API 5.1.1). Field evidence: ~9.1 Gbps still displayed, ~10.1 failed."""
import json, sys
EFF, LIMIT, COMP_RATIO = 0.93, 9.0, 1.5
def wire(w, h, fps, bpp=24): return w * h * fps * bpp / EFF / 1e9
def stream0(src):
    raw = wire(src["w"], src["h"], src["fps"], src["bpp"])
    if raw > LIMIT: return raw / COMP_RATIO, True   # auto light-compression (APP s4.2); unusable as a multiview tile
    return raw, False
def main():
    lab = json.load(open(sys.argv[1])) if len(sys.argv) > 1 else DEFAULT
    print(f"TX egress (limit {LIMIT} Gbps; audio/control/USB not included)")
    for t in lab["tx"]:
        a, comp = stream0(t["s0"])
        b = wire(t["s1"]["w"], t["s1"]["h"], t["s1"]["fps"]) if t.get("s1") else 0
        print(f"  {t['name']:<16} S0 {a:5.2f}{' (compressed)' if comp else '             '}  S1 {b:5.2f}  total {a+b:5.2f}  {'OVER' if a+b > LIMIT else 'ok'}")
    print("RX ingress: N distinct scaled sources (24 bpp)")
    fmts = ((1280, 720, 30), (1920, 1080, 30), (1920, 1080, 60), (3840, 2160, 30))
    print("  N   " + "  ".join(f"{w}x{h}@{f}".rjust(11) for w, h, f in fmts))
    for n in range(1, 11):
        print(f"  {n:<3} " + "  ".join((f"{n*wire(w,h,f):6.2f}" + ("*" if n*wire(w,h,f) > LIMIT else " ")).rjust(11) for w, h, f in fmts))
    print("  max sources <= limit: " + ", ".join(f"{w}x{h}@{f}: {int(LIMIT // wire(w,h,f))}" for w, h, f in fmts))
DEFAULT = {"tx": [
 {"name": "VEGA_1100 4K60", "s0": {"w": 3840, "h": 2160, "fps": 60, "bpp": 24}, "s1": {"w": 1920, "h": 1080, "fps": 30}},
 {"name": "Boom_Camera_A", "s0": {"w": 3840, "h": 2160, "fps": 60, "bpp": 24}, "s1": {"w": 1920, "h": 1080, "fps": 30}},
 {"name": "PTZ_Camera", "s0": {"w": 1920, "h": 1080, "fps": 60, "bpp": 24}, "s1": {"w": 1920, "h": 1080, "fps": 30}},
 {"name": "VEGA S0 halved", "s0": {"w": 3840, "h": 2160, "fps": 30, "bpp": 24}, "s1": {"w": 1920, "h": 1080, "fps": 30}}]}
if __name__ == "__main__": main()
