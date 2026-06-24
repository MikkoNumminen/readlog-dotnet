# CLAUDE.md

Guidance for AI agents (and humans) working in this repository. Read this first,
then the deep references it points to. Keep it accurate — if you change behaviour
that contradicts a rule below, update this file in the same change.

## What this is

**ReadLog (.NET)** — an idiomatic **ASP.NET Core port** of a Next.js + Prisma +
Postgres reading-log app ([original](https://github.com/MikkoNumminen/ReadLog),
live at <https://read-log-pi.vercel.app/>). Users search books across **Open
Library** + **Google Books**, log finished reads (format, finished-on date, 0–5
rating), browse/search/edit/delete their library, see account stats, and a public
"recently read" feed.

### Prime directive — this is a learning exercise

The artifact is not the point: **the code must be idiomatic .NET, not TypeScript
transliterated into C#.** A transliteration is a failure. When you add or change
code, write it the way an experienced .NET developer would (DI, async + 
`CancellationToken`, `IOptions`, `ILogger`, DTOs + DataAnnotations, EF Core
migrations, nullable-as-error) — and be able to defend the choice. The original's
behaviour is faithfully reproduced; the *implementation* is .NET-native.

## Standing rules for agents

- **Do NOT merge to `master` or trigger a deploy without the owner's explicit
  go-ahead.** (The port is merged and the initial deploy was authorized and is **live** —
  see *Status*; further deploys are gated by the `production` GitHub Environment.)
- Work in **PR-sized chunks**, open a PR per chunk, **review your own work
  critically and fix every finding** before considering it done. Branches are
  named `feat/prN-<topic>`; `develop` is the integration branch and currently
  equals `master`.
- Treat the build as a contract: **nullable warnings are errors**
  (`Directory.Build.props`), analyzers run at build (`AnalysisLevel=latest-recommended`,
  `EnforceCodeStyleInBuild`). Don't suppress — fix.

## Commands

```bash
dotnet build                              # restore + build (Release in CI)
dotnet run --project src/ReadLog.Web      # https://localhost:7232  http://localhost:5239
dotnet test                               # xUnit; the whole suite
dotnet dev-certs https --trust            # once, so the HTTPS dev URL loads cleanly
dotnet format --verify-no-changes         # local format check (.editorconfig); complements build-time EnforceCodeStyleInBuild; drop the flag to auto-fix

# EF Core migrations (dotnet ef is pinned in .config/dotnet-tools.json)
dotnet tool restore
dotnet ef migrations add <Name> --project src/ReadLog.Web
# A DesignTimeDbContextFactory builds the context without booting the app, so
# `migrations add` never trips the startup Database.Migrate().
```

SDK is pinned in `global.json` (8.0.422, `rollForward: latestFeature`). Target is
**.NET 8 (LTS)**, set once in `Directory.Build.props`.

## Layout

Two projects, no Clean-Architecture sprawl — separation is by **folder**, not by
project (deliberate; the app has two real entities).

```
src/ReadLog.Web/
  Program.cs            # DI + middleware pipeline (the whole composition root)
  Models/               # Book, ReadEntry, ApplicationUser, Format enum, ICreatedAt
  Data/                 # ApplicationDbContext (: IdentityDbContext), Migrations, design-time factory
  Dtos/                 # immutable record DTOs in/out of services
  Options/              # GoogleBooksOptions (IOptions-bound)
  Validation/           # NotInFutureAttribute, etc.
  Auth/                 # ClaimsPrincipal helpers, DisplayName claims factory
  Services/             # ReadLogService (domain) + BookSearch/BookDetails + External/ HTTP clients
  Pages/                # Razor Pages (GET reads, POST mutates, redirect after)
tests/ReadLog.Tests/    # xUnit: Services (unit) + Pages/Auth/Smoke (integration over HTTP)
```

## Architecture at a glance

- **Razor Pages**, server-rendered, **PRG** (GET to read, POST to mutate, redirect
  after). No SPA/JS interactivity — the original's live search/dialogs/`router.refresh`
  became GET query params + server round-trips.
- **EF Core + SQLite**, code-first migrations. `ApplicationDbContext :
  IdentityDbContext<ApplicationUser>` — app tables and Identity tables share one
  context + one migration. `Database.Migrate()` runs on startup, so a clean DB just
  works.
- **DI lifetimes**: `DbContext` and the domain/search services are **scoped**; the
  external API clients are **typed `HttpClient`s** via `IHttpClientFactory`;
  `IMemoryCache` and the HTML sanitizer are **singletons**.
- **`ReadLogService` is HTTP-free**: every method takes the acting `userId` as a
  parameter. Pages enforce auth with `[Authorize]` and pass `User.GetUserId()` in.
  Keep it that way — it's what makes the service unit-testable without a web host.
- **Auth**: ASP.NET Core Identity, **local email/password as the primary path**,
  Google as an **optional** external login that registers *only* when
  `Authentication:Google:*` is configured.

The *why* behind every one of these is in **[PORTING-NOTES.md](PORTING-NOTES.md)**
(organised per PR). The original feature/file → C# mapping is in
**[docs/SOURCE-MAP.md](docs/SOURCE-MAP.md)**.

## Invariants — do not break these

Behaviour that is load-bearing and easy to regress. Most are enforced in
`Services/ReadLogService.cs` and `Data/ApplicationDbContext.cs`, and each is pinned by a
test under `tests/ReadLog.Tests/` (chiefly `Services/ReadLogServiceTests.cs` and
`Pages/UiPagesTests.cs`) — break one and `dotnet test` shows which.

- **Ownership returns 404, not 403.** Edit/delete combine existence + ownership in
  one query (`Id == id && UserId == userId`); a non-owner gets the same "not found"
  as a stranger and can't tell the entry exists.
- **One read per `(UserId, BookId, FinishedAt)`** — a unique index. Logging a
  duplicate throws `DuplicateReadEntryException` out of the service (the page maps
  it to a friendly "already logged"). Don't swallow it in the service.
- **`Book` is a shared catalogue row** keyed by a unique `OpenLibraryId`. Logging
  find-or-creates it (first logger's metadata wins). The create tolerates a unique-
  index **race**: on `DbUpdateException`, detach and re-fetch the winner; if there's
  no winner, **re-throw** (don't mask a locked-DB failure).
- **Shared catalogue `Book.Title` is read-only from the entry edit path** (a deliberate
  divergence from the original, where editing an entry's title mutated the shared `Book`
  for *every* user of that book). The edit path changes only per-user fields
  (`Format`/`FinishedAt`/`Rating`); the catalogue title is set once at log time.
- **Rating: `null` = unrated, `0` = a real rating** — both round-trip. A DB check
  constraint enforces `Rating IS NULL OR 0..5`.
- **Delete removes the `ReadEntry`, never the shared `Book`.** FKs: deleting a user
  cascades to their entries; a `Book` referenced by entries is `Restrict` (can't be
  deleted).
- **"Have I read this?" is case-insensitive `LIKE`** with the user's `% _ \`
  escaped (no wildcard injection); the query is trimmed; blank short-circuits to
  empty.
- **Public feed leaks no user identity:** `PublicReadDto` carries no user fields.
  It's cached in `IMemoryCache` for 60 s and **evicted on every write**
  (`_cache.Remove(PublicFeedCacheKey)` in log/update/delete — the analogue of the
  original's `updateTag`). The cache-populate uses `CancellationToken.None`, not the
  triggering request's token (the entry serves all readers).
- **Book-details cache:** 30-day TTL keyed by a `(title, author)` tuple; **only
  non-null results are cached** (so a transient miss is retried, not cached as "none").
- **Provider-failure asymmetry (preserved):** `OpenLibraryClient` *throws* on a
  non-OK response; `GoogleBooksClient` and `HardcoverClient` return `[]`.
  `BookSearchService` degrades each to `[]` independently (the `Promise.allSettled`
  equivalent) and concatenates **Open Library first** (Hardcover last), so Open Library
  wins de-dup ties. Hardcover is skipped entirely when its API token is absent.
- **`CreatedAt` is stamped in code**, not by the DB — `SaveChanges`/`SaveChangesAsync`
  override stamps `ICreatedAt.CreatedAt = DateTime.UtcNow` on insert.

## Security invariants — do not weaken

- **Open-redirect protection:** every post-auth redirect uses `LocalRedirect`.
- **Account-takeover guard:** external (Google) login **refuses to auto-link** to a
  pre-existing *local* account by email match; the user must sign in locally first.
- **Brute-force throttling:** Identity lockout (5 failures → 5-minute lockout),
  `RequireUniqueEmail`, password ≥ 8.
- **CSRF:** mutations are POST forms with antiforgery; a **GET to `/signout` does not
  sign out** (there's a test for this). Keep mutations off GET.
- **XSS:** Google Books descriptions are untrusted HTML — run them through
  `BookDescriptionSanitizer` (HtmlSanitizer) before `@Html.Raw`, and the `target`
  attribute is stripped (reverse-tabnabbing). Never `@Html.Raw` unsanitised input.
- **Demo posture, known limits:** `RequireConfirmedAccount = false` (no email sender
  wired) ⇒ registration can enumerate which emails have accounts, and unverified
  emails are accepted. Production needs email confirmation; don't present the current
  posture as production-hardened.

## Testing

- **xUnit.** Service tests run `ReadLogService` over an **in-memory SQLite** context
  (no web host). Integration tests drive the **real pages over HTTP** via
  `WebApplicationFactory<Program>` (`Program` is `partial` so the test host can boot
  it), extracting the **antiforgery token** from each rendered form and carrying
  cookies across requests.
- Each integration test gets an **isolated temp SQLite file** via
  `ConfigureTestServices` — runs are hermetic; don't reintroduce sharing of the real
  `readlog.db`.
- HTTP integrations are tested with a `StubHttpMessageHandler` (canned responses,
  records requests) — assert no HTTP call happens for a blank query or a missing key.
- Run `dotnet test` before opening a PR; CI gates the same.

## Configuration

Bound from `appsettings.json` + env vars + (dev) user-secrets. Nothing secret is
committed.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | SQLite (`Data Source=readlog.db` locally) |
| `GoogleBooks:ApiKey` | enables Google Books search/details (absent ⇒ skipped) |
| `Hardcover:ApiToken` | enables the Hardcover search provider (absent ⇒ skipped) |
| `Authentication:Google:ClientId` / `ClientSecret` | enables the Google login button |

In dev: `cd src/ReadLog.Web && dotnet user-secrets set "GoogleBooks:ApiKey" "<key>"`
(and `"Hardcover:ApiToken" "<token>"` to enable Hardcover).

## CI

`.github/workflows/ci.yml`: on push/PR to `master`/`develop` — `dotnet restore` →
`build --configuration Release --no-restore` → `test --configuration Release
--no-build --collect:"XPlat Code Coverage"`.

`.github/workflows/deploy.yml`: **manual-only** (`workflow_dispatch`) — builds the
image, pushes to ghcr.io, deploys the container to App Service via OIDC. The `deploy`
job runs in a `production` GitHub Environment (add a required reviewer to gate it).
Setup + caveats live in **[docs/DEPLOY.md](docs/DEPLOY.md)**.

## Status / deploy

Port is **feature-complete and merged to `master`** (full xUnit suite green, 0 warnings),
and **deployed live** (with the owner's go-ahead) to **Azure App Service F1 Linux**:
**<https://readlog-a2feef.azurewebsites.net/>**. It runs the container from
`ghcr.io/mikkonumminen/readlog` (public image), built and shipped by the manual,
reviewer-gated `deploy.yml` (OIDC, no stored creds); full runbook in
**[docs/DEPLOY.md](docs/DEPLOY.md)**. App settings in place: `WEBSITES_PORT=8080`,
`WEBSITES_ENABLE_APP_SERVICE_STORAGE=true`,
`ConnectionStrings__Default=Data Source=/home/data/readlog.db`, **HTTPS Only**, plus
`Authentication__Google__*` (Sign in with Google) and `GoogleBooks__ApiKey` (Google Books
search/details enabled). The live SQLite holds the **real reading history imported from the
original's Neon Postgres** (see **[docs/DATA-MIGRATION.md](docs/DATA-MIGRATION.md)**;
non-owner readers anonymized). Hosting is **$0** (F1 is
always-free; a `readlog-zero-guard` budget alerts on any spend). Known caveat: SQLite on
the App Service network share is officially unsupported (fine for this single-instance
demo; not production-grade — a managed DB is the upgrade path).

## The doc set

- **CLAUDE.md** (this file) — orientation, commands, rules, invariants. Start here.
- **[PORTING-NOTES.md](PORTING-NOTES.md)** — every significant .NET decision, with
  rationale, per PR. Read before changing an architectural choice.
- **[docs/SOURCE-MAP.md](docs/SOURCE-MAP.md)** — the original Next.js app's behaviour
  mapped to its C# counterpart. Read before touching feature parity.
- **[docs/DEPLOY.md](docs/DEPLOY.md)** — the deployment runbook: CI/CD pipeline,
  one-time Azure bootstrap, OIDC setup, free-tier caveats. Read before deploying.
- **[docs/DATA-MIGRATION.md](docs/DATA-MIGRATION.md)** — the one-off Neon Postgres →
  Azure SQLite data migration (ETL, identity mapping, safe file-swap). Read before
  re-importing or touching the live database.
- **[README.md](README.md)** — human-facing setup, configuration, deployment.
