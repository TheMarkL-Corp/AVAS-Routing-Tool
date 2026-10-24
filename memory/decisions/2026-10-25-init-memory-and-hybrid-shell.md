# ADR 001: Project Memory System and Dual-Mode Shell Architecture

**Date**: 2026-10-25  
**Status**: Accepted  
**Deciders**: Engineering Team & Product Owner  

## Context
The goal of this project is to recreate the BlueRiver AV Manager (BAVM) with a superior visual drag-and-drop routing UI/UX and live video previews for Advantech AVAS-223 transceivers, while preserving 100% of BAVM's functionality without altering any of it. Additionally, a primary project rule requires keeping an organized, consistent memory in a sub-directory.

## Decision

### 1. Dedicated `memory/` Sub-Directory
We establish `memory/` as the single source of truth for project state, consisting of `active_context.md`, `product_context.md`, `system_patterns.md`, `progress.md`, `GLOSSARY.md`, and `decisions/`. All AI agents are governed by `AGENTS.md` and `GEMINI.md` to read memory on entry and update memory upon completing tasks.

### 2. Dual-Mode Application Shell in .NET 8 WPF
To satisfy both requirements (100% BAVM function inheritance + native 60fps drag-drop visual routing):
* **Mode 1 (Visual Routing Canvas)**: Implemented as native WPF controls for high-speed drag-and-drop, low-latency live preview rasterization via `WriteableBitmap`, and automated 3-in-1 routing (Video + Audio + USB).
* **Mode 2 (BAVM Console)**: Implemented as an isolated Microsoft WebView2 tab hosting the complete official BAVM web UI (`http://localhost:80`).
* Both modes interface with the same local SDVoE Control Server (port 6970 / 8080), ensuring seamless state synchronization without code duplication or risk of regression.

### 3. Phased Routing Phasing
* **Phase 1**: Single View routing (1-to-1 video, audio to Viewport 1, USB HID Mouse/Touch Thru).
* **Phase 2**: MultiView window-level routing (Matrix 2x2, PIP, Side-by-Side, 1+3).

## Consequences
* **Positive**: 100% backward compatibility with all BAVM features; zero risk of losing advanced management options (EDID, firmware, walls, security); optimal performance for live video preview decoding via C# Winsock + SIMD/fixed-point rasterizer.
* **Trade-off**: Requires hosting WebView2 for the BAVM tab, adding ~100MB memory footprint, which is well within acceptable limits on modern workstations.
