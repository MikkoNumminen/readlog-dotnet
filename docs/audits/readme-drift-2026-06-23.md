# README drift report — 2026-06-23

## Summary
- README audited: `README.md` (branch `docs/pr12-readme-refresh`, off `master`)
- Total drifts: 2 (stale: 1, missing: 1, unverifiable: 0)
- Rewrites applied: 2
- Rewrites skipped (voice match failed): 0
- Voice profile: `docs/audits/readme-drift-scratch.md` (cached: no — first run)

## Findings

### Stale claims (rewritten)

| Axis | README claim | Reality | Section rewritten | Voice-match attempts |
| --- | --- | --- | --- | --- |
| status | "feature-complete … the Dockerfile is in place" — deployment described only as a future/unreached chunk | App is **deployed and live** on Azure App Service F1 at <https://readlog-a2feef.azurewebsites.net/>, shipped by `deploy.yml`; local + Google auth work in production | "Status" | 1 |

### Missing additions (added)

| Axis | Added | Section | Voice-match attempts |
| --- | --- | --- | --- |
| file-structure | `CLAUDE.md`, `docs/DEPLOY.md`, `SECURITY.md`, `docs/THREAT-MODEL.md`, `docs/audits/` | "Project structure" tree | 1 |

### Skipped (voice match failed twice)

_None._

### Unverifiable claims (flagged, not touched)

| Claim | Why unverifiable | Suggested action |
| --- | --- | --- |
| "the test suite is green" | Relies on CI (no .NET SDK in this environment to re-run `dotnet test`) | Trust the CI badge / latest `ci.yml` run; leave as-is |

## Not drift (already current)
- Intro live-link line and the **Azure App Service** deploy subsection were synced in the prior PR (#11) and remain accurate.
- Tech-stack and Configuration tables match `Directory.Build.props` / `appsettings.json` / `Program.cs`.

## Notes
- Scope was the five standard axes (file-structure, dependency, skill, feature, status). No restructure, tagline, or voice changes were made — the existing tone (technical, em-dash + `→`/`⇒`, "PR-sized chunks", test suite "green") was preserved.
- `skill-drift` axis: N/A (not a skills repo).
