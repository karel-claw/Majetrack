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

---

### Step 1 — Architect → Plan
Invoke the **architect** subagent to produce a written plan (no code):
- Files to create/modify
- Key implementation decisions
- API contracts, data model changes
- Risks and open questions

---

### Step 2 — Plan Review (3 independent agents, run in parallel)
Invoke three **plan-reviewer** agents simultaneously, each with a different model:
- **opus** — deep reasoning, architectural correctness
- **sonnet** — balance of speed and quality
- **minimax-m2.5** — independent perspective, alternative approaches

Each reviewer must answer:
1. Does the plan make sense?
2. Are there simpler or better alternatives?
3. Any risks or gaps the architect missed?

Consolidate findings. If blockers found → send back to architect for revision.

---

### Step 3 — Human approves the plan
**Wait for explicit approval before proceeding.**
Do not implement until the human says "schváleno", "implementuj", "approve", or similar.
Present a short summary of what each reviewer noted.

---

### Step 4 — Test Scenario Design
Invoke the **test-scenario-designer** subagent to produce a full test plan:
- **Happy path** — standard usage, multiple input variations
- **Edge cases** — boundary values, empty collections, zero amounts, max precision
- **Validation** — invalid inputs, missing required fields, constraint violations
- **Error paths** — DB failures, external service errors, concurrency conflicts

Output: structured list of named test cases (no code yet).

---

### Step 5 — Test Scenario Validation
Invoke an independent **test-scenario-reviewer** subagent to validate the test plan:
- Are all scenarios necessary and well-defined?
- Are there missing cases (negative tests, auth, concurrency)?
- Are the expected outcomes correct?

If gaps found → send back to designer for revision before proceeding.

---

### Step 6 — Test Implementation Verification
Invoke the **test-implementation** subagent to verify:
- Every approved test scenario from Steps 4–5 has a corresponding test
- Tests are correct (right assertions, right coverage)
- If any scenario is missing or wrong → fix before proceeding

---

### Step 7 — Developer → Implementation (model: opus)
Invoke the **developer** subagent to implement using the approved plan and verified test scenarios:
- **TDD strictly:** write failing tests first (RED), then implement (GREEN), then refactor
- Tests must map 1:1 to the approved test scenarios
- Follow the plan exactly — no improvisation
- Fresh session: `claude --model opus --permission-mode bypassPermissions`

---

### Step 8 — Code Review (2 independent agents, run in parallel)
Invoke two **code-reviewer** agents simultaneously:
- **opus**
- **minimax-m2.5**

Both reviews are focused on:
1. **Does the implementation make sense?** — logic correctness, alignment with plan
2. **Efficiency and readability** — could it be written more cleanly or simply?
3. **Performance issues** — N+1 queries, unnecessary allocations, blocking I/O
4. **Visible bugs** — off-by-one, null handling, race conditions, incorrect calculations

Security and conventions are secondary — surface them but don't block on style.

---

### Step 8b — Review Triage
Invoke the **review-triager** subagent to consolidate findings from both code reviewers:
- For each finding: is it **valid and relevant**? (genuine issue vs. noise/style preference)
- Classify as: `blocker` | `improvement` | `nitpick` | `invalid`
- Produce a prioritized action list of issues worth fixing

---

### Step 8c — Fix Issues (model: opus)
Invoke the **developer** subagent to fix all `blocker` and `improvement` findings from the triage:
- Fix each issue exactly as identified — no scope creep
- Re-run tests after fixes to confirm nothing regressed

---

### Step 9 — Documentation
Invoke the **documentation** subagent to:
- Add or update XML doc comments on all changed/new public and internal members
- Update ADR if an architectural decision was made
- Update README if public-facing behavior changed

---

### Step 10 — Close issue + continue
- Close the GitHub issue
- Present plan for the next task

---

### Agents available (in `.claude/agents/`)
| Agent | When to use |
|-------|-------------|
| `orchestrator` | Starting a new feature with multiple subtasks |
| `architect` | Design, API contracts, data models — before any code |
| `plan-reviewer` | Reviewing architect's plan (3x in parallel: opus, sonnet, minimax) |
| `developer` | Implementation after plan is approved (opus) |
| `test-scenario-designer` | Full test plan before writing any test code |
| `test-scenario-reviewer` | Validating test plan completeness and correctness |
| `test-implementation` | Writing or verifying tests against approved scenarios |
| `code-reviewer` | After implementation (2x in parallel: opus, minimax) |
| `review-triager` | Consolidate code review findings, classify relevance, produce action list |
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

_v1.3 — 2026-04-03_
