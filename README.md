# jobtracker

Personal job application tracker. Blazor Server (.NET 10), SQLite, GitHub OAuth.

## Local development

1. Create a GitHub OAuth app at https://github.com/settings/developers
   - Homepage URL: `http://localhost:5054`
   - Callback URL: `http://localhost:5054/signin-github`
2. Paste the Client ID and Client Secret into `appsettings.Development.json` under `Authentication.GitHub`.
3. `dotnet run`. App listens on http://localhost:5054.

## Deploy (Oracle ARM VM via Caddy)

### One-time setup

1. **Cloudflare DNS** — add A record `jobs` → `157.151.210.112` (orange cloud).
2. **GitHub OAuth app for prod** — create a second app at https://github.com/settings/developers
   - Homepage URL: `https://jobs.demetrioq.com`
   - Callback URL: `https://jobs.demetrioq.com/signin-github`
3. **VM directory** — SSH to VM, then:
   ```
   mkdir -p ~/jobtracker/data
   cd ~/jobtracker
   ```
   Copy `deploy/docker-compose.yml` to `~/jobtracker/docker-compose.yml`.
   Create `~/jobtracker/jobtracker.env` from `deploy/jobtracker.env.example` and fill in the prod ClientId/ClientSecret.
4. **Caddy site block** — append to `~/caddy/Caddyfile`:
   ```
   jobs.demetrioq.com {
       reverse_proxy jobtracker:8080
   }
   ```
   Then `cd ~/caddy && docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile`.
5. **GitHub repo secrets** (Settings → Secrets and variables → Actions):
   - `SSH_HOST` = `157.151.210.112`
   - `SSH_USER` = `ubuntu`
   - `SSH_PRIVATE_KEY` = contents of the deploy key (same key reused from other projects)

### Deploys

Push to `main` → GitHub Actions builds the image on `ubuntu-24.04-arm`, pushes to `ghcr.io/demetrioq/jobtracker:latest`, SSH-deploys to VM.
