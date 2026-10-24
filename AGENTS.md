# AI Agent Instructions for AVAS Routing Tool

## Primary Project Rule: Memory Maintenance
This project mandates keeping an organized, consistent memory in the `memory/` sub-directory:
* [`memory/active_context.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/active_context.md)
* [`memory/product_context.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/product_context.md)
* [`memory/system_patterns.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/system_patterns.md)
* [`memory/progress.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/progress.md)
* [`memory/GLOSSARY.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/GLOSSARY.md)
* [`memory/decisions/`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/memory/decisions/)

### Required Agent Lifecycle:
1. **On Session Start**: Always read `memory/active_context.md` and relevant sections of `memory/system_patterns.md` before taking action.
2. **During Execution**: Follow Test-Driven Development (TDD) and the Implementation Plan in `docs/superpowers/plans/`.
3. **On Task Completion**: Update `memory/active_context.md` (recent changes, next steps) and `memory/progress.md` (milestone progress).
4. **On Key Decisions**: Record new Architecture Decision Records in `memory/decisions/YYYY-MM-DD-<title>.md`.
5. **Git Versioning**: Commit memory updates together with code changes in atomic commits.
