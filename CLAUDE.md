# Claude Code Guidelines for AVAS Routing Tool

Welcome! This repository houses the **AVAS Routing Tool**, a .NET 8 WPF desktop application for medical/surgical SDVoE video routing and ultra-low-latency previewing.

## Primary Handover Documentation
A complete set of handover documentation has been prepared specifically for you in the [`Handover/`](./Handover/) directory:
* **Start Here**: [`Handover/README.md`](./Handover/README.md)
* **Architecture & Pipelines**: [`Handover/01_ARCHITECTURE_AND_DESIGN.md`](./Handover/01_ARCHITECTURE_AND_DESIGN.md)
* **Codebase Map**: [`Handover/02_CODEBASE_STRUCTURE.md`](./Handover/02_CODEBASE_STRUCTURE.md)
* **Protocols & Hardware Setup**: [`Handover/03_PROTOCOLS_AND_HARDWARE.md`](./Handover/03_PROTOCOLS_AND_HARDWARE.md)
* **Verified Features & Edge Cases**: [`Handover/04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md`](./Handover/04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md)
* **Phase 2 MultiView Roadmap & Tasks**: [`Handover/05_NEXT_STEPS_AND_PHASE_2_PLAN.md`](./Handover/05_NEXT_STEPS_AND_PHASE_2_PLAN.md)

---

## Essential Commands

All commands run from the repository root:
* **Build Solution**: `dotnet build AvasRoutingTool.sln`
* **Run Tests**: `dotnet test AvasRoutingTool.sln`
* **Run App**: `dotnet run --project src/AvasRoutingTool/AvasRoutingTool.csproj`
* **Publish Release**: `dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -o publish/AvasRoutingTool`

---

## Mandatory Operational Rules

1. **Persistent Memory Maintenance**:
   * Always inspect [`memory/active_context.md`](./memory/active_context.md) and [`memory/system_patterns.md`](./memory/system_patterns.md) before starting any task.
   * Update [`memory/active_context.md`](./memory/active_context.md) and [`memory/progress.md`](./memory/progress.md) upon completing tasks.
2. **Strict Test-Driven Development (TDD)**:
   * Write failing tests before writing production code.
   * Verify Red -> Green -> Refactor cycle.
3. **STA Thread Testing for Views**:
   * Never modify XAML without verifying views in [`ViewSmokeTests.cs`](./tests/AvasRoutingTool.Tests/ViewSmokeTests.cs) on an STA thread to prevent silent TwoWay binding runtime crashes.
4. **Zero Socket Leaks**:
   * When handling RTP preview streams, ensure sockets and multicast memberships are deterministically closed.
5. **Atomic Git Commits**:
   * Commit code modifications together with memory updates in single, focused atomic commits.
