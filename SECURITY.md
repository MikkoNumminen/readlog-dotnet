# Security

The security model for ReadLog (.NET). This is a personal reading-log app built as
a .NET learning exercise; the model below is what is actually implemented in the
code, plus the limits of the current **demo posture**. A full attacker-by-attacker
analysis lives in **[docs/THREAT-MODEL.md](docs/THREAT-MODEL.md)**.

## Reporting a vulnerability

Report privately — please **do not open a public issue** for a security problem.
Use a [GitHub private security advisory](https://github.com/MikkoNumminen/Readlog-c-.net/security/advisories/new)
on the repository so the maintainer can triage it before disclosure.

## Scope

`master` is the only supported line. The app is a single ASP.NET Core 8 process
with a local SQLite database; there is no multi-tenant or admin surface.

## Security model (as implemented)

### Authentication & sessions
- **ASP.NET Core Identity** over EF Core. Local email/password is the primary path;
  Google is an **optional** external login that is wired only when
  `Authentication:Google:*` is configured.
- Sessions are a **signed auth cookie** (not DB session rows): 14-day expiry,
  sliding. There is no long-lived bearer token to leak.
- **Lockout** throttles online password guessing: 5 failed attempts → 5-minute
  lockout (`Program.cs`).
- Password policy: ≥ 8 chars, `RequireUniqueEmail = true`.

### Authorization (the IDOR boundary)
- User pages are `[Authorize]`d; the cookie middleware redirects unauthenticated
  requests to `/signin?ReturnUrl=…` server-side.
- Every per-entry operation scopes by the acting user: queries are
  `Id == entryId && UserId == userId`, so a non-owner gets **404, not 403** and
  cannot even confirm an entry exists. `ReadLogService` never reads `HttpContext` —
  the page passes the authenticated `userId` in.

### Account-takeover guard
External (Google) login **refuses to auto-link** to a pre-existing *local* account
that merely shares the email address; the user must sign in locally first. An
email-string match is not proof of ownership, so silent linking is rejected.

### Web-surface hardening
- **CSRF**: state changes are POST forms protected by antiforgery tokens; a **GET to
  `/signout` does not sign the user out** (regression-tested).
- **XSS**: Google Books descriptions are untrusted HTML — sanitized via
  `BookDescriptionSanitizer` (HtmlSanitizer) before `@Html.Raw`, with the `target`
  attribute stripped to prevent reverse-tabnabbing. No other raw-HTML rendering.
- **Open redirect**: all post-auth redirects use `LocalRedirect`, rejecting
  off-site URLs in a crafted `ReturnUrl`.
- **SQL injection**: EF Core parameterizes all queries; the "have I read this?"
  `LIKE` search escapes the user's `% _ \` (with `ESCAPE '\'`) so the term can't
  inject wildcards.
- **Mass assignment**: input is bound to explicit request DTOs with DataAnnotations
  (`[Required]`, `[StringLength]`, `[Range(0,5)]`, `[Url]`, `[NotInFuture]`) — only
  intended fields are accepted, and a rating is bounded both in the DTO and by a DB
  check constraint.

### Transport
- HTTPS redirection + HSTS (outside Development).
- `UseForwardedHeaders` is the first middleware so that, behind a TLS-terminating
  proxy (Azure App Service), the original `https` scheme drives redirects and cookie
  flags. Known-network/proxy lists are intentionally cleared because the platform is
  the sole ingress; **do not deploy the container with a different ingress topology
  without revisiting this.**

### Secrets
Nothing secret is committed. The Google API key and OAuth client id/secret come from
**user-secrets** (development) or environment variables / app settings (production).
`appsettings.json` carries only a default SQLite connection string.

## Known limitations (demo posture)

These are deliberate for a runnable demo and are **not** production-safe as-is:

- **No email confirmation** (`RequireConfirmedAccount = false`, no email sender).
  Consequences: registration can be used to **enumerate** which emails have accounts
  (Identity's default duplicate-email message), and unverified emails are accepted.
  Production must enable confirmation with a configured sender.
- **No second factor** and a modest password policy.
- **No per-endpoint rate limiting** beyond Identity lockout.
- **Single-instance assumptions**: `IMemoryCache` (not distributed) and a local
  SQLite file — fine for one instance, not for a scaled-out deployment.

See **[docs/THREAT-MODEL.md](docs/THREAT-MODEL.md)** for the threat-by-threat
breakdown and what production hardening would add. The must-not-weaken invariants are
also summarized for contributors in **[CLAUDE.md](CLAUDE.md)**.
