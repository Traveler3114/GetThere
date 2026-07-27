# TransitInfoDB re-baseline repair

**Found and fixed:** 2026-07-27. **No data was lost.**

## What was wrong

TransitInfoAPI **could not start at all**. `Program.cs` calls `db.Database.MigrateAsync()` on
startup, and it failed with:

```
Microsoft.Data.SqlClient.SqlException: There is already an object named 'Countries' in the database.
Error Number:2714
```

The cause: `TransitInfoAPI/Migrations/` was squashed into a single baseline,
`20260722145915_InitialCreate`, but the database still carried the **pre-squash history** — 35 rows
in `__EFMigrationsHistory`, running `20260619113423_InitialCreate` through
`20260706113229_AddIsScheduleCapable`. EF saw the new baseline as unapplied and tried to create
tables that already existed.

This is the same class of problem as `docs/database-drift.md`, but the remedy had to be different:
this database holds real, expensive data.

| Table | Rows |
|---|---|
| StopTimes | 4,242,325 |
| Trips | 282,342 |
| CalendarDates | 169,175 |
| Calendars | 139,434 |
| MobilityStations | 104,603 |
| Shapes | 52,577 |
| CanonicalStations | 6,200 |
| FeedVersions | 17 |

## Why a plain stamp was not enough

Comparing the baseline's tables against the live schema showed the squash was not schema-neutral:

- **Missing from the database:** all nine Identity/auth tables — `AspNetUsers`, `AspNetRoles`,
  `AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens`,
  `AuditLogs`, `RefreshTokens`. Authentication was introduced in the squash and this database never
  received it.
- **Present but not in the baseline:** `CustomFeeds`, `CustomFeedRuns`, `CustomFeedTableConfigs`,
  `CustomFeedFieldMappings`, `CustomFeedTableFieldMappings` — a feature that existed in the old
  migration series and was dropped from the squashed baseline.

## What was done

1. Generated the baseline SQL with `dotnet ef migrations script`, and extracted **only** the 20
   statements belonging to the nine missing auth tables (creates plus their indexes), so the DDL is
   exactly what EF would have produced rather than hand-written.
2. Applied them inside `SET XACT_ABORT ON; BEGIN TRANSACTION;` — the first attempt failed on a
   filtered index because `QUOTED_IDENTIFIER` was off and rolled back cleanly with nothing created;
   re-run with `sqlcmd -I`.
3. Stamped the baseline as applied so `MigrateAsync` stops trying to run it:
   ```sql
   INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
   VALUES (N'20260722145915_InitialCreate', N'10.0.9');
   ```

Verified afterwards: 9 auth tables present, `StopTimes` still 4,242,325 rows, TransitInfoAPI starts
and answers `/health` with 200.

## Left behind

The five `CustomFeed*` tables are now orphans — the current model has no entities for them, so EF
ignores them entirely. They still hold data (597 `CustomFeedRuns`, 4 table configs, 27 field
mappings). Decide whether that feature is coming back; if not, drop them in a migration. They are
harmless where they are.

## If another environment was stamped the same way

Check before deploying:

```sql
SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;
```

If `20260722145915_InitialCreate` is absent but older migrations are present, that environment has
this same problem and TransitInfoAPI will not start there either. The repair script is reusable —
regenerate it with `dotnet ef migrations script` rather than copying SQL by hand.
