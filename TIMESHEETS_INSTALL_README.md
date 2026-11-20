# PMT Timesheets — installation and portability

This package is based on the uploaded `november20.zip`. Existing application modules are retained. The uploaded project already contained Timesheets models/pages and SQL; this update wires the models into EF Core and enables the sidebar link.

## Requirements
- .NET 8 SDK and MySQL compatible with the existing PMT project.
- A database backup before running SQL.
- Local database connection settings and user secrets on **each** computer. Do not commit passwords or Gmail app passwords.

## Installation on an existing Git checkout
1. Create a dedicated branch (`git switch -c timesheets_nov20`) and verify `git status --short`.
2. Merge **only** these files into your checkout: `Models/TimeEntry.cs`, `Models/QMSPortalDbContext.cs`, `Pages/Timesheets.cshtml`, `Pages/Timesheets.cshtml.cs`, `Pages/Shared/_Layout.cshtml`, `INSTALL_TIMESHEETS.sql`. Do not replace newer versions of shared files without reviewing the diff.
3. Back up the MySQL database. Execute `INSTALL_TIMESHEETS.sql` against the intended PMT database. The script uses `CREATE TABLE IF NOT EXISTS`, but it does **not** migrate incompatible existing tables. Verify the resulting schema.
4. Run `dotnet restore QMSApplication.csproj`, `dotnet build QMSApplication.csproj`, then `dotnet run --project QMSApplication.csproj --launch-profile https`.
5. Test user timer start/stop, 13:00–14:00 lunch exclusion, manager-only meetings, employee scope, submission, approval, and existing Notifications/Guidelines/Tasks.
6. Stage only intended changes; do not stage unrelated SQL or secrets. Commit/push the Timesheets branch.
7. On the second computer, check out that branch, configure its database and user secrets, apply SQL to its database, and run the same tests.

## Current behavior and limitations
- All work durations are calculated in India Standard Time (Asia/Kolkata), with work windows 09:00–13:00 and 14:00–18:00. A timer can be stopped after hours, but only office-window minutes count.
- Employees can time their own assigned targets. Admin/Manager can create Meeting/Training/Session entries for users in their permitted scope. Team Lead/Manager/Admin can review submitted entries in scope. Approved entries have no edit/delete handler.
- Meeting entry conflicts are checked against existing entries; timer stop also checks overlaps. Concurrent requests still need transaction-level enforcement before production use.
- Manager-entered meetings are created in Draft status; they are not automatically approved.
- The page shows up to 250 recent entries, not a complete historical report. More reporting and controlled reopen workflows are not yet implemented.
- **Build/runtime validation was not possible in this environment because the .NET SDK is unavailable.** Test on Windows before pushing or deploying.
- The original archive included Git metadata and build/cache folders; the deliverable excludes them for safety and portability.
