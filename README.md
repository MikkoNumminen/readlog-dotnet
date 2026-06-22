# ReadLog (.NET)

A personal reading-log web app: search for books across **Open Library** and
**Google Books**, log what you finish with a format (book / audiobook / e-book),
a finished-on date and a 0–5 star rating, then browse, search, edit and delete
your personal library. There's also an account/stats page and a public
"recently read" feed.

This is an **idiomatic ASP.NET Core port** of the original
[Next.js + Prisma + Postgres ReadLog](https://github.com/MikkoNumminen/ReadLog),
which runs live at **<https://read-log-pi.vercel.app/>**. The porting decisions
(and *why* each one was made) are documented in
[`PORTING-NOTES.md`](PORTING-NOTES.md); the original app's behaviour is mapped
in [`docs/SOURCE-MAP.md`](docs/SOURCE-MAP.md).

## Tech stack

| Concern        | Choice                                               |
| -------------- | ---------------------------------------------------- |
| Runtime        | .NET 8 (LTS)                                          |
| Web            | ASP.NET Core **Razor Pages**                         |
| Data           | **EF Core** + **SQLite** (code-first migrations)     |
| Auth           | ASP.NET Core **Identity** (local + optional Google)  |
| UI             | Razor + Bootstrap 5, themed with the ReadLog palette |
| Tests          | xUnit                                                |

## Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) (pinned in
  [`global.json`](global.json))

## Getting started

```bash
# restore + build
dotnet build

# run the web app
dotnet run --project src/ReadLog.Web
```

This starts the default `https` launch profile, listening on
`https://localhost:7232` and `http://localhost:5239`. Open either URL Kestrel
prints. The first time, trust the local dev certificate so the HTTPS URL loads
without a warning:

```bash
dotnet dev-certs https --trust
```

## Running the tests

```bash
dotnet test
```

## Configuration

Settings are bound from `appsettings.json` + environment variables (and user-secrets
in development). Nothing secret is committed.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | SQLite connection string (default `Data Source=readlog.db`) |
| `GoogleBooks:ApiKey` | enables Google Books search/details (absent ⇒ Open Library only) |
| `Authentication:Google:ClientId` / `ClientSecret` | enables the optional "Sign in with Google" button |

In development, put secrets in user-secrets rather than the file:

```bash
cd src/ReadLog.Web
dotnet user-secrets set "GoogleBooks:ApiKey" "<key>"
dotnet user-secrets set "Authentication:Google:ClientId" "<id>"
dotnet user-secrets set "Authentication:Google:ClientSecret" "<secret>"
```

## Deployment

### Docker

A multi-stage [`Dockerfile`](Dockerfile) builds on the SDK image and runs on
`mcr.microsoft.com/dotnet/aspnet:8.0` as a non-root user, listening on port `8080`:

```bash
docker build -t readlog .
docker run -p 8080:8080 -v readlog-data:/home/data readlog
# open http://localhost:8080
```

EF Core migrations are applied automatically on startup, so the SQLite database is
created on first run. The volume keeps it across container restarts.

### Azure App Service (F1 Linux)

The app is sized for the free **F1 Linux** tier (SQLite, no Postgres → no cold-start
database). Deploy the container, then set:

| App setting | Value |
| --- | --- |
| `WEBSITES_PORT` | `8080` |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE` | `true` (persist `/home`, where the SQLite file lives) |
| `ConnectionStrings__Default` | `Data Source=/home/data/readlog.db` (the Dockerfile default) |
| `GoogleBooks__ApiKey`, `Authentication__Google__*` | as needed |

Enable **HTTPS Only** on the App Service; the app honours `X-Forwarded-Proto` via
forwarded-headers middleware so auth cookies and links use the right scheme behind the
platform's TLS-terminating proxy.

> Note: `dotnet publish` and a Production run of the published app are verified locally;
> the Docker image build itself was not run in this environment (no Docker daemon available).

## Project structure

```
ReadLog.sln
Directory.Build.props        # solution-wide build settings (nullable, langversion, analysis)
global.json                  # pinned .NET SDK
Dockerfile / .dockerignore   # container build (aspnet:8.0 runtime, non-root)
docs/SOURCE-MAP.md           # behavioural map of the original Next.js app
PORTING-NOTES.md             # every significant .NET decision, with rationale
src/ReadLog.Web/             # the ASP.NET Core Razor Pages application
  Models/  Data/  Dtos/  Options/  Validation/  Auth/  Services/  Pages/
tests/ReadLog.Tests/         # xUnit tests (unit + integration)
.github/workflows/ci.yml     # build + test on every push / PR
```

## Status

The port is feature-complete: every feature of the original ReadLog is implemented,
the app builds and runs (`dotnet run`), EF Core migrations apply from a clean database,
the Dockerfile is in place, and the test suite is green. It was built in reviewed,
PR-sized chunks (scaffold → data layer → integrations → auth → CRUD → UI →
Docker/deploy); see the merged pull requests for the history.
