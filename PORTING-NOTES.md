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

## PR3 — Book-search integrations

### Typed `HttpClient` instead of `fetch`

The original calls `fetch()` directly. The idiomatic .NET equivalent is a **typed
`HttpClient`** registered through `IHttpClientFactory` (`AddHttpClient<IClient, Client>`):
`OpenLibraryClient` and `GoogleBooksClient` each get an `HttpClient` with a configured
`BaseAddress`, timeout and (for Open Library) a `User-Agent`. The factory pools and
recycles the underlying handlers, avoiding socket exhaustion — the problem the
original's environment hid behind serverless. Each client exposes a small interface
so the search/details services depend on an abstraction, not on HTTP.

### JSON deserialization

`System.Text.Json` with `PropertyNameCaseInsensitive = true` plus `[JsonPropertyName]`
on the wire DTOs maps Open Library's `snake_case` (`author_name`,
`number_of_pages_median`, `cover_i`) and Google's `camelCase`. The wire DTOs are
`private sealed record`s living next to their client — they're an implementation
detail, not part of the app's surface (the app speaks `BookSearchResult` / `BookDetails`).

### Async + cancellation

Every I/O method is `async` and takes a `CancellationToken` (defaulted), threaded
through `GetAsync`/`ReadFromJsonAsync`. This is the .NET norm and lets a dropped
request abort the outbound HTTP call — something the original couldn't express.

### The provider-failure asymmetry, preserved

The original relies on `Promise.allSettled`: Open Library **throws** on a non-OK
response while Google **returns `[]`**, and the settled wrapper means one failing
provider never sinks the search. The port keeps that exact shape:

- `OpenLibraryClient` throws `HttpRequestException` on non-success;
- `GoogleBooksClient` returns `[]`/`null`;
- `BookSearchService` starts both tasks, awaits them with `Task.WhenAll`, and wraps
  each in a `try/catch` that logs and degrades to `[]` — the `allSettled` equivalent.
  Open Library is concatenated first, so it wins de-dup ties.

### De-dup + scoring

Ported faithfully: normalise `title|author` (lower-case, strip non-alphanumerics via
a source-generated `[GeneratedRegex]`), keep the first occurrence's position but
upgrade to the richer duplicate (score = has-cover + has-page-count). A
`List` + `Dictionary<key,index>` reproduces the JS `Map`'s insertion-order-preserving
"replace value, keep position" semantics. "First or null" on the provider lists uses
**list patterns** (`is [var first, ..]`) rather than `FirstOrDefault`.

### Configuration via `IOptions`

The Google Books API key moves from `process.env.GOOGLE_BOOKS_API_KEY` to
`GoogleBooksOptions` bound from the `GoogleBooks` config section and injected as
`IOptions<GoogleBooksOptions>`. Absent key ⇒ the Google integration is skipped with
no HTTP call (same as the original). In dev the key belongs in user-secrets, in prod
in an env var / app setting — never in `appsettings.json`.

### Caching details with `IMemoryCache`

`getBookDetails` used `unstable_cache` with a 30-day TTL. The port uses
`IMemoryCache` with a 30-day absolute expiration, keyed by a
`("book-details", title, author)` **ValueTuple** (structural equality — no
delimiter-collision risk a `"{title}|{author}"` string would carry), with the
components trimmed + lower-cased so case/whitespace variants share an entry.
One deliberate improvement: **only non-null results are cached**, so a transient
failure or a missing API key is retried rather than cached as "no details" for a
month. (`IMemoryCache` is right for a single-instance app; a multi-instance deploy
would swap in `IDistributedCache`/`HybridCache` behind the same interface.)

### Testing

A `StubHttpMessageHandler` returns canned JSON / status codes and records requests,
so the client tests assert mapping, the cover-URL build, `http→https`, the
series-subtitle logic, year parsing, the throw-vs-empty contracts, and that a blank
query or missing key makes **no HTTP call**. The service tests use hand-written stub
clients to prove de-dup/scoring, Open-Library-first ordering, the failure resilience,
and the cache (hit, null-not-cached, per-(title,author) keying). 30 tests total.

---

## PR4 — Authentication (ASP.NET Core Identity)

### NextAuth → ASP.NET Core Identity

The original uses **NextAuth v5** with a single **Google** provider and the Prisma
adapter (database-backed sessions). The brief says: if there are users/login, use
**ASP.NET Core Identity** — so that's the home for auth here.

| NextAuth | ASP.NET Core Identity |
| --- | --- |
| `User` table | `AspNetUsers` (`ApplicationUser : IdentityUser`) |
| `Account` (OAuth link/tokens) | `AspNetUserLogins` |
| `Session` (DB session rows) | the **application auth cookie** (a signed ticket, not DB rows) |
| `VerificationToken` | dropped (token providers exist if ever needed) |
| `auth()` / `session.user.id` | `User.FindFirstValue(ClaimTypes.NameIdentifier)` + `[Authorize]` |
| `signIn("google")` | `Challenge(props, GoogleDefaults.AuthenticationScheme)` |
| `signOut()` | `SignInManager.SignOutAsync()` |
| `pages.signIn:"/signin"` | cookie `LoginPath = "/signin"` |

### Local accounts *and* Google (a deliberate departure)

The original is Google-only. The port adds **local email/password accounts as the
primary path, with Google as an optional external login**. Why:

- It runs out-of-the-box with **no Google credentials** — register with an email
  and you're in. Requiring a live Google OAuth client just to sign in would make
  the app un-runnable for a reviewer.
- Local accounts + external providers is exactly Identity's bread-and-butter, so
  this is *more* idiomatic, not less.
- Google still maps faithfully: it registers **only when configured**
  (`Authentication:Google:ClientId`/`Secret` present), so the "Sign in with Google"
  button appears when, and only when, credentials exist.

### Why `AddIdentity` (not `AddIdentityCore`) and no scaffolded UI

`AddIdentity<ApplicationUser, IdentityRole>()` wires all three Identity cookie
schemes (application, external, two-factor) automatically — the external-login
challenge/callback depends on the external cookie, and rolling that by hand with
`AddIdentityCore` is more error-prone for no gain. Roles are unused but their tables
are inert. The default Identity **UI** package (Bootstrap-themed Razor Class Library)
is **not** used; instead the Login/Register/Logout/ExternalLogin pages are written
by hand. That keeps them on the ReadLog theme and is more educational — they show
`SignInManager.PasswordSignInAsync`, `UserManager.CreateAsync`, and the external
challenge/callback flow explicitly.

### The external-login flow

`Login.OnPostExternalLogin` issues a `Challenge` to Google with a redirect to the
`ExternalLogin` callback. The callback (`OnGetCallbackAsync`) signs the user in if
the login is already linked. Otherwise: if **no** local account exists for the
email, it provisions one from the provider's email/name claims, links the login
(`AddLoginAsync`), and signs in; if a local account **already** exists, it
**refuses to auto-link** and tells the user to sign in locally first. That refusal
is an account-takeover guard — an email-string match alone is not proof of
ownership, so silently attaching the provider to a pre-existing password account
would let an attacker who pre-created an account hijack it.

### Safety + config

- **Open-redirect protection**: every post-auth redirect uses `LocalRedirect`,
  which rejects non-local URLs — so a crafted `?returnUrl=https://evil` can't bounce
  the user off-site.
- **Brute-force throttling**: password sign-in uses `lockoutOnFailure: true` and
  Identity lockout is configured (5 attempts → 5-minute lockout), so online password
  guessing is bounded.
- **Display name without a DB hit**: a custom `UserClaimsPrincipalFactory`
  (`DisplayNameClaimsPrincipalFactory`) emits a `display_name` claim from
  `ApplicationUser.Name` at sign-in; the navbar reads that claim (falling back to the
  email) rather than querying the database on every render.
- **No new migration**: the Identity tables were already created by PR2's
  `InitialCreate` (the context was `IdentityDbContext` from the start), so PR4 is
  pure behaviour — no schema change.
- **Demo posture (and its limits)**: `RequireConfirmedAccount = false` (no email
  sender is wired up) and a relaxed-but-sane password policy (≥8 chars). Two known
  consequences of skipping email confirmation: registration can be used to *enumerate*
  which emails have accounts (Identity's default duplicate-email message), and
  unverified emails are accepted. Production would require email confirmation (with a
  configured sender), which closes both.

### Testing

Ten tests drive the real pages over HTTP — extracting the **antiforgery token** from
each rendered form and carrying cookies across requests — covering register-signs-in,
the register→logout→login round-trip, wrong-password rejection, password-mismatch and
duplicate-email rejection, the display-name greeting, and that a **GET** to `/signout`
does *not* sign the user out (CSRF safety). The integration host swaps the
`ApplicationDbContext` onto an isolated temp SQLite file via `ConfigureTestServices`
(an earlier connection-string override silently didn't apply, so tests had been
sharing — and accumulating state in — the real `readlog.db`; the swap makes every run
hermetic). The Google path is wired but not integration-tested (that needs an OAuth
double), and the `[Authorize]`→`/signin` challenge is exercised in PR6 against the
first real protected page. 54 tests total.

---

## Roadmap (documented as each PR lands)

- **PR5 — CRUD/business:** services + DI, DTOs + DataAnnotations validation,
  ownership checks (404-not-403), rating null-vs-0 semantics, `IOptions`, `ILogger`.
- **PR6 — UI:** Razor Pages, PRG, antiforgery, ratings, the book-detail view.
- **PR7 — Docker/deploy:** multi-stage Dockerfile, Azure App Service F1 Linux,
  startup migration.
