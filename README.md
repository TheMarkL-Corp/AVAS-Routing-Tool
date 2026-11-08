# AVAS Routing Tool

Low-latency medical and surgical video routing desktop application for operating room environments, interfacing with Semtech BlueRiver SDVoE transceivers and proprietary AVAS hardware.

## Overview

The **AVAS Routing Tool** is a high-performance Windows desktop application designed for operating room video management. It integrates native WPF visual routing with an embedded Semtech BlueRiver AV Manager (BAVM) console to provide intuitive, low-latency control over surgical video feeds.

### Key Features

* **Low-Latency Live Video Previews**: Acquires and decodes RFC 4175 raw video RTP multicast streams directly on the host desktop over UDP 5000/5004 with zero-leak rendering to `WriteableBitmap`.
* **Single-View Drag-and-Drop Video Routing**: Intuitive touch-friendly routing from video sources (transmitters) to surgical displays (receivers).
* **MultiView Canvas Layouts**: Support for `Matrix 2x2`, Picture-in-Picture (`PIP`), `Side-by-Side` (50%/50%), and `1+3 Quad` layouts with per-window drag-and-drop source assignment.
* **Hardware-Informed Display Canvas**: Automatic display EDID detection (`get edid`) sizing the MultiView canvas to the monitor's native timing (e.g., 3840x2160@60) with automatic frame-rate division (>9 Gbps) for high-bandwidth 4K layouts.
* **Hardware Source of Truth**: Automatic route and layout restoration querying physical decoders on startup, refresh, decoder reconnection, and tab switching.
* **Multi-Modal Video Disconnect**: Quick un-route button on display cards and drag-and-drop "Off" layout tile.
* **Zero-Socket Video Mirroring**: Display cards reactively mirror active source video previews without spawning redundant network sockets or multicast groups.
* **Adaptive FullHD Grid**: Dynamic, widescreen-proportioned card sizing (320px–600px, 16:9 aspect) eliminating empty voids across standard 1080p and high-resolution displays.
* **Dual-Mode Shell**: Seamless switching between the native Visual Routing canvas and an isolated Microsoft WebView2 browser control hosting the BlueRiver AV Manager web console.

---

## Technical Stack & Architecture

* **Framework**: .NET 8.0 WPF (`net8.0-windows`)
* **Design Pattern**: Model-View-ViewModel (MVVM)
* **Networking**:
  * SDVoE REST API (port 8090) & Telnet/TCP CLI (port 6970)
  * RFC 4175 raw video RTP multicast (UDP ports 5000 / 5004)
  * Dynamic multicast IP management (`225.0.0.1`–`225.0.1.254`)
* **Color Processing**: Q8 fixed-point BT.709 limited-range UYVY to RGB24/BGR24 rasterization
* **Web Integration**: Microsoft WebView2 with isolated user data directory

---

## Getting Started

### Prerequisites

* Windows 10/11 x64
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Network connectivity to a Semtech BlueRiver Control Server (default `127.0.0.1:8090` / `6970`)

### Build & Run

```powershell
# Clone the repository
git clone https://github.com/TheMarkL-Corp/AVAS-Routing-Tool.git
cd AVAS-Routing-Tool

# Build the solution
dotnet build AvasRoutingTool.sln

# Run all non-live unit & smoke tests (excludes exactly the two classes that drive real lab hardware)
dotnet test AvasRoutingTool.sln --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"

# Launch the application
dotnet run --project src/AvasRoutingTool/AvasRoutingTool.csproj
```

### Standalone Release Packaging

To publish a self-contained portable executable:

```powershell
dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -o publish/AvasRoutingTool
```

---

## Documentation

* Technical architecture and hardware protocol specifications are available in the [`Handover/`](./Handover/) directory.
