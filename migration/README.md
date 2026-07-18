# CP6 development-environment migration

Snapshot date: 2026-07-18

This directory contains verified SQL Server backups for the three CP6 development databases. The `.bak` files are stored with Git LFS. Local secrets are intentionally excluded and must be recreated from `.env.example` on the destination host.

## Clone and verify

```powershell
git lfs install
git clone https://github.com/GTX537/CP6.git C:\CP6
Set-Location C:\CP6
git lfs pull
Get-FileHash migration\database\*.bak -Algorithm SHA256
Get-Content migration\database\SHA256SUMS.txt
```

Compare every computed hash with `database/SHA256SUMS.txt` before restoring.

## Restore into the Docker SQL Server

Start the database container first, copy the backups into it, then restore them. The commands use the password already present in the container environment and do not print it.

```powershell
wsl.exe -e sh -lc 'docker exec cp6-db mkdir -p /var/opt/mssql/backup'
wsl.exe -e sh -lc 'docker cp /mnt/c/CP6/migration/database/CP6DB_20260718.bak cp6-db:/var/opt/mssql/backup/'
wsl.exe -e sh -lc 'docker cp /mnt/c/CP6/migration/database/CP6DB_OA_20260718.bak cp6-db:/var/opt/mssql/backup/'
wsl.exe -e sh -lc 'docker cp /mnt/c/CP6/migration/database/CP6DB_SpaceQA_20260718.bak cp6-db:/var/opt/mssql/backup/'
```

Before restoring over existing databases, stop `cp6-api` so it has no open database connections. Restore through SSMS, Azure Data Studio, or `sqlcmd`, selecting the matching backup for each database. Use `WITH REPLACE` only when intentionally replacing an existing destination database.

## Secrets and generated files

- Copy `.env.example` to `.env` and set new local values.
- Rotate the SQL Server password, Cloudflare Tunnel token, SSH credentials, JWT keys, and any deployment credentials that existed while this repository was public.
- Reinstall frontend packages instead of migrating `node_modules` or `.pnpm-store`.
- Rebuild `bin`, `obj`, `dist`, and Docker images on the new host.
- Logs, caches, temporary files, and editor state are not migration sources of truth.
