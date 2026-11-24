# Sho8lana operations: logs and rollback

Live site: https://sho8lana.fady-n-gwargeous.workers.dev (Cloudflare worker proxy to http://sho8lana.runasp.net, MonsterASP free plan, IIS, net8, 64-bit app pool).
MonsterASP free is a demo/testing tier: no uptime guarantee, no backups. Fine for a portfolio project, not for real customers or payments.

## Reading the logs

- The app writes a rolling file log to `App_Data/logs/app-YYYYMMDD.log` on the server (UTC dates, 14 days kept). Default level is Warning, so you see warnings and errors. Unhandled errors include the full stack trace and a reference ID.
- Easiest way: log in as the admin and open **/Admin/Logs**. It shows today's warnings and errors, newest first.
- A visitor who sees the "Something went wrong" page gets a Reference code. Search the log for that code.
- Raw files: MonsterASP panel > File manager (or FTPS) > `App_Data/logs/`.
- For more detail set the server env var `Logging__File__MinLevel` to `Information` (then back to `Warning`).
- Alerts: the free plan has no alerting. Use a free external uptime monitor (for example UptimeRobot) on the site URL to get an email when it goes down, and check /Admin/Logs after any deploy.

## Rollback

Every deploy: first copy the current server files to a dated backup, then upload the new build.

### Before a deploy (make a restore point)
1. Over FTPS, download the files you are about to overwrite (at minimum `Marketplace.dll`, `Marketplace.pdb`, `Marketplace.deps.json`, plus any other changed file) into a dated local folder, e.g. `rollback/2026-11-24/`.
2. A known-good copy of the previous build also lives on the server in `rollback/` (outside the web root, not served) when one was made.

### Roll back with the saved files (fastest, about 2 minutes)
1. Upload `app_offline.htm` to the site root (the site shows a maintenance page and the app unloads, so files unlock).
2. Upload the saved `Marketplace.dll`, `Marketplace.pdb`, `Marketplace.deps.json` (and any other file you changed) over the current ones. Never touch `web.config` or env vars.
3. Delete `app_offline.htm`.
4. Open the site and /Admin/Logs to check. The app pool must stay 64-bit.

### Roll back from GitHub (if you have no saved files)
1. `git log` and pick the last good commit, or revert the bad one: `git revert <bad-commit>` and push to `main`.
2. `dotnet publish Marketplace.csproj -c Release -r win-x64 --self-contained false -o publish`
3. Upload the changed files with the same app_offline.htm steps above.

The database (Neon Postgres) is not changed by a code rollback.
