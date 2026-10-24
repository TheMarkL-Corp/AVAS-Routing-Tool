# Active Context

## Current Status & Focus
* **Current Phase**: Project Architecture & Implementation Planning (Phase 1: Single View Video Routing).
* **Key User Refinements**:
  1. Configurable BAVM URL (default: `http://localhost:80`, remembered across sessions).
  2. SDVoE Control Server IP derived automatically from BAVM host IP; Control Server port configurable.
  3. Strict Device Name display (no EDID monitor model names).
  4. Phase 1: Video Routing ONLY (Audio and USB-KVM functions remain in original BAVM).
  5. Architecture clarification: Comparing Native C# WPF vs Web/Node alternatives.

## Recent Changes
* Established project requirements and architectural specification in `docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md`.
* Reverse-engineered reference codebases: `AVAS-BRV-wrapper-win` (.NET 8 WPF), `SingleLink_REA_Report` (CYP SDVoE Tester), and `62596956.BlueRiver_AV_Manager_2.1.0.1_b1375_source` (BAVM 2.1.0).
* Created canonical domain glossary and structured memory layout.

## Next Steps
1. Initialize Git repository and commit baseline memory structure.
2. Complete comprehensive `writing-plans` implementation document.
3. Scaffold WPF solution and import proven core libraries (`SdvoeClient`, `UnifiedRtpDemuxReceiver`).
4. Implement drag-and-drop 3-in-1 routing orchestrator (Video + Audio + USB).

## Active Blockers / Open Questions
* None currently blocking implementation.
