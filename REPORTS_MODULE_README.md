# Reports module update - 23 November 2026

Replace Pages/Reports.cshtml, Pages/Reports.cshtml.cs and merge Pages/Shared/_Layout.cshtml from this archive. No new database tables are required.

Included: scoped deliverables, 14-step target workflow, month-by-month planned/actual completion counts, workflow distribution, department-level completion comparison, overdue/deviation reporting, time activity breakdown, audit history, management summary, CSV export (deliverables, workflow, timesheets, audit), and browser print/save-as-PDF.

Limitations: Planned vs actual chart uses currently stored target and actual completion dates (not immutable historical baselines). Department-level charts do not represent individual productivity. Structured risk and issue reporting needs database integration. Native XLSX/PDF file generation and scheduled reports are not included. CSV can be opened in Excel. Guest exports are restricted. Test access scopes for all roles before production.

Build with dotnet build QMSApplication.csproj and open /Reports. Existing data is not modified.
