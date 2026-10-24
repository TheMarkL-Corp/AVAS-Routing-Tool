# AVAS Routing Tool: Project Memory System

This sub-directory (`memory/`) serves as the organized, persistent, and consistent memory for the **AVAS Routing Tool** project across all coding sessions, human interactions, and AI agents.

## Structure

| File | Purpose | Update Cadence |
|---|---|---|
| [`active_context.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/active_context.md) | Current focus, recent changes, immediate next steps, and active blockers. | Every task / session |
| [`product_context.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/product_context.md) | Why this project exists, target hardware (Advantech AVAS-223), user persona, and business value. | When domain or requirements evolve |
| [`system_patterns.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/system_patterns.md) | Key architectural decisions, component patterns, protocol rules, and coding standards. | When patterns or conventions change |
| [`progress.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/progress.md) | High-level roadmap, completed feature checklist, and known issues. | After completing milestones/tasks |
| [`GLOSSARY.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/GLOSSARY.md) | Canonical domain language (SDVoE, AVAS, TX/RX, Viewport, Multicast). | When terminology is refined |
| [`decisions/`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/decisions/) | Architecture Decision Records (ADRs: `YYYY-MM-DD-<title>.md`). | Upon major technical decisions |

## Rules of Engagement for AI Agents

1. **Inbound Context Loading**: At the start of every session, read `memory/active_context.md` and relevant sections of `system_patterns.md`.
2. **Outbound State Synchronization**: Before finishing a turn or claiming a task is done, update `memory/active_context.md` and `memory/progress.md`.
3. **Atomic Commits**: Always commit documentation updates in `memory/` alongside the code changes they describe.
