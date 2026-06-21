# ReadLog (.NET)

A personal reading-log web app: search for books across **Open Library** and
**Google Books**, log what you finish with a format (book / audiobook / e-book),
a finished-on date and a 0–5 star rating, then browse, search, edit and delete
your personal library. There's also an account/stats page and a public
"recently read" feed.

This is an **idiomatic ASP.NET Core port** of the original
[Next.js + Prisma + Postgres ReadLog](https://github.com/MikkoNumminen/ReadLog).
The porting decisions (and *why* each one was made) are documented in
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

## Project structure

```
ReadLog.sln
Directory.Build.props        # solution-wide build settings (nullable, langversion, analysis)
global.json                  # pinned .NET SDK
docs/SOURCE-MAP.md           # behavioural map of the original Next.js app
PORTING-NOTES.md             # every significant .NET decision, with rationale
src/ReadLog.Web/             # the ASP.NET Core Razor Pages application
tests/ReadLog.Tests/         # xUnit tests
.github/workflows/ci.yml     # build + test on every push / PR
```

## Status

This repository is being built up in reviewed, PR-sized chunks
(scaffold → data layer → integrations → auth → CRUD → UI → Docker/deploy). See
the open/merged pull requests for progress.
