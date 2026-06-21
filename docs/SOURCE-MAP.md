# ReadLog — SOURCE-MAP.md

Authoritative source map for porting **ReadLog** from Next.js to **idiomatic ASP.NET Core (Razor Pages) + EF Core + SQLite + ASP.NET Core Identity (Google OAuth)**. Synthesized from a full read of the source tree: `prisma/schema.prisma`, `prisma.config.ts`, `src/lib/{db,auth,actions,openlibrary,googlebooks,bookdetails}.ts`, the App Router pages/conventions under `src/app/`, all client components under `src/components/`, `src/theme.ts`, and the config/infra files (`package.json`, `next.config.ts`, `vercel.json`, `jest.config.ts`, CI/Husky, `.env.example`).

---

## 1. Overview

ReadLog is a **personal reading-log web app**: signed-in users search for books across **two metadata providers (Open Library + Google Books, merged & deduplicated)**, log a finished book with a **reading format** (book / audiobook / e-book), a **finished-on date**, and an optional **0–5 star rating**, then browse, search, edit, and delete their **personal library**. It also surfaces an **account/stats page** (total books + per-format breakdown), a **public "Recently Read" feed** of the 20 newest logs across all users, and a **rich book-detail dialog** (Google Books description/categories/publisher/links). It is a **Next.js 16 / React 19 / TypeScript** app using **MUI 7** for UI, **Prisma 7** over **Neon serverless PostgreSQL**, **NextAuth v5** (Google OAuth, database-backed sessions), all business logic implemented as **server actions** in `src/lib/actions.ts` with **`unstable_cache` tag-based caching**. Tables are namespaced with a `readlog_` prefix so the app can share one Postgres database with other applications.

---

## 2. Domain model

The schema lives in `prisma/schema.prisma`. `datasource db.provider = "postgresql"`; the generated client is emitted to `src/generated/prisma` (gitignored) and imported as `@/generated/prisma/client`. **Every model and the enum is `@@map`'d to a `readlog_`-prefixed table name** (explicit design choice to share one Postgres instance with other apps). Cardinality: **`User 1—* ReadEntry *—1 Book`**; `ReadEntry` is the join carrying attributes (format / finishedAt / rating / notes). All primary keys are `@default(cuid())` strings.

### Table-name mappings

| Model | Table (`@@map`) | Notes |
|---|---|---|
| `User` | `readlog_user` | NextAuth identity |
| `Account` | `readlog_account` | NextAuth OAuth provider link/tokens |
| `Session` | `readlog_session` | NextAuth DB-backed sessions |
| `VerificationToken` | `readlog_verification_token` | NextAuth; **unused** with OAuth-only |
| `Book` | `readlog_book` | Shared catalog (not user-scoped) |
| `ReadEntry` | `readlog_entry` | User-scoped reading log |
| `Format` (enum) | `readlog_format` | Postgres enum `BOOK / AUDIOBOOK / EBOOK` |

### User → `readlog_user`

| Field | Type | Nullable | Default | Constraints / Indexes | Relations |
|---|---|---|---|---|---|
| `id` | String (cuid) | no | `cuid()` | `@id` | — |
| `name` | String | yes | — | — | — |
| `email` | String | yes | — | `@unique` | — |
| `emailVerified` | DateTime | yes | — | — | — |
| `image` | String | yes | — | — | — |
| `createdAt` | DateTime | no | `now()` | — | — |
| `updatedAt` | DateTime | no | — | `@updatedAt` | — |
| `accounts` | Account[] | — | — | — | 1—* Account |
| `sessions` | Session[] | — | — | — | 1—* Session |
| `readEntries` | ReadEntry[] | — | — | — | 1—* ReadEntry |

### Account → `readlog_account`

| Field | Type | Nullable | Default | Constraints / Indexes | Relations |
|---|---|---|---|---|---|
| `id` | String (cuid) | no | `cuid()` | `@id` | — |
| `userId` | String | no | — | FK → User.id | `user` (onDelete: **Cascade**) |
| `type` | String | no | — | — | — |
| `provider` | String | no | — | part of `@@unique([provider, providerAccountId])` | — |
| `providerAccountId` | String | no | — | part of `@@unique([provider, providerAccountId])` | — |
| `refresh_token` | String | yes | — | `@db.Text` | — |
| `access_token` | String | yes | — | `@db.Text` | — |
| `expires_at` | Int | yes | — | — | — |
| `token_type` | String | yes | — | — | — |
| `scope` | String | yes | — | — | — |
| `id_token` | String | yes | — | `@db.Text` | — |
| `session_state` | String | yes | — | — | — |

### Session → `readlog_session`

| Field | Type | Nullable | Default | Constraints / Indexes | Relations |
|---|---|---|---|---|---|
| `id` | String (cuid) | no | `cuid()` | `@id` | — |
| `sessionToken` | String | no | — | `@unique` | — |
| `userId` | String | no | — | FK → User.id | `user` (onDelete: **Cascade**) |
| `expires` | DateTime | no | — | — | — |

### VerificationToken → `readlog_verification_token`

| Field | Type | Nullable | Default | Constraints / Indexes | Relations |
|---|---|---|---|---|---|
| `identifier` | String | no | — | part of `@@unique([identifier, token])` | — |
| `token` | String | no | — | `@unique`; part of `@@unique([identifier, token])` | — |
| `expires` | DateTime | no | — | — | — |

> No `id` column. Unused under OAuth-only sign-in — safe to drop in the port.

### Book → `readlog_book` (shared catalog, not user-scoped)

| Field | Type | Nullable | Default | Constraints / Indexes | Relations |
|---|---|---|---|---|---|
| `id` | String (cuid) | no | `cuid()` | `@id` | — |
| `title` | String | no | — | `@@index([title])` | — |
| `author` | String | yes | — | — | — |
| `coverUrl` | String | yes | — | — | — |
| `openLibraryId` | String | yes | — | `@unique` | natural key for idempotent upsert |
| `pageCount` | Int | yes | — | — | — |
| `firstPublishYear` | Int | yes | — | — | — |
| `createdAt` | DateTime | no | `now()` | — | — |
| `readEntries` | ReadEntry[] | — | — | — | 1—* ReadEntry |

> `openLibraryId @unique` enables idempotent **upsert keyed on the work id**, reusing one catalog row across all users. Postgres (and SQLite) treat multiple NULLs as distinct in a unique index, so a null `openLibraryId` still upserts. The stored id is whatever the chosen search result carried: an Open Library `key` (e.g. `/works/OL1234W`), a `google:<id>`, or a client-generated `manual:<timestamp>`.

### ReadEntry → `readlog_entry` (user-scoped)

| Field | Type | Nullable | Default | Constraints / Indexes | Relations |
|---|---|---|---|---|---|
| `id` | String (cuid) | no | `cuid()` | `@id` | — |
| `userId` | String | no | — | `@@index([userId])`; part of `@@unique([userId, bookId, finishedAt])` | `user` (onDelete: **Cascade**) |
| `bookId` | String | no | — | part of `@@unique([userId, bookId, finishedAt])` | `book` (**no onDelete ⇒ Prisma default Restrict**) |
| `format` | Format | no | `BOOK` | — | — |
| `finishedAt` | DateTime | no | `now()` | `@@index([finishedAt])`; part of `@@unique([userId, bookId, finishedAt])` | — |
| `rating` | Int | yes | — | 0–5 stars (no DB-level range check) | — |
| `notes` | String | yes | — | `@db.Text` | — |
| `createdAt` | DateTime | no | `now()` | — | — |

> `@@unique([userId, bookId, finishedAt])` prevents the same user logging the same book finished at the same timestamp twice (a violation surfaces as a Prisma uniqueness error). `ReadEntry → User` cascades on user delete; `ReadEntry → Book` is **Restrict** (a Book referenced by entries cannot be deleted) — note `deleteReadEntry` removes only the entry, never the Book.

### Format enum → `readlog_format`

| Value |
|---|
| `BOOK` (default) |
| `AUDIOBOOK` |
| `EBOOK` |

### DB client & CLI config (no user flows)

- **`src/lib/db.ts`** builds a singleton `PrismaClient` via the Neon adapter: `new PrismaClient({ adapter: new PrismaNeon({ connectionString: process.env.DATABASE_URL! }) })`. Non-null assertion on `DATABASE_URL` (crashes at runtime if unset). Memoized on `globalThis.prisma` to avoid connection exhaustion across hot reloads; the global is only written when `NODE_ENV !== "production"`. Exported `prisma` is consumed by `auth.ts` (`PrismaAdapter(prisma)`) and `actions.ts`.
- **`prisma.config.ts`** (Prisma 7) loads `dotenv/config`, sets `schema: "prisma/schema.prisma"`, `migrations.path: "prisma/migrations"`, `datasource.url: process.env["DATABASE_URL"]`. **No `prisma/migrations` directory exists** — the project syncs schema with `prisma db push`, not versioned migrations. `package.json` runs `prisma generate` on `build` and `postinstall`.

---

## 3. Complete feature & user-flow inventory

### 3.1 Book search (dual-source, dedup, manual fallback)
- **Components:** `BookSearch.tsx` → `searchBooksAction` → `searchBooks` (Open Library) + `searchGoogleBooks` (Google Books).
- **Flow:** On `/log`, the user types a **title** (required) and optional **author**; the query is `[title.trim(), author.trim()].filter(Boolean).join(" ")`. Pressing **Enter** in either field runs the search (empty query is a no-op; non-Enter keys ignored). Both providers run concurrently via `Promise.allSettled`; results are concatenated (Open Library first) and **deduplicated**. Rows render an avatar (cover or `MenuBookIcon`), `title — subtitle`, and `author · firstPublishYear`. First 10 shown; **"Show N more results"** reveals the rest. Picking a row calls `onSelect(book)`. Zero results → **"No books found."** plus **`Add "<title>" manually`**, which synthesizes `{ openLibraryId: "manual:" + Date.now(), title, subtitle:null, author||null, firstPublishYear:null, pageCount:null, coverUrl:null }`.
- **Data touched:** None (external APIs only). No auth required.

### 3.2 Logging a read
- **Page/components:** `/log` (`src/app/log/page.tsx`, client) using `BookSearch` then an edit form → `logBook`.
- **Flow:** `/log` is client-gated: `useSession()`; a `useEffect` redirects to `/signin?callbackUrl=/log` when `status === "unauthenticated"`, and the page renders `null` until `authenticated` (brief blank flash). After selecting a result the user can edit **title/author** (prefilled), choose **format** (ToggleButtonGroup, default `BOOK`), set **"Finished on"** date (default today `YYYY-MM-DD`), and set a **MUI Rating** (precision 1, nullable). Save calls `logBook(...)` → on success `router.push("/library")`; on throw shows an Alert "Failed to save. Please try again."; `saving` disables the button.
- **Data touched:** `book.upsert` by `openLibraryId` (existing book untouched), then `readEntry.create` (`userId`, `bookId`, `format`, `finishedAt: new Date(string)`, `rating`). Invalidates `public-feed` + `my-books`.

### 3.3 Viewing the library
- **Page/components:** `/library` (`src/app/library/page.tsx`, server) → `getMyBooks` → renders `LibrarySearch` + `LibraryView`.
- **Flow:** Server component awaits `getMyBooks()`; **`null` (no session) → `redirect("/signin?callbackUrl=/library")`**. Entries serialized to `{ id, format, finishedAt, rating, book:{title,author,coverUrl} }`, ordered `finishedAt desc`. Empty → "No books logged yet. Start by logging your first book!". `LibraryView` offers a **grid/list toggle**: grid is a responsive 3/4/5-col grid of clickable covers with clamped title/author and a read-only Rating; list is outlined cards with cover, title/author/inline Rating, format Chip, date, and an Edit button.
- **Data touched:** `readEntry.findMany({ where:{userId}, include:{book:true}, orderBy:{finishedAt:"desc"} })`.

### 3.4 Editing an entry
- **Component:** `LibraryView.tsx` → `EditDialog` → `updateReadEntry`.
- **Flow:** Clicking a grid cell or the list Edit button opens a dialog with controlled **title** (TextField), **format** (ToggleButtonGroup), **finishedAt** (`type=date`, init from `finishedAt.toISOString().split("T")[0]`), **rating** (interactive Rating). A `hasChanges` guard compares each field (rating vs `entry.rating ?? null`); **Save is disabled when no changes or saving**. Save sends **only changed fields** to `updateReadEntry(entry.id, {...})`, then `router.refresh()` and close. No-change Save just closes.
- **Data touched:** If `title` set → `book.update` of **the shared `Book.title`** (affects every user's entry for that book). If `format || finishedAt || rating !== undefined` → `readEntry.update` of only provided fields (`finishedAt` wrapped `new Date`; `rating` applied even when `null`). Ownership enforced server-side. Invalidates `public-feed` + `my-books`.

### 3.5 Deleting an entry
- **Component:** `LibraryView.tsx` delete flow → `deleteReadEntry`.
- **Flow:** A `DeleteIcon` reveals an inline **"Delete this entry?"** confirmation; **"Yes, delete"** calls `deleteReadEntry(entry.id)` → `router.refresh()` → close; **"No"** cancels.
- **Data touched:** Ownership-checked `readEntry.delete` (the `Book` row is left intact). Invalidates `public-feed` + `my-books`.

### 3.6 Ratings (0–5 stars)
- **Where:** Set on the log form (3.2) and the edit dialog (3.4); rendered read-only in the public feed, library grid/list, and library-search cards.
- **Behavior:** `rating: Int?` nullable; MUI Rating precision 1. The update path uses a `!== undefined` guard so **`rating: null` clears it** while `rating: 0` is a legitimate stored value. No server-side range validation.

### 3.7 Account / stats page
- **Page:** `/account` (`src/app/account/page.tsx`, server) → `getAccountStats`.
- **Flow:** `await getAccountStats()`; falsy (no session) → `redirect("/signin?callbackUrl=/account")`. Renders Avatar (from live session `image`/`name`), name, email, big **totalBooks** count with singular/plural label, and per-format **Chips** (`BOOK→Books`, `AUDIOBOOK→Audiobooks`, `EBOOK→E-books`), **skipping zero counts**.
- **Data touched:** `readEntry.count({where:{userId}})` + `readEntry.groupBy({by:["format"], where:{userId}, _count:true})`. The aggregate is cached; **user identity (name/email/image) is read live from the session and merged in outside the cache**.

### 3.8 "Have I read this?" library lookup
- **Component:** `LibrarySearch.tsx` → `checkIfRead`.
- **Flow:** Single TextField; blank query on Enter clears results (`searched=false`, no action call); non-blank runs `checkIfRead(query)`. Zero + searched → "Not in your library."; results → "Yes! Found N match(es):" (singular `match` for 1) then outlined cards (cover, title, format Chip, `finishedAt`).
- **Data touched:** `readEntry.findMany({ where:{ userId, book:{ title:{ contains:query, mode:"insensitive" } } }, include:{book:true} })`. Returns `[]` if unauthenticated. **Not cached.**

### 3.9 Public feed
- **Page/components:** `/` (`src/app/page.tsx`, server) → `getRecentPublicReads` → `FeedList` → `BookDetailDialog`.
- **Flow:** Public (no auth). Renders the 20 newest reads across all users as `Card`/`CardActionArea`s: cover or placeholder, title, author, format Chip + `createdAt` (`toLocaleDateString`), read-only Rating if present. Empty → "No books logged yet. Be the first!". Clicking a card opens `BookDetailDialog` (see 3.11). The page defensively does `new Date(entry.createdAt).toISOString()` because `unstable_cache` can return dates as strings (commit `31858ca`).
- **Data touched:** `readEntry.findMany({ take:20, orderBy:{createdAt:"desc"}, include:{book:true} })`. **No user fields exposed.**

### 3.10 Auth: sign-in / sign-out / session
- **Files:** `src/lib/auth.ts`, `/api/auth/[...nextauth]/route.ts`, `/signin`, `Providers.tsx`, `NavBar.tsx`.
- **Flow:** **NextAuth v5** with a single **Google** provider and `PrismaAdapter` ⇒ **database-backed sessions** (no JWT/callbacks). `/signin` (client) reads `callbackUrl` (default `/`, via `useSearchParams` in `<Suspense>`) and calls `signIn("google", { callbackUrl })`. OAuth round-trip lands at `/api/auth/callback/google`; the adapter upserts `User`+`Account` and creates a `Session` row, sets the opaque session cookie, redirects to `callbackUrl`. `Providers` wraps the tree in `SessionProvider`; `NavBar` uses `useSession()` to toggle nav items and Sign in (`signIn("google")`) / Sign out (`signOut({callbackUrl:"/"})`). Server code reads identity via `auth()` (`session.user.{id,name,email,image}`).
- **Data touched:** `readlog_user`, `readlog_account` (OAuth tokens), `readlog_session`.

### 3.11 Book detail dialog (Google Books enrichment)
- **Component:** `BookDetailDialog.tsx` → `getBookDetails` → `fetchBookDetails`.
- **Flow:** Opened from a feed card. Content mounts only when `open` (fetch fires on open only), with a cancelled-guard cleanup. Loading shows a CircularProgress. On success: cover (`details.coverUrl || coverUrl`), authors joined `", "`, `Published:`/`Publisher:`/`<n> pages`/`Language: <UPPER>`, category Chips, **description via `dangerouslySetInnerHTML`** (raw Google Books HTML), and a **"More on Google Books"** external link (`rel="noopener noreferrer"`) when `infoLink` present. Null → "No details available for this book."
- **Data touched:** None (Google Books only); cached 30 days server-side.

### 3.12 App chrome & conventions (no business data)
- **Root layout** (`layout.tsx`): Geist font → `--font-geist-sans`; static metadata (title "ReadLog", description "Track books and audiobooks you've read", `metadataBase` **hardcoded** `https://read-log-pi.vercel.app`, openGraph + twitter `summary_large_image`); wrap order `AppRouterCacheProvider → ThemeProvider → CssBaseline → Providers → NavBar → children`.
- **Loading skeletons** (`app/`, `app/library/`, `app/log/` `loading.tsx`): identical centered `CircularProgress`.
- **Error boundary** (`error.tsx`, client): "Something went wrong" + "Try again" → `reset()` (does **not** display the error).
- **Not-found** (`not-found.tsx`, client): "Page not found" + "Go home" → `/`.
- **OG image** (`opengraph-image.tsx`): `next/og` `ImageResponse`, 1200×630 brown gradient (`#5c4033`→`#3e2723`), 📚 emoji, "ReadLog" + tagline.
- **Global CSS** (`globals.css`): only `body { margin: 0; }`.
- **Theme** (`theme.ts`): MUI `createTheme` — primary `#5D4E37`, secondary `#8B6914`, bg default `#FAFAF5` / paper `#FFFFFF`, font `var(--font-geist-sans), sans-serif`.

---

## 4. API / server-action surface

All server actions live in `src/lib/actions.ts` (`"use server"`). Auth via NextAuth `auth()`; persistence via Prisma. **"required"** = throws `Error("Not authenticated")` when no `session.user.id`. **"required (soft)"** = returns `null`/`[]` when unauthenticated (no throw).

| Name | Signature | Auth | Behavior | Cache tags |
|---|---|---|---|---|
| `searchBooksAction` | `(query: string) => Promise<BookSearchResult[]>` | none | `Promise.allSettled([searchBooks, searchGoogleBooks])`; rejected source → `[]`; concat (OL first) + `deduplicateResults`. Both fail → `[]` (no throw). | none |
| `logBook` | `(openLibraryId, title, author\|null, coverUrl\|null, pageCount\|null, firstPublishYear\|null, format, finishedAt: string, rating: number\|null) => {success:true}` | required | `book.upsert({where:{openLibraryId}, create:{...}, update:{}})` (existing untouched) → `readEntry.create({userId, bookId, format, finishedAt:new Date(finishedAt), rating})`. | invalidates `public-feed`, `my-books` |
| `getMyBooks` | `() => Promise<ReadEntry[] \| null>` | required (soft) | `null` if unauth; else cached `findMany({where:{userId}, include:{book:true}, orderBy:{finishedAt:"desc"}})`. | read `my-books` (revalidate 300s, key includes `userId`) |
| `checkIfRead` | `(query: string) => Promise<ReadEntry[]>` | required (soft) | `[]` if unauth; else `findMany({where:{userId, book:{title:{contains:query, mode:"insensitive"}}}, include:{book:true}})`. **Not cached.** | none |
| `updateReadEntry` | `(entryId, data:{title?, format?, finishedAt?, rating?:number\|null}) => {success:true}` | required | `findUnique` + ownership (`entry.userId !== session.user.id` → throw `"Not found"`). `title` truthy → `book.update`. `format \|\| finishedAt \|\| rating!==undefined` → `readEntry.update` with conditional spread (`finishedAt`→`new Date`; `rating` applied even when `null`). | invalidates `public-feed`, `my-books` |
| `deleteReadEntry` | `(entryId) => {success:true}` | required | `findUnique` + ownership (`"Not found"`), then `readEntry.delete` (Book kept). | invalidates `public-feed`, `my-books` |
| `getBookDetails` | `(title, author: string\|null) => Promise<BookDetails \| null>` | none | cached `fetchBookDetails`. `null` if no API key / no result. | read `book-details` (revalidate **2,592,000s / 30 days**) |
| `getAccountStats` | `() => Promise<{user:{name,email,image}, totalBooks, formats:Record<Format,number>} \| null>` | required (soft) | `null` if unauth; cached `count` + `groupBy(format)`; **user identity merged live (not cached)**. | read `my-books` (revalidate 300s, cache key `account-stats` + `userId`) |
| `getRecentPublicReads` | `() => Promise<ReadEntry[]>` | none | cached `findMany({take:20, orderBy:{createdAt:"desc"}, include:{book:true}})`. No user filter; **no user fields**. | read `public-feed` (revalidate 60s) |

**Internal helpers:** `normalize(str) = str.toLowerCase().replace(/[^a-z0-9]/g,"")`; `deduplicateResults` keys on `normalize(title)+"|"+normalize(author??"")`, keeping the higher-data duplicate (`score = (coverUrl?1:0)+(pageCount?1:0)`), ties keep first-seen (OL precedes Google). Cache wrappers `getCachedMyBooks` / `getCachedAccountStats` / `getCachedRecentPublicReads` / `cachedFetchBookDetails` are module-level `unstable_cache(fn, keyParts, {revalidate, tags})` — the wrapped fn's args participate in the cache key.

**Other routed surface (Next conventions):**

| Name | Kind | Auth | Behavior |
|---|---|---|---|
| `GET, POST` (`/api/auth/[...nextauth]`) | NextAuth catch-all (re-exports `handlers.GET/POST`) | none | Serves `/api/auth/{signin,callback/google,signout,session,csrf,providers}`. No custom logic. |
| `auth` / `signIn` / `signOut` / `handlers` | NextAuth exports (`src/lib/auth.ts`) | n/a | `auth()` = server session accessor (DB-backed, `session.user.id` populated); `signIn`/`signOut` server initiators; `handlers` HTTP route handlers. |
| `RootLayout` + `metadata` | Route convention exports | n/a | MUI/SessionProvider/NavBar chrome + static metadata. |
| `opengraph-image` exports | Route convention | n/a | `alt`, `size {1200,630}`, `contentType "image/png"`, default → `next/og` `ImageResponse`. |

### Cache tag → reader/invalidator map

| Tag | Read by | Invalidated by |
|---|---|---|
| `my-books` | `getMyBooks`, `getAccountStats` | `logBook`, `updateReadEntry`, `deleteReadEntry` |
| `public-feed` | `getRecentPublicReads` | `logBook`, `updateReadEntry`, `deleteReadEntry` |
| `book-details` | `getBookDetails` | never invalidated (30-day TTL only) |

---

## 5. External integrations

Three pure, stateless fetch+map modules (`src/lib/{openlibrary,googlebooks,bookdetails}.ts`). None touch the DB, do auth, or cache. They are consumed by `searchBooksAction` (fan-out + dedup) and `getBookDetails` (30-day cache).

### 5.1 Open Library Search — `searchBooks(query)`
- **Endpoint:** `GET https://openlibrary.org/search.json`
- **Params:** `q=query`, `limit=15`, `fields=key,title,subtitle,author_name,first_publish_year,number_of_pages_median,cover_i`
- **Guard:** `if (!query.trim()) return []` — **no fetch**.
- **Failure:** `if (!res.ok) throw new Error("Open Library API error")` — **throws** (status not included).
- **Parse/map** (`data.docs ?? []`): `openLibraryId = doc.key` (e.g. `/works/OL1234W`); `subtitle = doc.subtitle ?? null`; `author = doc.author_name?.[0] ?? null` (**first author only**); `firstPublishYear = doc.first_publish_year ?? null`; `pageCount = doc.number_of_pages_median ?? null`; `coverUrl = doc.cover_i ? `https://covers.openlibrary.org/b/id/${doc.cover_i}-M.jpg` : null`.
- **No API key.** Cover host `covers.openlibrary.org` is allowlisted in `next.config.ts`.

### 5.2 Google Books Search — `searchGoogleBooks(query)`
- **Endpoint:** `GET https://www.googleapis.com/books/v1/volumes`
- **Params:** `q=query`, `maxResults=15`, `key=GOOGLE_BOOKS_API_KEY`
- **Guard:** `if (!apiKey || !query.trim()) return []` — **no fetch**.
- **Failure:** `if (!res.ok) return []` — **does NOT throw** (contrast with Open Library).
- **Parse/map** (`data.items ?? []`): `openLibraryId = "google:" + item.id` (**namespaced prefix**); `author = v.authors?.[0] ?? null`; `firstPublishYear = v.publishedDate ? (parseInt(v.publishedDate.slice(0,4),10) || null) : null` (`"unknown"` → null); `pageCount = v.pageCount ?? null`; `coverUrl = v.imageLinks?.thumbnail?.replace("http:","https:") ?? null` (**http upgraded to https**). **Subtitle/series logic:** start `subtitle = v.subtitle ?? null`; if `v.seriesInfo?.bookDisplayNumber` exists → `subtitle = "Book " + num`, and if a subtitle already existed → `"Book " + num + " — " + subtitle` (**em dash U+2014**).
- Cover host `books.google.com` is allowlisted in `next.config.ts`.

### 5.3 Google Books Details — `fetchBookDetails(title, author)`
- **Endpoint:** same volumes endpoint, `maxResults=1`.
- **Query:** `[title, author].filter(Boolean).join(" ")` (title-only when author null).
- **Guards/failure:** `null` if no `apiKey` (no fetch), `null` if `!res.ok`, `null` if no `items[0]`.
- **Parse/map** `volumeInfo` → `BookDetails` with fallbacks to the passed args: `title = v.title ?? title`; `authors = v.authors ?? (author ? [author] : [])`; `description ?? null` (**raw HTML**); `categories ?? []`; `publisher ?? null`; `publishedDate ?? null` (**raw string, not parsed**); `pageCount ?? null`; `coverUrl = v.imageLinks?.thumbnail?.replace("http:","https:") ?? null`; `language ?? null`; `previewLink ?? null`; `infoLink ?? null`.

### 5.4 Merge & dedup (`searchBooksAction`)
`Promise.allSettled([searchBooks(query), searchGoogleBooks(query)])` — each **rejected** source contributes `[]`, so a single provider failure never fails the whole search (this is exactly why Open Library's `throw` matters and Google's silent `[]` differs). Results are concatenated **Open Library first, Google second**, then `deduplicateResults`: key `normalize(title)+"|"+normalize(author??"")`; on collision keep the higher `score = (coverUrl?1:0)+(pageCount?1:0)`; **ties keep first-seen (Open Library wins)**. Implementation is an insertion-ordered `Map`.

### 5.5 Shared DTOs
- `BookSearchResult = { openLibraryId, title, subtitle:string|null, author:string|null, firstPublishYear:number|null, pageCount:number|null, coverUrl:string|null }`
- `BookDetails = { title, authors:string[], description:string|null, categories:string[], publisher:string|null, publishedDate:string|null, pageCount:number|null, coverUrl:string|null, language:string|null, previewLink:string|null, infoLink:string|null }`

| Service | URL | Key | Params | Failure mode |
|---|---|---|---|---|
| Open Library search | `https://openlibrary.org/search.json` | none | `q, limit=15, fields=…` | **throws** on `!ok` |
| Open Library covers | `https://covers.openlibrary.org/b/id/{cover_i}-M.jpg` | none | URL only (not fetched server-side) | n/a |
| Google Books search | `https://www.googleapis.com/books/v1/volumes` | `GOOGLE_BOOKS_API_KEY` | `q, maxResults=15, key` | returns `[]` |
| Google Books details | same | `GOOGLE_BOOKS_API_KEY` | `q, maxResults=1, key` | returns `null` |

---

## 6. Caching & revalidation

ReadLog uses Next.js `unstable_cache` (read-side memoization keyed by `keyParts` + wrapped-fn args) plus `updateTag(...)` invalidation after mutations. **For a single-instance SQLite app most of this is optional** (you can query directly), but the **invalidate-on-write semantics** and the **per-user cache keying** should be preserved if you keep caching.

| Wrapper | Cache keyParts | Revalidate (TTL) | Tag | Genuinely needed for single-instance SQLite? | .NET mapping |
|---|---|---|---|---|---|
| `getCachedMyBooks(userId)` | `["my-books"]` + `userId` | 300s | `my-books` | **Droppable** — a `Where(userId).Include(Book)` query is cheap. Keep only as a hot-path optimization. | `IMemoryCache`/`HybridCache` key `my-books:{userId}`, 5-min TTL; evict on write |
| `getCachedAccountStats(userId)` | `["account-stats"]` + `userId` | 300s | `my-books` | **Droppable** — `CountAsync` + `GroupBy` is cheap. | key `account-stats:{userId}`; cache **only the aggregate**, merge live profile from `ClaimsPrincipal` |
| `getCachedRecentPublicReads()` | `["recent-public-reads"]` | 60s | `public-feed` | **Useful** — global hot read; a short-TTL cache or `OutputCache` is worthwhile. | `OutputCache`/`IMemoryCache` key `recent-public-reads`, 60s; evict on any write |
| `cachedFetchBookDetails(title, author)` | `["book-details"]` + `(title, author)` | **2,592,000s (30 days)** | `book-details` | **Worth keeping** — avoids repeat Google Books calls; never invalidated, plain TTL suffices. | `IMemoryCache`/`IDistributedCache` key `book-details:{title}|{author}`, `AbsoluteExpiration = 30 days` |

**Invalidation behavior to preserve:** `logBook`, `updateReadEntry`, `deleteReadEntry` each fire `updateTag("public-feed")` + `updateTag("my-books")`. Because `account-stats` is tagged `my-books`, **a library write must also evict that user's stats**. `book-details` is never invalidated. Next has tag-based invalidation; .NET has no built-in equivalent for `IMemoryCache` — model tags as **cache-key prefixes you evict explicitly**, or use a **`CancellationChangeToken` per tag**, or use **ASP.NET Core `OutputCache` with `EvictByTagAsync`** (the closest analog to `updateTag`). **Date gotcha:** `unstable_cache` may serialize `Date` as a string (commit `31858ca`), which is why pages re-parse with `new Date(...).toISOString()`; EF returns real `DateTime`, so this workaround is dropped in the port.

---

## 7. Configuration & environment variables

| Env var | Configures | Used by | .NET equivalent |
|---|---|---|---|
| `DATABASE_URL` | Postgres/Neon connection string (example uses `sslmode=disable`) | `db.ts`, `prisma.config.ts` | `ConnectionStrings:Default` (e.g. `Data Source=readlog.db`) — appsettings / env |
| `AUTH_SECRET` | NextAuth cookie/CSRF signing (`openssl rand -base64 32`) | NextAuth v5 (auto) | **Data Protection key ring** (no 1:1 secret); persist for multi-instance |
| `AUTH_GOOGLE_ID` | Google OAuth client id | NextAuth Google provider (auto) | `Authentication:Google:ClientId` (IOptions/user-secrets) |
| `AUTH_GOOGLE_SECRET` | Google OAuth client secret | NextAuth Google provider (auto) | `Authentication:Google:ClientSecret` |
| `GOOGLE_BOOKS_API_KEY` | Google Books search + details auth | `googlebooks.ts`, `bookdetails.ts` | `GoogleBooks:ApiKey` (IConfiguration/user-secrets); absent ⇒ empty/null, **no HTTP call** |
| `NODE_ENV` | Gates the Prisma global-singleton cache | `db.ts` | `IWebHostEnvironment.IsDevelopment()` |

**Non-env config of note:** `metadataBase` is **hardcoded** to `https://read-log-pi.vercel.app` in `layout.tsx` (should become configurable). `next.config.ts` allowlists image hosts `covers.openlibrary.org` + `books.google.com` (no Razor equivalent needed — render covers with plain `<img>`). Secrets live in `.env*` (gitignored; only `.env.example` committed). In .NET, move secrets to **appsettings.json + user-secrets (dev) + env vars (prod)** bound via `IOptions`.

---

## 8. Recommended PR-sized chunk plan

1. **Scaffold** — Create the ASP.NET Core Razor Pages project; add MUI-equivalent layout shell (`_Layout`, nav, brand colors `#5D4E37`/`#8B6914`/`#FAFAF5`, Geist font via CSS); wire `appsettings`/user-secrets, Serilog/logging, error + 404 pages.
2. **Data layer** — Define EF Core entities (`Book`, `ReadEntry`, `Format` enum) + `AppDbContext` with `readlog_*` table mappings, indexes, unique constraints, cascade/restrict delete behavior, string PK value generation; create the **initial EF migration** (replacing `db push`).
3. **External integrations** — Typed `IHttpClientFactory` clients `IOpenLibraryClient` / `IGoogleBooksClient` (+ details) returning `BookSearchResult`/`BookDetails` records; `IBookSearchService` reproducing the parallel fan-out, per-source try/catch failure modes, and dedup/scoring.
4. **Auth** — ASP.NET Core Identity + Google handler (`AddGoogle`), `IdentityDbContext` with `readlog_user`-style mapping, extend `IdentityUser` with `Name`/`Image`, custom `/signin` (single Google challenge), `LoginPath`, sign-out, `returnUrl` validation (`Url.IsLocalUrl`).
5. **CRUD / business** — `IReadLogService` methods for `LogBook` (find-or-create Book by `OpenLibraryId` + create entry), `GetMyBooks`, `CheckIfRead`, `UpdateReadEntry`/`DeleteReadEntry` (ownership-guarded, `NotFound` not `Forbidden`), `GetAccountStats` (Count + GroupBy + live profile merge), `GetRecentPublicReads`.
6. **UI** — Razor Pages: `/` feed, `/library` (grid/list + edit/delete modal via handlers + PRG), `/log` (search box JS/HTMX → JSON handler, then log form), `BookDetailDialog` → `OnGetBookDetails` JSON; antiforgery tokens, read-only/interactive star ratings.
7. **Account / feed** — `/account` (`[Authorize]`, stats + per-format chips) and the public feed polish; caching layer (`IMemoryCache`/`OutputCache`) with the tag→key eviction model + `book-details` 30-day TTL.
8. **Docker / deploy / notes** — Dockerfile (SQLite volume), `Migrate()` on startup, CI (`setup-dotnet` → restore/build/test+coverlet thresholds 80/80/70/method), deploy path-filter mirroring `vercel.json` excludes, README + porting notes (shared-Book-title hazard, `manual:`/`google:` id scheme).

---

## 9. Mapping table — Next.js / Prisma / NextAuth → idiomatic .NET

| Next.js / Prisma / NextAuth concept | Idiomatic ASP.NET Core equivalent |
|---|---|
| Server action (`"use server"` fn) | Razor Page handler (`OnGet*`/`OnPost*`) delegating to an injected service (`IReadLogService`/`IBookSearchService`) |
| `searchBooksAction` / `getBookDetails` (client-driven) | JSON page handler or minimal-API endpoint consumed by JS/HTMX/fetch |
| Prisma model | EF Core entity + `DbContext`; `@@map("readlog_*")` → `ToTable("readlog_*")` |
| `@default(cuid())` string PK | string PK via custom `ValueGenerator` (cuid/ulid) or `Guid` / Identity default; SQLite stores as TEXT |
| `Format` Postgres enum | C# `enum Format { Book, Audiobook, Ebook }`, `HasConversion<string>()` to keep `BOOK/AUDIOBOOK/EBOOK`, default `Book` |
| `@default(now())` / `@updatedAt` | `HasDefaultValueSql(...)` or `SaveChanges` interceptor (SQLite has no Postgres-identical `now()` default; set in code) |
| `@@unique([...])` / `@@index([...])` | `HasIndex(...).IsUnique()` / `HasIndex(...)` |
| `onDelete: Cascade` / default Restrict | `.OnDelete(DeleteBehavior.Cascade)` / `.OnDelete(DeleteBehavior.Restrict)` |
| `prisma.book.upsert({where:{openLibraryId}, update:{}})` | `FirstOrDefaultAsync(b => b.OpenLibraryId == id)` then Add if null, leave untouched if found; unique index + catch `DbUpdateException` for the insert race |
| `findMany({include:{book:true}})` | `.Include(e => e.Book)` |
| `contains` + `mode:"insensitive"` | `EF.Functions.Like(e.Book.Title, $"%{q}%")` (SQLite LIKE is ASCII case-insensitive) |
| `groupBy({by:["format"], _count})` / `count` | `.GroupBy(e => e.Format).Select(g => new {g.Key, Count = g.Count()})` / `CountAsync` |
| `unstable_cache` (read memoization) | `IMemoryCache` / `HybridCache` / `OutputCache` with composite keys baking in the fn args |
| `updateTag(tag)` invalidation | explicit `cache.Remove(key)`, `CancellationChangeToken` per tag, or `OutputCache.EvictByTagAsync(tag)` |
| NextAuth + `PrismaAdapter` (DB sessions) | ASP.NET Core Identity + EF stores + Identity **application cookie** (ticket) |
| NextAuth Google provider | `AddAuthentication().AddGoogle()` (ClientId/Secret from config) |
| `auth()` / `session.user.id` | `[Authorize]` + `User.FindFirstValue(ClaimTypes.NameIdentifier)`; `UserManager<ApplicationUser>` for the full row |
| `signIn("google",{callbackUrl})` | handler returns `Challenge(new AuthenticationProperties{RedirectUri=returnUrl}, GoogleDefaults.AuthenticationScheme)` |
| `signOut({callbackUrl:"/"})` | `SignOutAsync(IdentityConstants.ApplicationScheme)` + redirect `/` |
| `/api/auth/[...nextauth]` catch-all | framework callback (`/signin-google`) + Identity endpoints — no custom controller |
| `pages.signIn:"/signin"` | cookie `LoginPath = "/signin"` (custom Razor Page) |
| `callbackUrl` query param | `ReturnUrl` validated with `Url.IsLocalUrl` (open-redirect safety) |
| `User`/`Account`/`Session`/`VerificationToken` | `AspNetUsers` (extend with `Name`/`Image`) / `AspNetUserLogins` / application cookie / **drop** |
| `SessionProvider` + `useSession` (client) | server-rendered conditional markup on `User.Identity.IsAuthenticated` |
| `/log` client `useEffect` redirect | `[Authorize]` on the page (cookie middleware redirects to `LoginPath`; removes the blank flash) |
| `router.refresh()` after mutation | PRG: `RedirectToPage` after the POST so the page re-queries |
| `dangerouslySetInnerHTML` (book description) | `@Html.Raw(details.Description)` (consider HtmlSanitizer; source does not sanitize) |
| MUI theme / components | Razor + CSS (capture brand colors `#5D4E37`/`#8B6914`/`#FAFAF5`, Geist font) |
| `next/image` + `remotePatterns` allowlist | plain `<img src>` to `covers.openlibrary.org` / `books.google.com` (no proxy) |
| `next/og` `opengraph-image` | pre-rendered static `og-image.png` + meta tags (or ImageSharp/SkiaSharp) |
| `loading.tsx` Suspense fallbacks | drop / static loading partial (server render is synchronous) |
| `error.tsx` / `not-found.tsx` | `UseExceptionHandler("/Error")` + 404 status-code page (don't surface the raw error) |
| Neon serverless adapter (`PrismaNeon`) | `UseSqlite(connectionString)` (no adapter layer) |
| `prisma db push` / generated client | `dotnet ef migrations add` + `database update` / `context.Database.Migrate()` |
| `db.ts` globalThis singleton | `AddDbContext` (scoped lifetime; no hot-reload hack) |
| `.env` + `AUTH_*` secrets | `appsettings.json` + user-secrets (dev) + env (prod) via `IOptions` |
| Husky/lint-staged; pre-push tsc/eslint/jest | `dotnet format` pre-commit; `dotnet build` + `dotnet test` pre-push |
| GitHub Actions (node/npm/prisma/eslint/tsc/jest) | `setup-dotnet` → restore/build/test + coverlet (threshold `line,branch,method`) |
| `vercel.json` `ignoreCommand` | CI `paths-ignore` mirroring the same exclude set (README/TODO/CLAUDE/tests/.github/.husky/jest config/.gitignore) |

### Carry-over edge cases & hazards (must survive the port)
- **Ownership returns "Not found" (404), not "Forbidden" (403)** — deliberate existence-hiding; replicate with a single guarded query `FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId)` → `NotFound()`.
- **Shared-Book title mutation:** `updateReadEntry` with a title edits the shared `Book.title`, changing it for every user's entry of that book; `upsert update:{}` means the first logger's metadata wins. Decide keep-shared vs per-entry denormalization.
- **`finishedAt` is a `YYYY-MM-DD` string** parsed by JS `new Date()` as **UTC midnight**; store as UTC `DateTime`/`DateOnly` and honor `@@unique([userId, bookId, finishedAt])`.
- **Rating semantics:** `null` clears, `0` is valid (`!== undefined` guard) — preserve in the update path.
- **Account stats split:** cache only the aggregate; read name/email/image live from the `ClaimsPrincipal`.
- **Provider failure asymmetry:** Open Library **throws** on non-OK; Google Books returns empty/null — reproduce per-source try/catch so one provider failing still returns the other's results.
- **Id namespacing:** `OpenLibraryId` stores OL `key`, `google:<id>`, or `manual:<timestamp>`; keep it a nullable unique TEXT column tolerant of all three.
- **Public feed exposes no user fields** — keep the projection user-free.
