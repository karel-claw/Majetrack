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

## What Not To Do

- ❌ Pin versions without a reason
- ❌ Write code without tests
- ❌ Guess how something works — look it up
- ❌ Use `#region` — ever

---

_v1.1 — 2026-04-03_
