# DEPLOY.md — shipping ReadLog to Azure App Service

How ReadLog gets to production, end to end. The application artifact is already
deploy-ready (see [CLAUDE.md → Status](../CLAUDE.md) and
[README → Docker / Azure](../README.md)); this doc covers everything *around* it:
the CI/CD pipeline, the one-time Azure bootstrap it depends on, and the operational
caveats of the free tier.

**Target:** Azure **App Service, Free F1, Linux**, running ReadLog as a **custom
container** pulled from **GitHub Container Registry (ghcr.io)**.

> **Cost: $0.** Every component of the documented path is free — F1 App Service plan,
> a **public** ghcr.io package, OIDC auth, and GitHub Actions minutes. There is **no
> Azure Container Registry** (that would cost ~USD 5/mo) and no paid tier anywhere on
> this path. The paid options in [Upgrading off the demo tier](#upgrading-off-the-demo-tier)
> are strictly optional and **not** part of going live.

> **Authorization.** Deployment is gated. The `Deploy` workflow is
> **manual-only** (`workflow_dispatch`) and the `deploy` job runs inside a
> `production` GitHub Environment — add a **required reviewer** to that environment
> so every run pauses for approval. Nothing ships without a human clicking *Run*
> and (optionally) approving.

---

## The pipeline at a glance

[`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml), triggered
manually:

1. **build-and-push** — builds the multi-stage `Dockerfile` and pushes two tags to
   `ghcr.io/mikkonumminen/readlog`: `:latest` and `:<commit-sha>` (the immutable
   ref the deploy actually uses).
2. **deploy** — `azure/login` via **OIDC** (no stored credentials), then
   `azure/webapps-deploy` points the web app at the pinned `:<sha>` image and
   restarts it. EF Core migrations run on container startup, so the SQLite DB is
   created/upgraded on first boot.

The image name is hard-coded lowercase (`ghcr.io/mikkonumminen/readlog`) rather than
derived from `${{ github.repository }}` — the repo slug `Readlog-c-.net` contains
characters that aren't a valid container reference.

---

## One-time bootstrap (do this once, before the first deploy)

All `az` commands below are run **by you** (they need your Azure login and
subscription). Run them in a shell with the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
installed; in a Claude Code session you can run an interactive login with
`! az login`.

> **Shell note.** These snippets use **bash** syntax (`$VAR`, `$(…)`, `<<` heredocs).
> On Windows use **Git Bash** (this project's primary shell is PowerShell, where the
> variable and quoting syntax differs). The OIDC step in particular passes JSON to
> `az` via a file (`--parameters @file`) precisely because inline JSON mangles quotes
> under PowerShell.

Pick names once and reuse them:

```bash
RG=readlog-rg
LOCATION=westeurope
PLAN=readlog-plan
APP=readlog-$RANDOM           # must be globally unique → becomes <APP>.azurewebsites.net
IMAGE=ghcr.io/mikkonumminen/readlog:latest
```

### 1. Resource group + free Linux plan + web app

```bash
az group create --name "$RG" --location "$LOCATION"

# Free F1, Linux. (F1 *does* support Linux custom containers — verified against the
# MS Learn custom-container quickstart, which selects F1 explicitly.)
az appservice plan create --name "$PLAN" --resource-group "$RG" --is-linux --sku F1

# Create the app pointing at the image. The package may not exist yet (see step 4);
# that's fine — the first Deploy run creates it and re-points the app at the :sha tag.
az webapp create --resource-group "$RG" --plan "$PLAN" --name "$APP" \
  --container-image-name "$IMAGE"
```

### 2. App settings (the load-bearing four)

```bash
az webapp config appsettings set -g "$RG" -n "$APP" --settings \
  WEBSITES_PORT=8080 \
  WEBSITES_ENABLE_APP_SERVICE_STORAGE=true \
  ConnectionStrings__Default="Data Source=/home/data/readlog.db" \
  WEBSITES_CONTAINER_START_TIME_LIMIT=600

az webapp update -g "$RG" -n "$APP" --https-only true
```

| Setting | Why |
| --- | --- |
| `WEBSITES_PORT=8080` | Custom containers need this so App Service forwards HTTP to the right port (the image `EXPOSE`s 8080). |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE=true` | **Required.** For custom containers, persistent `/home` is **off by default** — without it the SQLite file is wiped on every restart. This mounts the platform `/home` share. |
| `ConnectionStrings__Default` | Points EF Core at the SQLite file on the persistent share. Matches the Dockerfile default; set explicitly so it's visible/overridable. |
| `WEBSITES_CONTAINER_START_TIME_LIMIT=600` | Default container-start timeout is 230 s. On F1's shared CPU a cold start that also runs `Database.Migrate()` can exceed it and get killed mid-migration. 600 s gives it room. |
| HTTPS Only | App Service terminates TLS at the edge (free `*.azurewebsites.net` cert); the container stays HTTP on 8080. `UseForwardedHeaders` (first in the pipeline) makes the app see the original `https` scheme. |

Optional, only if you want Google features (otherwise Open Library only, no Google
login button):

```bash
az webapp config appsettings set -g "$RG" -n "$APP" --settings \
  GoogleBooks__ApiKey="<key>" \
  Authentication__Google__ClientId="<id>" \
  Authentication__Google__ClientSecret="<secret>"
# Then register https://<APP>.azurewebsites.net/signin-google as an authorized
# redirect URI in the Google Cloud console.
```

### 3. OIDC: let GitHub Actions deploy without a stored secret

Create an Entra app registration, federate it to **this repo's `production`
environment**, and grant it Contributor on the resource group:

```bash
APP_ID=$(az ad app create --display-name "readlog-github-deploy" --query appId -o tsv)
az ad sp create --id "$APP_ID"
SUB=$(az account show --query id -o tsv)
OBJ=$(az ad sp show --id "$APP_ID" --query id -o tsv)

az role assignment create --assignee-object-id "$OBJ" --assignee-principal-type ServicePrincipal \
  --role Contributor --scope "/subscriptions/$SUB/resourceGroups/$RG"

# Federated credential — subject MUST match the workflow's environment exactly.
# Written to a file and passed with @ so the JSON survives any shell intact.
cat > federated-cred.json <<'JSON'
{
  "name": "readlog-prod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:MikkoNumminen/Readlog-c-.net:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}
JSON
az ad app federated-credential create --id "$APP_ID" --parameters @federated-cred.json

echo "AZURE_CLIENT_ID       = $APP_ID"
echo "AZURE_TENANT_ID       = $(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID = $SUB"
```

Then in **GitHub → repo → Settings**:

- **Environments → New environment → `production`** → add a **required reviewer**
  (you). This is the deploy gate.
- **Secrets and variables → Actions:**
  - Secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
  - Variables: `AZURE_WEBAPP_NAME` = your `$APP` value

> **Simpler alternative to OIDC:** download the web app's *publish profile* from the
> portal, store it as a secret, and swap the `azure/login` step for
> `publish-profile:` on `azure/webapps-deploy`. Less setup, but it's a long-lived
> credential in the repo — OIDC is preferred here given the project's no-stored-secrets
> posture.

### 4. Let App Service pull from ghcr.io

The package `ghcr.io/mikkonumminen/readlog` doesn't exist until the first
**build-and-push** runs. Two options:

- **Make it public (simplest).** Run the `Deploy` workflow once; it creates the
  package (private by default). Then **GitHub → your profile → Packages →
  `readlog` → Package settings → Change visibility → Public**. App Service then
  pulls anonymously. Re-run `Deploy` so the `deploy` job succeeds.
- **Keep it private.** Add registry creds to the web app (a PAT with
  `read:packages`):
  ```bash
  az webapp config appsettings set -g "$RG" -n "$APP" --settings \
    DOCKER_REGISTRY_SERVER_URL=https://ghcr.io \
    DOCKER_REGISTRY_SERVER_USERNAME=MikkoNumminen \
    DOCKER_REGISTRY_SERVER_PASSWORD="<ghcr-PAT>"
  ```
  A package pushed by Actions also starts **unlinked** from the repo, so a pull can
  401/403 even with a valid PAT. Link it under **Package settings → Manage Actions
  access / repository access**, or ensure the PAT owner has read access to the package.

---

## Deploying

1. **GitHub → Actions → Deploy → Run workflow.**
2. Approve the `production` environment when it pauses (if you set a reviewer).
3. Watch it build → push → deploy. First request after deploy is slow (image pull +
   cold start + migrations).
4. Verify: browse `https://<APP>.azurewebsites.net`, and tail the container log to
   confirm migrations ran and the app bound to 8080:
   ```bash
   az webapp log config -g "$RG" -n "$APP" --docker-container-logging filesystem
   az webapp log tail   -g "$RG" -n "$APP"
   ```

---

## Operational caveats (free tier + SQLite)

These are accepted, deliberate limits for a **personal demo** — not production
posture. Don't present the deployed app as production-hardened.

- **SQLite on a network share is officially unsupported.** App Service Linux mounts
  `/home` as an SMB share where exclusive file locks can't be reliably acquired;
  Microsoft's guidance is to use a managed DB (Azure SQL / PostgreSQL / MySQL) for
  file-based providers. In practice a low-traffic, single-instance demo mostly works,
  but expect occasional `database is locked` errors under concurrency. The fix, when
  it matters, is an EF Core provider swap to Postgres/Azure SQL — out of scope for
  the demo. F1 can't scale out (one instance), which is actually what keeps SQLite's
  single-writer model viable.
- **No Always On.** The app sleeps after ~20 min idle; the next request triggers a
  cold start (pull + JIT + `Database.Migrate()`), so the first hit after idle is
  slow. Migrations re-check the history table on every start (cheap once applied, but
  on the critical path of that slow request).
- **~60 CPU-minutes/day quota, 1 GB storage, 1 GB RAM** on F1. Exceeding the CPU
  quota throttles the app until the daily reset. The `/home` share (app + DB + logs)
  counts against the 1 GB.
- **No custom domain / SSL binding on F1.** The default `*.azurewebsites.net` host
  and its managed cert work; a custom domain needs Shared (D1)+ and a TLS binding
  needs Basic (B1)+.
- **Mount-over-chown gotcha.** The Dockerfile `chown`s `/home/data` to the non-root
  `$APP_UID` at build time, but enabling persistent storage mounts the platform
  `/home` over that path at runtime. If EF Core can't open/create the DB on first
  boot, this mount-permission interaction is the likely cause — check the log stream.

## Upgrading off the demo tier

If this ever needs to be more than a demo, in rough order of payoff:

1. **Managed DB** (Azure SQL serverless or PostgreSQL Flexible) + EF provider swap —
   removes the SQLite-on-share fragility; the only genuinely production-appropriate
   change.
2. **B1 Basic** (~USD 13/mo) — Always On (no cold-start migration pain), no CPU
   quota, custom domain + free SNI TLS. Still single-instance.
3. **Azure Container Apps** (consumption, often free at low traffic) — same image,
   scale-to-zero; pin `maxReplicas=1` if still on SQLite.
