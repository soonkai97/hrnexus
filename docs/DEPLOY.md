# Production deployment runbook

Fresh, end-to-end setup of HRNexus on a DigitalOcean droplet with DuckDNS (free domain),
Let's Encrypt (free TLS), and Uptime Robot (free monitoring). You run the steps; the repo already
contains all the scripts (`deploy.sh`, `scripts/update-duckdns.sh`, `scripts/renew-cert.sh`,
`scripts/backup-db.sh`, `docker-compose.uat.yml`, `nginx/nginx.uat.conf`).

**Live URLs after setup** (one host, two ports):
- Admin portal → `https://<domain>` (443)
- Staff portal → `https://<domain>:8443`

> ⚠️ **Same-host cookie caveat:** admin (`:443`) and staff (`:8443`) share one hostname, so logging
> into **both in the same browser** collides the auth cookie (last login wins). In practice this is
> fine — the owner uses admin on desktop, staff use the staff app on phones (separate browsers).
> The clean fix is separate subdomains (`admin.<domain>` / `staff.<domain>`), each with its own
> `server_name` block + TLS cert. See "Subdomain option" at the end.

---

## Phase 1 — DigitalOcean droplet

1. **Create** at https://cloud.digitalocean.com → Create → Droplets:
   - Image: **Ubuntu 24.04 LTS x64**
   - Plan: Basic → Regular → **4 GB RAM / 2 vCPU** (the `--no-cache` Angular build needs ~1.5 GB)
   - Region: **Singapore (SGP1)**
   - Auth: SSH key (recommended)
   - Hostname: `hrnexus-prod` — note the **public IPv4**.
   - In the droplet's **Backups** tab, enable weekly snapshots (disaster recovery).
2. **SSH in:** `ssh root@YOUR_DROPLET_IP`
3. **Swap + update** (insurance for the build):
   ```bash
   fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
   echo '/swapfile none swap sw 0 0' >> /etc/fstab
   apt update && apt -y upgrade
   ```
4. **Docker + tools:**
   ```bash
   curl -fsSL https://get.docker.com | sh
   apt -y install docker-compose-plugin certbot git apache2-utils
   ```
5. **Firewall:**
   ```bash
   ufw allow 22 && ufw allow 80 && ufw allow 443 && ufw allow 8081 && ufw allow 8443
   ufw --force enable
   ```

---

## Phase 2 — DuckDNS (domain → droplet IP)

1. https://www.duckdns.org → sign in → add a subdomain (e.g. `hrnexus` → `hrnexus.duckdns.org`).
   - ⚠️ If you pick a name other than `hrnexus`, edit `DOMAIN="hrnexus"` in
     `scripts/update-duckdns.sh` to match.
2. Copy your **token** (top of the page).
3. Set the domain's **current ip** to your droplet IP (the cron job keeps it current afterward).
4. Verify from your PC: `nslookup <domain>` → returns the droplet IP.

---

## Phase 3 — Clone repo (read-only SSH deploy key) + secrets

The repo is private, so the server authenticates with a **read-only SSH deploy key** (no token
expiry; unattended `git pull` works).

```bash
ssh-keygen -t ed25519 -C "hrnexus-prod" -f ~/.ssh/id_ed25519 -N ""
cat ~/.ssh/id_ed25519.pub
```
On GitHub: repo → **Settings → Deploy keys → Add deploy key** → paste the public line, title
`hrnexus-prod`, leave **Allow write access UNCHECKED** → Add key. Then:

```bash
cd /root
git clone git@github.com:soonkai97/hrnexus.git   # type "yes" to accept the host key
cd hrnexus
cp .env.example .env
```

**Generate strong secrets** (one per line) and paste into `.env` (`nano .env`):
```bash
openssl rand -base64 48   # JWT_SECRET
openssl rand -base64 48   # PII_ENCRYPTION_KEY
openssl rand -base64 32   # FEATURE_TOKEN_SECRET
openssl rand -base64 24   # POSTGRES_PASSWORD
```
`.env` must contain:
```ini
DOMAIN=<domain>
UAT_HOST=<domain>
DUCKDNS_TOKEN=<duckdns-token>        # add this line — update-duckdns.sh reads it
POSTGRES_DB=hr_nexus
POSTGRES_USER=ncuser
POSTGRES_PASSWORD=<generated>
JWT_SECRET=<generated, 32+ chars>
PII_ENCRYPTION_KEY=<generated, 32+ chars>   # ⚠️ NEVER change after real data exists
FEATURE_TOKEN_SECRET=<generated>
VAPID_SUBJECT=mailto:admin@yourdomain.com   # leave VAPID keys blank to skip push,
                                            # or generate: npx web-push generate-vapid-keys
```
Save in nano: **Ctrl+O → Enter → Ctrl+X**. Never commit `.env` (it's git-ignored).

---

## Phase 4 — TLS certificate (Let's Encrypt)

Port 80 must be free (nothing is running yet):
```bash
certbot certonly --standalone -d <domain> --agree-tos -m your@email.com --no-eff-email
```
Certs land in `/etc/letsencrypt/live/<domain>/` — `docker-compose.uat.yml` mounts them into nginx.

**Renewal (important for Docker):** certbot's built-in auto-renew timer uses `--standalone` and
needs port 80 free, which the nginx container occupies — so it will fail. Disable it and let the
cron script (Phase 6) own renewal:
```bash
systemctl disable --now certbot.timer
```
`scripts/renew-cert.sh` stops web → `certbot renew` → starts web, freeing port 80 for the challenge.

---

## Phase 5 — First launch

```bash
cd /root/hrnexus
docker compose -f docker-compose.yml -f docker-compose.uat.yml up -d --build
docker compose -f docker-compose.yml -f docker-compose.uat.yml ps   # all healthy?
```
Open `https://<domain>` (admin) and `https://<domain>:8443` (staff). Log in as the seeded admin
**`HRADMIN` / `Admin@1234`** and **change the password immediately**.

---

## Phase 6 — Cron jobs

```bash
chmod +x /root/hrnexus/scripts/*.sh
crontab -e
```
Add (keep the temp filename when saving — that's how `crontab -e` installs it):
```cron
*/5 * * * *   /root/hrnexus/scripts/update-duckdns.sh >> /var/log/hrnexus-duckdns.log 2>&1
0 3 1 */2 *   /root/hrnexus/scripts/renew-cert.sh      >> /var/log/hrnexus-renew.log   2>&1
0 2 * * *     /root/hrnexus/scripts/backup-db.sh       >> /var/log/hrnexus-backup.log  2>&1
```
- DuckDNS IP refresh every 5 min · cert renewal every 2 months (no-op until <30 days to expiry) ·
  nightly DB backup (gzipped `pg_dump`, 7-day retention).
- Optional: `bash /root/hrnexus/scripts/setup-logrotate.sh` to rotate those logs.
- Verify: `crontab -l`, and `/root/hrnexus/scripts/update-duckdns.sh` should print `DuckDNS updated OK`.

---

## Phase 7 — Uptime Robot

1. https://uptimerobot.com → sign up.
2. **+ Add New Monitor** ×2 (type **HTTPS**, 5-min interval):
   - `https://<domain>` — "HRNexus Admin"
   - `https://<domain>:8443` — "HRNexus Staff"
3. **My Settings → Alert Contacts** → add your email (down/up alerts + ~30-day SSL-expiry warnings).

---

## Routine deploys (after the first launch)

On the droplet:
```bash
cd /root/hrnexus
./deploy.sh        # web only (most changes)
./deploy.sh all    # web + api (when the backend changed)
```
`deploy.sh` does: `git pull` → maintenance page ON → `--no-cache` rebuild → restart → wait for
healthy → maintenance OFF. Then hard-refresh the browser (**Ctrl+Shift+R**) — nginx serves
`index.html` with `no-cache`, but the SW/browser may still hold the old shell briefly.

---

## Backup & restore

- **Nightly DB dump:** `scripts/backup-db.sh` → gzipped files (7-day retention). Restore with
  `scripts/restore-db.sh <path-to-dump.sql.gz>` (prompts for confirmation; destructive).
- **Droplet snapshots:** weekly via the DO dashboard — full-server recovery.

---

## Subdomain option (eliminates the cookie caveat)

DuckDNS resolves any `*.<name>.duckdns.org` to the same IP, so you can serve each portal on its own
subdomain instead of two ports:
- `admin.<domain>` and `staff.<domain>`, each a separate `server_name` block in nginx with its own
  cert (`certbot ... -d admin.<domain> -d staff.<domain>`).
- Host-only cookies are per-exact-host, so the admin/staff cookies can never collide.
This is more setup (extra nginx config + cert) and unnecessary for a single shop where admin and
staff use different devices.

---

## Troubleshooting

- **`git clone` asks for a password / "Password authentication is not supported"** → the repo is
  private; use the SSH deploy key (Phase 3), not HTTPS+password.
- **Cert renew fails "port 80 in use"** → you didn't disable `certbot.timer`; the cron
  `renew-cert.sh` is the correct path (it stops nginx first).
- **Site shows the maintenance page and never recovers** → a failed `deploy.sh` left
  `./maintenance/on`; `rm -f /root/hrnexus/maintenance/on`.
- **Build OOM-killed** → confirm swap is on (`swapon --show`); 2 GB swap covers the Angular build.
- **Staff dropdown "no locations assigned" / wrong user** → admin + staff opened in the **same
  browser** (shared cookie). Use separate browsers/devices, or the subdomain option above.
