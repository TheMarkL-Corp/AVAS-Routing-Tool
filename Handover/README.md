# AVAS Routing Tool — Claude Code Handover Guide

Welcome to the **AVAS Routing Tool** codebase! This documentation suite is specifically organized to provide Claude Code (or any incoming AI engineer / human developer) with complete architectural context, hardware environment details, verified workflows, and actionable implementation guidelines to seamlessly continue development.

---

## 1. Executive Summary & Project Purpose

**AVAS Routing Tool** is a mission-critical, low-latency medical/surgical desktop application designed for operating room video management. It interfaces with Semtech BlueRiver SDVoE transceivers and proprietary AVAS hardware (Cypress PSoC Baseboard MCU + Semtech BlueRiver chipsets) to provide:
1. **Low-Latency Live Video Previews**: Acquires and decodes RFC 4175 raw video RTP multicast streams directly on the Windows host desktop over UDP 5000/5004.
2. **Single-View Drag-and-Drop Video Routing**: Intuitive touch-friendly routing from video sources (TX) to surgical display monitors (RX).
3. **Multi-Modal Un-Routing**: Instant video turn-off via on-card button or drag-and-drop from the bottom "Off" layout tile.
4. **Decoder Video Mirroring**: Decoders reactively mirror the source video preview with zero additional network socket overhead.
5. **Adaptive FullHD Grid**: Dynamic, widescreen-proportioned card sizing (320px–600px, 16:9 aspect) eliminating empty voids on 1920×1080 screens.
6. **Embedded BAVM Console**: Isolated Microsoft WebView2 browser control hosting the official Semtech BlueRiver AV Manager web console (`http://localhost:80`).

---

## 2. Essential Commands

All commands run from the repository root: `c:\Users\POC-615\Documents\GitHub\AVAS-Routing-Tool`

| Action | Command |
|---|---|
| **Build Solution** | `dotnet build AvasRoutingTool.sln` |
| **Run Unit & Smoke Tests** | `dotnet test AvasRoutingTool.sln --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"` (a bare `dotnet test` also runs the `Live*` hardware classes) |
| **Run Tests (Detailed)** | `dotnet test AvasRoutingTool.sln --logger "console;verbosity=normal"` |
| **Run Application** | `dotnet run --project src/AvasRoutingTool/AvasRoutingTool.csproj` |
| **Publish Standalone Binary** | `dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -o publish/AvasRoutingTool` |

> **Current Test Status**: **147 non-live tests passing** (unit, integration, STA view-smoke). The `Live*` tests route/unroute real lab hardware when a Control Server is up, so run them only deliberately (the classes `LiveControlServerTests` and `LiveFunctionTests`): `dotnet test AvasRoutingTool.sln --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"` for the safe set. (Do not use `!~Live`: it also skips every test whose name merely contains "live".) If the app is running, add `--artifacts-path <scratch dir>` because the Debug output is locked.

---

## 3. Handover Documentation Index

Please read the following documents in order depending on your immediate task:

| Document | Purpose & Contents |
|---|---|
| [`01_ARCHITECTURE_AND_DESIGN.md`](./01_ARCHITECTURE_AND_DESIGN.md) | Deep dive into the Dual-Mode Shell, Live Video Demux/Rasterizer pipeline, in-band MCU UART, and Adaptive Grid layout engine. |
| [`02_CODEBASE_STRUCTURE.md`](./02_CODEBASE_STRUCTURE.md) | Complete file-by-file map of all ViewModels, Views, RTP decoders, SDVoE services, controls, and test suites. |
| [`03_PROTOCOLS_AND_HARDWARE.md`](./03_PROTOCOLS_AND_HARDWARE.md) | Semtech BlueRiver REST API specifications, raw TCP CLI commands, in-band Cypress Baseboard MCU UART protocol, and RFC 4175 RTP headers. |
| [`04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md`](./04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md) | Catalog of all physical hardware-verified features, discovered quirks, and technical workarounds. |
| [`05_NEXT_STEPS_AND_PHASE_2_PLAN.md`](./05_NEXT_STEPS_AND_PHASE_2_PLAN.md) | Phase 2 MultiView roadmap (Matrix 2x2, PIP, Side-by-Side, 1+3 Quad), route persistence, and immediate tasks. |

---

## 4. Key Rules for Claude Code

When working in this repository:
1. **Memory Maintenance**: Follow [`AGENTS.md`](../AGENTS.md). Always consult [`memory/active_context.md`](../memory/active_context.md) and [`memory/system_patterns.md`](../memory/system_patterns.md) before writing code, and update them when completing tasks.
2. **Test-Driven Development (TDD)**: Write failing tests before implementation code. Never claim a test passes without running `dotnet test` and reading the output.
3. **STA Thread Testing for Views**: When adding or altering XAML views, add real UI smoke tests using `ViewSmokeTests.RunSta(...)` to catch binding/resource errors that pure unit tests miss.
4. **Zero Network Socket Leaks**: When interacting with preview streams or decoders, never spawn redundant IGMP multicast groups or leak UDP sockets.
5. **Atomic Commits**: Stage and commit code changes together with updated memory files.
