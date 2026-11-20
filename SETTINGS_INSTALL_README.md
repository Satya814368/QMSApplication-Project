# PMT Settings module installation

Based on PMT_Timesheets_Nov20_Updated(3).zip. Preserves existing modules.

## Changed files
- Models/QMSPortalDbContext.cs (three DbSets and two model configurations)
- Pages/Shared/_Layout.cshtml (Settings menu route)

## Added files
- Models/PortalSettings.cs
- Pages/Settings.cshtml
- Pages/Settings.cshtml.cs
- INSTALL_SETTINGS.sql
- SETTINGS_INSTALL_README.md

## Install
1. Back up source repository and MySQL database. Use a feature branch.
2. Copy/merge changed files; copy added files. Do not overwrite local newer changes blindly.
3. Open MySQL Workbench against the database specified by your connection string. Execute INSTALL_SETTINGS.sql once. It does not contain `USE` to avoid changing the wrong database.
4. Run `dotnet restore QMSApplication.csproj` and `dotnet build QMSApplication.csproj`.
5. Run `dotnet run --project QMSApplication.csproj --launch-profile https`; sign in and open `/Settings`.
6. Test Admin vs Manager/Team Lead/User/Guest permissions and profile editing.
7. Commit all listed source files and SQL; do not commit passwords, user secrets, or unrelated SQL files.
8. On other computers, pull the branch and apply INSTALL_SETTINGS.sql to each separate MySQL database before running.

## Current behavior and limits
- Profile name/mobile are editable by the account owner; role, username, email, and department remain controlled elsewhere.
- Security links to the existing forgot-password email verification flow.
- Preferences (theme, language, date format, notification toggles) persist but are **not yet wired** to the global CSS, localization, or notification delivery engine.
- Admin can save office schedule, Timesheet policy, and workspace metadata. **Existing Timesheets timer calculations remain hard-coded at 09:00-18:00 with 13:00-14:00 lunch**; the new settings do not change those calculations until integration is completed.
- Admin-only audit displays settings changes; role management links to existing Manage Users.
- The Settings module requires the three SQL tables; database migration strategy should be unified before using EF migrations on production databases.
- Build/runtime should be verified in your own .NET 8/MySQL environment; this package is not production-certified.
