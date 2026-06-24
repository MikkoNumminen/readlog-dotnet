# Threat model — ReadLog (.NET)

A STRIDE-style threat model for the ASP.NET Core port. It records the assets, trust
boundaries, and attacker model, then walks each threat category with the **vector**,
the **mitigation as actually implemented** (with the file that owns it), and the
**residual risk**. The summary policy is in [../SECURITY.md](../SECURITY.md); the
*why* behind each design choice is in [../PORTING-NOTES.md](../PORTING-NOTES.md).

> Posture: this is a personal reading-log app and a learning exercise, deployed (when
> deployed) as a single ASP.NET Core 8 instance with a local SQLite database. Several
> entries below are accepted risks appropriate to that posture and are flagged
> **demo** where production would harden further.

## Assets

| Asset | Why it matters |
| --- | --- |
| User credentials (password hashes, Google login links) | Account compromise |
| A user's reading library (entries, ratings, dates) | Private until the user is in the public feed |
| Auth cookie | A valid cookie *is* the session |
| Google Books API key / Google OAuth secret | Abuse / quota theft if leaked |
| Database integrity (uniqueness, ownership, rating bounds) | Correctness of every feature |

## Trust boundaries & data flow

1. **Browser → app** (HTTPS, via the platform's TLS-terminating proxy in prod). All
   user input crosses here: forms, query strings, the `ReturnUrl`, the Google
   description that is later rendered.
2. **App → SQLite** (in-process file). EF Core is the only writer.
3. **App → external APIs** (Open Library, Google Books) over outbound HTTPS to fixed
   base addresses. Responses (titles, authors, cover URLs, HTML descriptions) are
   **untrusted input** that re-enters the app.
4. **App → Identity provider** (Google OAuth), only when configured.

The acting `userId` is established by the auth cookie at boundary 1 and is the
authorization principal for everything at boundary 2.

## Attacker model

- **Anonymous internet user** — can hit public pages and the auth endpoints.
- **Authenticated user** — can try to reach *another* user's data (IDOR), over-post
  fields, or inject via search/title input.
- **Malicious external API response** — a compromised/spoofed provider returning
  hostile HTML or URLs.
- **Network attacker** — passive/active on the wire.
- Out of scope: a host/OS compromise, a malicious maintainer, supply-chain
  compromise of NuGet packages, physical access to the SQLite file.

## Threats (STRIDE)

### S — Spoofing (identity)
| Vector | Mitigation (where) | Residual |
| --- | --- | --- |
| Online password guessing | Identity **lockout** 5/5-min; ≥8-char password (`Program.cs`) | No MFA; weak passwords still allowed — **demo** |
| Session forgery | Signed Identity auth cookie; 14-day sliding expiry | Cookie theft via a client compromise (out of scope) |
| Unverified identity at signup | — | **No email confirmation** → unverified emails accepted; production enables confirmation — **demo** |
| OAuth account hijack | External login **refuses to auto-link** to an existing local account by email (`Pages/Account/ExternalLogin`) | None for the linking path |

### T — Tampering
| Vector | Mitigation (where) | Residual |
| --- | --- | --- |
| CSRF on state changes | Antiforgery tokens on POST forms; **GET `/signout` is a no-op** (tested) | None expected |
| Mass assignment / over-posting | Bind to explicit request DTOs with DataAnnotations, not entities (`Dtos/`) | None expected |
| Out-of-range / future data | `[Range(0,5)]`, `[NotInFuture]`, page/year ranges, **plus a DB check constraint** on rating (`ApplicationDbContext`) | None expected |
| SQL injection | EF Core parameterization; `LIKE` search escapes `% _ \` with `ESCAPE '\'` (`ReadLogService.CheckIfReadAsync`) | None expected |
| Clickjacking / UI redress | No sensitive one-click GET action; every mutation requires an antiforgery POST; **`X-Frame-Options: DENY` + CSP `frame-ancestors 'none'`** set (Program.cs response-headers middleware) | None expected |

### R — Repudiation
| Vector | Mitigation (where) | Residual |
| --- | --- | --- |
| "I didn't do that" | `ILogger` for notable server events (e.g. lost create-races); `CreatedAt` stamped on insert | No audit trail of user mutations — accepted for a personal app |

### I — Information disclosure
| Vector | Mitigation (where) | Residual |
| --- | --- | --- |
| IDOR — reading another user's entry | Ownership scoping `Id && UserId`, returns **404 not 403** (`ReadLogService`) | None expected |
| Public feed leaking identity | `PublicReadDto` carries **no user fields** (`ReadLogService.GetRecentPublicReadsAsync`) | The fact that *someone* read a book is public by design |
| Account enumeration | — | Duplicate-email message + no email confirmation reveal which emails exist — **demo** |
| Error detail leakage | `UseExceptionHandler` + themed error page (request id, no stack) outside Development | None expected |
| Secret leakage | Secrets in user-secrets / env only; not in `appsettings.json` or git | Operator misconfiguration |

### D — Denial of service
| Vector | Mitigation (where) | Residual |
| --- | --- | --- |
| Slow/failing upstream API stalls requests | Typed `HttpClient` **10 s timeout** + `IHttpClientFactory` handler pooling; per-request `CancellationToken` (`Program.cs`, clients) | A flood still consumes the single instance |
| Repeated expensive feed/search | Public feed cached 60 s in `IMemoryCache`, evicted on write; book-details cached 30 days | No general per-endpoint rate limiting — **accepted** |
| DB write contention | — | SQLite single-writer; fine for one instance, not for scale-out — **demo** |

### E — Elevation of privilege
| Vector | Mitigation (where) | Residual |
| --- | --- | --- |
| Reaching an admin/role surface | There is none; Identity roles exist but are inert | None |
| Acting as another user | `[Authorize]` + per-user scoping on every entry op | None expected |

## Untrusted-content specifics (provider responses)

- **HTML descriptions (XSS):** Google Books descriptions are sanitized with
  HtmlSanitizer before `@Html.Raw`, and the `target` attribute is stripped to block
  reverse-tabnabbing (`Services/BookDescriptionSanitizer`, `Pages/Book`). Tested
  against XSS payloads.
- **Cover URLs:** provider-supplied URLs are rendered as `<img src>` and upgraded
  `http→https`. Residual: a hostile provider could point an image at an arbitrary
  host (client-side fetch — a privacy/tracking vector, not server SSRF). Accepted.
- **Open redirect:** `LocalRedirect` rejects a non-local `ReturnUrl`.
- **SSRF:** outbound calls go only to fixed `BaseAddress`es (openlibrary.org,
  googleapis.com); user input never chooses the host. Not exposed.

## Production hardening checklist

If this graduated from demo to production, do these (and update SECURITY.md):

- [ ] Enable email confirmation (`RequireConfirmedAccount = true`) with a real email
      sender — closes account enumeration **and** unverified-email signup.
- [ ] Add a generic, identical response for register/duplicate to remove enumeration.
- [ ] Add MFA and/or a stronger password policy; consider a breached-password check.
- [ ] Add per-endpoint rate limiting (ASP.NET Core rate limiter) on auth + search.
- [ ] Move to a server RDBMS (Postgres/SQL Server) and `IDistributedCache`/`HybridCache`
      if scaling beyond one instance (the EF data layer is provider-swappable).
- [x] Add a Content-Security-Policy and the standard security headers. *(Done — CSP with
      strict `script-src 'self'`, plus X-Content-Type-Options, Referrer-Policy, X-Frame-Options;
      Program.cs response-headers middleware.)*
- [ ] Add audit logging for security-relevant mutations if accountability is needed.
