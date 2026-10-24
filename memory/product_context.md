# Product Context

## Problem Statement
Medical and industrial AV-over-IP systems operating with Advantech AVAS-223 transceivers require video routing between surgical cameras, endoscopy processors, diagnostic PCs, and surgical display monitors.
While the Semtech BlueRiver AV Manager (BAVM) provides rich device configuration and network management, its routing interface is table-based, lacks live video previews of camera feeds, and does not provide an intuitive visual drag-and-drop user experience.

## Solution Vision
**AVAS Routing Tool** delivers:
1. **Interactive Visual Canvas**: Inspired by SigmaXG, presenting Sources (TX) on the top row, Displays (RX) in the middle row, and Layouts on the bottom row.
2. **Live TX Previews**: Operators can see what each camera/source is displaying before making a routing decision.
3. **100% BAVM Preservation**: Rather than attempting to rewrite dozens of complex BAVM admin pages, the full BAVM web console is embedded via an isolated WebView2 tab, providing 100% of its functions completely intact.
4. **Single View First**: Streamlined 1-to-1 video, audio, and USB Mouse/Touch Thru routing in Phase 1, followed by MultiView layout composition in Phase 2.

## Target Hardware: Advantech AVAS-223
* Medical-grade SDVoE transceiver (Vendor ID: 105, Product ID: 81).
* Dual 10GbE SFP+ interfaces supporting uncompressed 4Kp60 4:4:4 video.
* Hardware: Semtech AVP2000 (Media SoC) + Cypress baseboard MCU (UART Node 1).
* HDMI 2.0 In/Out, 3.5mm 2-way stereo audio, USB 2.0 (Type-B Device / Type-A Host).
