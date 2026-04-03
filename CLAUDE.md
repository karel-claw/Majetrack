# Majetrack — Project Instructions

_Portfolio tracking application._

---

## Stack

- **.NET 10** (latest stable)
- Minimal APIs
- PostgreSQL + **EF Core** (latest stable)
- FluentValidation

---

## Architecture — Vertical Slices

Each feature is a self-contained slice. No Domain/Application/Infrastructure separation.

```
src/
  Features/
    Transactions/
      Create/
        Endpoint.cs
        Validator.cs
        Request.cs
      List/
      Import/
    Portfolio/
      Summary/
      Positions/
tests/
  Features/
    Transactions/
      CreateTests.cs
```

---

## Agent Workflow (mandatory for every task)

Every GitHub issue is implemented using this exact workflow. No shortcuts.

### 1. Architect → Plan
Invoke the **architect** subagent to produce a written plan (no code):
- Files to create/modify
- Key implementation decisions
- Risks and open questions

```
Use the architect agent. Produce an implementation plan (NO code) for: [task description]
```

### 2. Human approves the plan
**Wait for explicit approval before proceeding.**
Do not implement until the human says "schváleno", "implementuj", "approve", or similar.

### 3. Developer → Implementation (new session, model: opus)
Invoke the **developer** subagent to implement the approved plan:
- TDD: write tests first (RED), then implement (GREEN)
- Follow the plan exactly — no improvisation
- Each task in a fresh `claude --model opus --permission-mode bypassPermissions` session

### 4. Code Reviewer → Review
After implementation, invoke the **code-reviewer** subagent:
- Review changed files for security, architecture, conventions, performance
- Report blockers before closing the issue

### 5. Close issue + continue
- Close the GitHub issue
- Present plan for the next task

### Agents available (in `.claude/agents/`)
| Agent | When to use |
|-------|-------------|
| `orchestrator` | Starting a new feature with multiple subtasks |
| `architect` | Design, API contracts, data models — before any code |
| `developer` | Implementation after plan is approved |
| `code-reviewer` | After implementation, before PR/issue close |
| `test-scenario-designer` | Test plan before writing tests |
| `test-implementation` | Writing the actual tests |
| `documentation` | XML docs, ADRs, README updates |

---

## Development Rules

### TDD (mandatory)
1. Write tests first — RED
2. Write code to pass — GREEN
3. Refactor

### Always Latest Versions
- Always use the latest stable NuGet packages
- No pinned versions without a documented reason

### Research Before Coding
1. **Context7 MCP** — official library/framework docs
2. **Web search** — "best practices [technology] 2026", "[library] vs alternatives"
3. **GitHub issues** — known bugs, workarounds

---

## Conventions

### HTTP
- `POST /api/transactions` → 201 Created
- `GET /api/transactions` → 200 with pagination
- `DELETE /api/transactions/{id}` → 204 No Content
- `GET /api/portfolio/summary` → 200
- 400 Validation | 401 Unauthorized | 404 Not Found

### Commit Messages
```
feat: Add portfolio summary endpoint
fix: Resolve FIFO calculation for partial sells
test: Add integration tests for CSV import
chore: Update dependencies to latest
```

---

## Documentation (mandatory)

Every class, method, and property must have an XML doc comment in English.

```csharp
/// <summary>
/// Represents a financial transaction recorded by the user.
/// </summary>
public class Transaction { ... }

/// <summary>
/// The total amount of the transaction in the original currency, including fees.
/// Stored with precision 18,2.
/// </summary>
public decimal TotalAmount { get; set; }

/// <summary>
/// Calculates the net gain/loss for this transaction using FIFO cost basis.
/// Returns null if position has not been fully resolved yet.
/// </summary>
public decimal? CalculateRealizedPnl() { ... }
```

Rules:
- `<summary>` on every `public` and `internal` type and member — no exceptions
- Explain **what it is/does AND why it exists** — not just repeat the name
- When modifying existing code: **review and update the doc comment** to reflect the change
- Use `<param>`, `<returns>`, `<remarks>` where they add clarity
- Do NOT write obvious filler like `/// <summary>Gets the Id.</summary>`

## What Not To Do

- ❌ Pin versions without a reason
- ❌ Write code without tests
- ❌ Guess how something works — look it up
- ❌ Use `#region` — ever
- ❌ Add code without XML doc comments
- ❌ Leave stale doc comments after modifying code

---

_v1.1 — 2026-04-03_
