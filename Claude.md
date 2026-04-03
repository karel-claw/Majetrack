# Claude.md — Projektové Instrukce pro Claude Code Agenty

_Tento soubor slouží jako system prompt pro všechny Claude Code agenty spawnované v workspace. NAČTI SI tento soubor před začátkem jakékoliv práce._

---

## 🎯 Základní Principy

### Vždy Nejnovější Verze
- **VŽDY používej nejnovější stable verze balíčků a knihoven** — žádné pinned verze, žádné legacy
- Pokud existuje novější verze, použij ji (pokud není explicitně řečeno jinak)
- Před začátkem projektu zkontroluj npm/pypi/Cargo.toml na nejnovější verze

### TDD — Test-Driven Development
1. Nejdřív napiš testy (RED)
2. Napiš kód aby prošly (GREEN)
3. Refaktoruj (REFACTOR)
4. Přidávej edge cases do testů

### Dokumentace a Best Practices
- Používej **Context7 MCP** pro relevantní dokumentaci
- Web search pro best practices, doporučené postupy, comparison články
- Vždy cituj zdroje — nikdy nevymýšlej fakta z hlavy

---

## 🛠️ Development Stack (.NET / C#)

### Package Manager
- **NuGet** — oficiální zdroj balíčků
- Verze: vždy nejnovější stable

### Testing Frameworky
- **xUnit** — moderní, native async support
- **FluentAssertions** — for fluent assertions
- **Moq** nebo **NSubstitute** — mocking

### Best Practices (.NET 8+)
- **Minimal APIs** pro menší služby
- **Carter** pro route handlers
- **MediatR** pro CQRS pattern
- **Dapper** pro data access (ne EF pokud není nutné)
- **YAML** konfigurace místo JSON

### Code Style
- C# 12+ syntaxe (primary constructors, collection expressions)
- **Obsaheno** pattern matching
- **Records** pro immutable DTOs
- Nullable reference types: **zapnuté**

---

## 🛠️ Development Stack (JavaScript / TypeScript)

### Package Manager
- **pnpm** — preferovaný (rychlý, disk-space efficient)
- **npm** — fallback

### Testing
- **Vitest** — rychlý, Vite integrace
- **Jest** — pokud Vitest nejde použít
- **Playwright** pro E2E

### Best Practices
- **ESLint + Prettier** — vždy
- **TypeScript** — strict mode
- **Vite** — build tool
- **TanStack Query** — server state management
- **Zod** — runtime validation

---

## 🔍 Research Kroky (povinné!)

Před začátkem jakékoliv featury:

1. **Context7 MCP** — oficiální dokumentace knihovny/frameworku
2. **Web search** — "best practices [technology] 2025/2026", "[library] vs alternatives"
3. **GitHub issues** — known bugs, workarounds
4. **Stack Overflow / Reddit** — real-world experience

---

## 📝 Commit Messages

```
feat: Add user authentication flow
fix: Resolve memory leak in WebSocket handler  
chore: Update dependencies to latest
refactor: Extract payment logic to separate service
docs: Add API documentation
test: Add unit tests for user service
```

---

## 🏗️ Project Structure

```
/src
  /Domain          # Entities, Value Objects
  /Application    # Use Cases, Interfaces
  /Infrastructure # DB, External APIs
  /Presentation   # API, Controllers
/tests
  /Unit
  /Integration
  /E2E
```

---

## ⚡ Quick Reference

| Stack | Testing | Mocks | Best For |
|-------|---------|-------|---------|
| .NET 8+ | xUnit | Moq | APIs, Libraries |
| Node/TS | Vitest | Vitest mocks | React, Services |
| Python | pytest | pytest-mock | Scripts, Data |

---

## 🚫 Co Nikdy Nedělat

- ❌ Pinovat verze bez důvodu (pouze pokud broken change)
- ❌ Používat legacy frameworky (ASP.NET Core 2.x, Express bez TS)
- ❌ Psát kód bez testů
- ❌ Vymýšlet "jak to asi funguje" — vždy ověřit

---

_Datum: 2026-04-03 | Verze: 1.0_