# README voice profile (drift-sync cache)

Extracted from `README.md` head (stable across runs unless first ~500 chars change).

- **Tone register** — technical, precise, lightly opinionated; assumes .NET fluency.
  > "This is an **idiomatic ASP.NET Core port** of the original"
- **Humor** — essentially absent; dry and factual. No jokes, no exclamation.
- **Pronoun choice** — impersonal for description, second person for instructions.
  > "Open either URL Kestrel prints."
- **Sentence rhythm** — medium-length, clausal, em-dashes and parentheticals.
  > "The porting decisions (and *why* each one was made) are documented in…"
- **Vocabulary tells** — "port", "idiomatic", "feature-complete", "PR-sized chunks",
  test suite is "green", "wired"; backticks for every path/command; **bold** on key
  terms; arrows `→` for sequences and `⇒` for implications.
  > "scaffold → data layer → integrations → auth → CRUD → UI → Docker/deploy"
  > "absent ⇒ Open Library only"
- **Structural patterns** — `##`/`###` statement headers (not questions); tables for
  config/stack; fenced code blocks introduced by a `# comment`; bold lead-ins.
- **Reference style** — educational-concise; links to the deeper docs rather than
  inlining detail ("see [`PORTING-NOTES.md`]").

Voice-match test for any rewrite: *could this paragraph sit in the existing README
unnoticed?* Keep em-dashes, `→`/`⇒`, "green", "PR-sized chunks", bold key terms.
