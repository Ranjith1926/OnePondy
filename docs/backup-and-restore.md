# Backup and restore

How to back up and restore the OnePondy PostgreSQL database. It covers a scheduled logical backup (a full copy of the database at one moment) and how to bring it back on a clean server. It does **not** give point-in-time recovery: that needs WAL archiving, which is not set up (see `docs/release-readiness.md`).

## 1. What a backup is

`scripts/backup-database.ps1` writes two files per run, with the same timestamp, into a folder you choose:

| File | Holds | Restored with |
|---|---|---|
| `<database>-<yyyyMMdd-HHmmss>.dump` | Every table, row, trigger, constraint, index, function and the grants on the tables | `pg_restore` |
| `<database>-<yyyyMMdd-HHmmss>.globals.sql` | The server's roles, including `onepondy_app` and their password hashes | `psql` |

Both are needed on a new server: the grants in the dump apply only if the roles already exist.

The script checks each new dump before it counts: the file must not be empty and `pg_restore --list` must be able to read it. Until then the file is a `.partial` one. Older backups are deleted only after the new one passes, and only files the script itself named (`<database>-<date>-<time>.dump` and `.globals.sql`), so a failed run never costs you what you already had. It exits with code 1 on any failure and writes every run to `backup.log` in the same folder.

## 2. Running it

```
$env:PGPASSWORD = '<the postgres password>'
.\scripts\backup-database.ps1 -Database onepondy_dev -BackupDir D:\Backups\OnePondy -Keep 14
```

| Parameter | Default | Meaning |
|---|---|---|
| `-Database` | `onepondy_dev` | The database to copy |
| `-HostName`, `-Port`, `-User` | `localhost`, `5432`, `postgres` | The server and login |
| `-BackupDir` | `C:\OnePondyBackups` | Where the files go. Keep it **off the database server's own disk** for a real deployment |
| `-Keep` | `7` | How many backups to keep |
| `-BinDir` | `C:\Program Files\PostgreSQL\18\bin` | Where `pg_dump`, `pg_dumpall` and `pg_restore` are |

The password is never a parameter. Set `PGPASSWORD` for the run, or, for a scheduled task, put the login in the task user's pgpass file, `%APPDATA%\postgresql\pgpass.conf`, one line `localhost:5432:*:postgres:<password>`, readable only by that user. The script never prompts.

The roles file holds password hashes. The script restricts its permissions to the current user, SYSTEM and Administrators; do not copy it to a shared folder.

## 3. Scheduling it (Windows Task Scheduler)

```
schtasks /Create /TN "OnePondy database backup" /SC DAILY /ST 02:00 /RL HIGHEST ^
  /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\OnePondy\scripts\backup-database.ps1 -Database onepondy_dev -BackupDir D:\Backups\OnePondy -Keep 14"
```

The task runs as the user who creates it, so that user needs the pgpass file above. After the first scheduled run, check `backup.log`: a line starting `FAILED` means nothing was written. Someone must look at that log, or the task should alert, since a silent failure is the main way backups are lost.

Copy the backup folder off the machine (another disk, another host). A backup on the same disk does not survive the disk.

## 4. Restoring on a clean server

The restore never touches the original database. Do it on a new server, or into a new, differently named database.

1. **Roles first.** On the new server, as a superuser:
   ```
   psql -h <host> -U postgres -d postgres -f <database>-<stamp>.globals.sql
   ```
   One error is expected and harmless when the `postgres` role already exists (`role "postgres" already exists`). Anything else is a real problem.
2. **An empty database.**
   ```
   psql -h <host> -U postgres -d postgres -c "create database onepondy_restored"
   ```
3. **The data**, keeping owners and grants (do **not** pass `--no-owner` or `--no-acl` here; they were only used in the first by-hand test, before the roles were included):
   ```
   pg_restore -h <host> -U postgres -d onepondy_restored --exit-on-error <database>-<stamp>.dump
   ```
4. **Check it** (section 5), then point the application at it, or rename it into place.

Use the same major PostgreSQL version as the backup came from (18), or a newer one. A dump cannot be restored into an older major version.

## 5. Checking a restore

Run the same query on both databases and compare. They must be identical:

```sql
select table_name,
       (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text as n
from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1;
```

Then check that the structure came back: the counts of triggers, check constraints, foreign keys, indexes, functions and applied migrations (`"__EFMigrationsHistory"`) must match. This database repeats its important rules as triggers and constraints, so a restore that has the rows but not the triggers is not a restore.

Finally check the application role: as `onepondy_app` it must be able to read and write data and must be **refused** `create table` (`permission denied for schema public`).

## 6. Drill log

A restore that has never been tried is not a backup. Run a drill before each release and record it here.

| Date | Source | Result |
|---|---|---|
| 2026-11-05 | `onepondy_dev` (development, by hand then with the script) | Dump restored into a new, empty PostgreSQL 18 server, roles first. 60 tables with identical row counts (243 rows); 27 triggers, 158 check constraints, 100 foreign keys, 201 indexes, 16 functions and 25 migrations identical. `onepondy_app` read the data and was refused `create table`. Script exit codes, pruning (`-Keep 2` over three runs) and the failure path (wrong password: exit 1, old backups kept, no partial files) all checked, under Windows PowerShell 5.1. |

## 7. What this does not cover

- **Point-in-time recovery.** A restore returns the database to the moment of the last backup. With a daily backup that is up to a day of data lost; the spec's recovery target is 15 minutes, which needs WAL archiving. Not set up.
- **Production size and speed.** The drill used a 250 KB development database. How long a production restore takes has not been measured.
- **Files outside the database.** Uploaded files, if any appear later, and the application's settings and secrets are not in these backups.
- **The `postgres` login.** The script defaults to `postgres` because reading the roles' password hashes needs a superuser. On a production server, decide who may hold that login for the backup task.
