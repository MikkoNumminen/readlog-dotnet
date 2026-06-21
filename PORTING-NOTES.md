# PORTING-NOTES

Why ReadLog was built the way it was, in .NET. The original app is a Next.js 16 /
React 19 / TypeScript / Prisma 7 / Postgres app; this is an **idiomatic** port to
ASP.NET Core — not a transliteration. Each section explains a decision and the
reasoning behind it so it can be defended in a technical interview. The source
behaviour being matched is catalogued in [`docs/SOURCE-MAP.md`](docs/SOURCE-MAP.md).

The notes are written chunk-by-chunk as the port progresses; the heading for each
pull request marks what that PR introduced.

---

## PR1 — Scaffold & project structure

### Target architecture (locked)

- **ASP.NET Core 8 (LTS)**, full server-side .NET.
- **Razor Pages** for the UI.
- **EF Core + SQLite**, code-first **migrations** (no raw SQL).
- **ASP.NET Core Identity** for authentication.
- Deploy-ready for **Azure App Service F1 Linux** (`dotnet publish` + a Dockerfile
  on the `mcr.microsoft.com/dotnet/aspnet` base image).

### Why .NET 8 (and not 9/10)

.NET 8 is an **LTS** release with the broadest, most battle-tested package matrix
(EF Core 8, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 8,
`Microsoft.EntityFrameworkCore.Sqlite` 8 all ship and interoperate cleanly). The
machine already had the 8.0 runtime, and an LTS line means security/servicing
support without chasing STS upgrades. The TFM is centralised
(`Directory.Build.props`) so moving to a newer LTS later is a one-line change.
The exact SDK is pinned in `global.json` for reproducible local + CI builds.

### Why Razor Pages (not MVC or Blazor)

ReadLog is a small, **page-centric CRUD app**: a feed page, a library page, a log
page, an account page, a couple of auth pages. Razor Pages' page-per-URL model
(`Pages/Library.cshtml` + `LibraryModel`) maps almost 1:1 onto the original's
file-based App Router routes and keeps request handlers next to their markup —
less ceremony than MVC controllers for this shape of app. Blazor Server was
considered and deliberately **not** chosen: it brings a stateful circuit and a
component runtime that this mostly-static, form-driven app doesn't need. (If the
component model is wanted later, the page handlers/services stay the same and only
the view layer changes.)

### Why a single web project + a test project (not Clean Architecture layers)

The brief is explicit: *proportionate structure, no pointless abstraction*. A
four-project Domain/Application/Infrastructure/Web split would add three project
boundaries and a pile of interfaces for an app with two real entities. Instead:

```
src/ReadLog.Web/      # entities, DbContext, services, DTOs, Razor Pages — fol-dered, not project-split
tests/ReadLog.Tests/  # xUnit
```

Internal **folders** (`Models`, `Data`, `Services`, `Dtos`, `Pages`) give
separation of concerns without project sprawl. Services are still registered
through DI behind interfaces where that buys testability — the line is "abstract
at seams worth testing/mocking", not "abstract everything".

### UI stack: Bootstrap, themed (the MUI replacement)

The original uses MUI 7 (a React component library) with a brown palette
(`#5D4E37` / `#8B6914`, bg `#FAFAF5`). The idiomatic ASP.NET Core equivalent is
the template's **Bootstrap 5** (grid, cards, modals, badges, form + validation
styling) re-themed with CSS custom properties to the same palette
(`wwwroot/css/site.css`). This keeps us on the standard ASP.NET Core front-end —
including jQuery unobtrusive validation, which the DataAnnotations story plugs
into — rather than hand-rolling a component system.

### Build hygiene

- **`Directory.Build.props`** centralises `TargetFramework`, `LangVersion=latest`,
  `Nullable=enable`, `ImplicitUsings=enable`, and analyzers for the whole solution.
- **Nullable reference types are on**, and **nullable warnings are errors**
  (`WarningsAsErrors=nullable`) — nullability is treated as a correctness
  guarantee, not advice.
- `AnalysisLevel=latest-recommended` + `EnforceCodeStyleInBuild` turn the .NET
  analyzers on at build time.

### CI

`.github/workflows/ci.yml` restores, builds in Release, and runs the test suite
(with coverage collection) on every push/PR to `develop`/`master`. The scaffold
ships a `WebApplicationFactory<Program>` smoke test that boots the real app and
asserts the home page renders — so "it compiles" and "it actually starts" are both
gated from PR1 onward.

---

## PR2 — Data layer (EF Core + SQLite)

### EF Core vs Prisma

Prisma is a schema-first ORM: you write `schema.prisma`, run codegen, and get a
generated client. EF Core is **code-first** here: the C# entity classes + the
`DbContext` fluent configuration *are* the schema, and `dotnet ef migrations`
diffs the model to produce migration code. Key mapping:

| Prisma | EF Core |
| --- | --- |
| `schema.prisma` model | POCO entity class (`Models/Book.cs`, …) |
| generated `PrismaClient` | `ApplicationDbContext` (hand-written, DI-injected) |
| `prisma db push` | `dotnet ef migrations add` + `Database.Migrate()` |
| `@@index` / `@@unique` | `HasIndex(...).IsUnique()` in `OnModelCreating` |
| `@relation(onDelete:)` | `.OnDelete(DeleteBehavior.Cascade/Restrict)` |
| `@default(now())` | a `SaveChanges` override (see below) |
| Postgres `enum` | C# `enum` + `HasConversion<string>()` |

### DbContext + DI

`ApplicationDbContext` is registered with `AddDbContext` (scoped lifetime — one
context per request, the EF Core default). It inherits `IdentityDbContext<ApplicationUser>`
so the Identity tables (`AspNetUsers`, `AspNetUserLogins`, …) and the app tables
live in one context and one migration. The original's `db.ts` `globalThis`
singleton (a workaround for Next.js hot-reload) has no equivalent — DI owns the
lifetime.

### Entity decisions

- **`int` primary keys for `Book`/`ReadEntry`** instead of the original `cuid()`
  strings. cuid is a JS-ecosystem convention; for a single SQLite database,
  autoincrement `int` keys are the idiomatic, compact EF choice. `ApplicationUser`
  keeps Identity's `string` GUID key.
- **`ApplicationUser : IdentityUser`** carries the original `User`'s extra
  profile fields (`Name`, `Image`, `CreatedAt`). The NextAuth `Account` /
  `Session` / `VerificationToken` tables are dropped — Identity owns logins
  (`AspNetUserLogins`) and sessions are an auth cookie, not DB rows (see PR4).
- **`FinishedAt` is a `DateOnly`**, not a `DateTime`. The original stored a
  `YYYY-MM-DD` string parsed to UTC-midnight; `DateOnly` says exactly that
  ("the day it was finished") and is the idiomatic .NET type. `CreatedAt` stays
  a `DateTime` (a real instant, used to order the public feed).
- **`Format` is a C# `enum` persisted as its string name** (`HasConversion<string>()`),
  so the column reads `Book`/`Audiobook`/`Ebook` rather than an opaque ordinal.
- **`required string Title`** — with nullable reference types on, `required`
  enforces the non-null invariant at construction time, matching the schema.

### Table naming — dropped the `readlog_` prefix

The original `@@map("readlog_*")` existed only so the app could share one Postgres
instance with other apps. A dedicated SQLite file has no such constraint, so the
port uses idiomatic default table names (`Books`, `ReadEntries`, `AspNetUsers`).

### `CreatedAt` auditing

Rather than a database default (SQLite's `CURRENT_TIMESTAMP` is only
second-precision and provider-specific), entities implement a small `ICreatedAt`
marker and `ApplicationDbContext` overrides `SaveChanges`/`SaveChangesAsync` to
stamp `CreatedAt = DateTime.UtcNow` on insert. This is provider-agnostic, gives
full precision, and is a common EF auditing pattern.

### A constraint the original lacked

`ReadEntry.Rating` gets a DB **check constraint** (`Rating IS NULL OR 0..5`).
The original enforced the 0–5 range only in the UI; defence-in-depth at the
schema is cheap and correct. The null-vs-0 semantics (null = unrated, 0 = a real
rating) are preserved.

### Migrations, design-time factory, startup migrate

`dotnet ef` is pinned in a committed local tool manifest (`.config/dotnet-tools.json`).
A `DesignTimeDbContextFactory` lets the tooling build the context **without booting
the app**, so `migrations add` never trips the startup migration. At runtime,
`Database.Migrate()` runs on startup so a clean database is created/upgraded
automatically — satisfying "migrations work from a clean DB".

### SQLite foreign keys in tests

EF Core enables `PRAGMA foreign_keys=ON` on the connections it opens, so
cascade/restrict work in the app. Tests that share a hand-opened in-memory
connection set `Foreign Keys=True` explicitly, and exercise delete behaviour in a
*fresh* context (untracked dependents) so the assertion verifies the **database**
constraint, not EF's client-side cascade.

---

## Roadmap (documented as each PR lands)

- **PR3 — Integrations:** typed `HttpClient` + `IHttpClientFactory`, async fan-out
  (`Task.WhenAll` vs `Promise.allSettled`), the provider failure asymmetry.
- **PR4 — Auth:** how NextAuth (Google + DB sessions) maps to ASP.NET Core Identity
  + the application cookie + optional Google external login.
- **PR5 — CRUD/business:** services + DI, DTOs + DataAnnotations validation,
  ownership checks (404-not-403), rating null-vs-0 semantics, `IOptions`, `ILogger`.
- **PR6 — UI:** Razor Pages, PRG, antiforgery, ratings, the book-detail view.
- **PR7 — Docker/deploy:** multi-stage Dockerfile, Azure App Service F1 Linux,
  startup migration.
