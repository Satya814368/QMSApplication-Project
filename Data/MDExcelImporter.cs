using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using System.Globalization;

namespace QMSApplication.Data
{
    public class MDExcelImporter
    {
        private readonly QMSPortalDbContext _db;

        private readonly string _excelFilePath =
            @"C:\QMSData\MilestoneDeliverables.xlsx";

        public MDExcelImporter(QMSPortalDbContext db)
        {
            _db = db;
        }

        // Retain the existing fixed-file import entry point for compatibility.
        public async Task<int> ImportAsync()
        {
            if (!File.Exists(_excelFilePath))
            {
                throw new FileNotFoundException(
                    "MilestoneDeliverables.xlsx could not be found.", _excelFilePath);
            }

            await using var fileStream = File.OpenRead(_excelFilePath);
            return await ImportAsync(fileStream, Path.GetFileName(_excelFilePath));
        }

        // Imports a user-selected Excel file and appends to existing records.
        public async Task<int> ImportAsync(Stream excelStream, string sourceFileName)
        {
            if (excelStream == null || !excelStream.CanRead)
                throw new InvalidOperationException("The uploaded Excel file could not be read.");

            using var workbook = new XLWorkbook(excelStream);

            var worksheet = workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                throw new InvalidOperationException(
                    "M&D worksheet could not be found.");
            }

            int headerRow = 0;

            foreach (var row in worksheet.RowsUsed())
            {
                foreach (var cell in row.CellsUsed())
                {
                    if (cell.GetString()
                        .Trim()
                        .Equals(
                            "S.No",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        headerRow = row.RowNumber();
                        break;
                    }
                }

                if (headerRow > 0)
                {
                    break;
                }
            }

            if (headerRow == 0)
            {
                throw new InvalidOperationException(
                    "M&D header row could not be found.");
            }

            /*
             * Create one ImportBatch for this import.
             */
            var importBatch = new ImportBatch
            {
                SourceFileName =
                    Path.GetFileName(sourceFileName),

                ImportedAtUtc =
                    DateTime.UtcNow,

                ImportedBy =
                    "System",

                Status =
                    "Started"
            };

            _db.ImportBatches.Add(importBatch);

            await _db.SaveChangesAsync();

            // Continue after the highest existing MD-#### ID instead of restarting at MD-0001.
            var existingRecordIds = await _db.MDRecords
                .Select(x => x.RecordID)
                .ToListAsync();

            int generatedRecordNumber = existingRecordIds
                .Select(id =>
                {
                    if (!string.IsNullOrWhiteSpace(id) &&
                        id.StartsWith("MD-", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(id.Substring(3), out int number))
                        return number;
                    return 0;
                })
                .DefaultIfEmpty(0)
                .Max();
            int successfulRecords = 0;
            int exceptionCount = 0;

            foreach (var row in worksheet.RowsUsed()
                         .Where(r => r.RowNumber() > headerRow))
            {
                bool hasData =
                    !string.IsNullOrWhiteSpace(
                        row.Cell(1).GetString()) ||
                    !string.IsNullOrWhiteSpace(
                        row.Cell(2).GetString()) ||
                    !string.IsNullOrWhiteSpace(
                        row.Cell(3).GetString()) ||
                    !string.IsNullOrWhiteSpace(
                        row.Cell(7).GetString()) ||
                    !string.IsNullOrWhiteSpace(
                        row.Cell(13).GetString());

                if (!hasData)
                {
                    continue;
                }

                generatedRecordNumber++;

                string recordId = $"MD-{generatedRecordNumber:0000}";

                string sourceSNo =
                    row.Cell(1).GetString().Trim();

                string milestoneName =
                    row.Cell(2).GetString().Trim();

                string deliverableName =
                    row.Cell(3).GetString().Trim();

                string deliverableType =
                    row.Cell(4).GetString().Trim();

                string assignedTo =
                    row.Cell(7).GetString().Trim();

                string status =
                    row.Cell(13).GetString().Trim();

                /*
                 * Map source status spelling.
                 */
                if (status.Equals(
                        "Deffered",
                        StringComparison.OrdinalIgnoreCase) ||
                    status.Equals(
                        "Defferred",
                        StringComparison.OrdinalIgnoreCase))
                {
                    AddImportException(
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Status",
                        row.Cell(13).GetString(),
                        "Source status 'Deffered'/'Defferred' was mapped to 'Deferred'.",
                        ref exceptionCount);

                    status = "Deferred";
                }

                /*
                 * Schedule deviation.
                 */
                string scheduleDeviation =
                    row.Cell(8).GetString().Trim();

                if (scheduleDeviation.Equals(
                    "YES",
                    StringComparison.OrdinalIgnoreCase))
                {
                    scheduleDeviation = "Yes";
                }
                else if (scheduleDeviation.Equals(
                    "NO",
                    StringComparison.OrdinalIgnoreCase))
                {
                    scheduleDeviation = "No";
                }

                /*
                 * Parse dates and record exceptions when
                 * a non-empty source value cannot be parsed.
                 */
                var milestoneTarget =
                    ParseDateWithException(
                        row.Cell(5),
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Milestone Target Date",
                        ref exceptionCount);

                var deliverableTarget =
                    ParseDateWithException(
                        row.Cell(6),
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Deliverable Target Date",
                        ref exceptionCount);

                var actualCompleted =
                    ParseDateWithException(
                        row.Cell(11),
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Deliverable Actual Completed Date",
                        ref exceptionCount);

                var milestoneAchieved =
                    ParseDateWithException(
                        row.Cell(12),
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Milestone Achieved Date",
                        ref exceptionCount);

                /*
                 * Record blank required fields as data-quality
                 * exceptions. The record is still imported so
                 * source information is not silently lost.
                 */
                if (string.IsNullOrWhiteSpace(milestoneName))
                {
                    AddImportException(
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Milestone Name",
                        row.Cell(2).GetString(),
                        "Required field is blank.",
                        ref exceptionCount);
                }

                if (string.IsNullOrWhiteSpace(deliverableName))
                {
                    AddImportException(
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Deliverable Name",
                        row.Cell(3).GetString(),
                        "Required field is blank.",
                        ref exceptionCount);
                }

                if (string.IsNullOrWhiteSpace(deliverableType))
                {
                    AddImportException(
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Deliverable Type",
                        row.Cell(4).GetString(),
                        "Required field is blank.",
                        ref exceptionCount);
                }

                if (string.IsNullOrWhiteSpace(assignedTo) ||
                    IsPlaceholder(assignedTo))
                {
                    AddImportException(
                        importBatch.Id,
                        recordId,
                        sourceSNo,
                        "Assigned To",
                        row.Cell(7).GetString(),
                        "Assigned To is blank or contains a placeholder.",
                        ref exceptionCount);
                }

                /*
                 * Preserve source values in the Source fields,
                 * while storing blank/NA/- as NULL where possible.
                 */
                var record = new MDEntity
                {
                    RecordID =
                        recordId,

                    SNo =
                        TextOrDash(
                            row.Cell(1).GetString()),

                    MilestoneName =
                        TextOrDash(
                            row.Cell(2).GetString()),

                    DeliverableName =
                        TextOrDash(
                            row.Cell(3).GetString()),

                    DeliverableType =
                        TextOrDash(
                            row.Cell(4).GetString()),

                    MilestoneTargetDate =
                        milestoneTarget,

                    MilestoneTargetDateSource =
                        TextOrDash(
                            row.Cell(5).GetString()),

                    DeliverableTargetDate =
                        deliverableTarget,

                    DeliverableTargetDateSource =
                        TextOrDash(
                            row.Cell(6).GetString()),

                    AssignedTo =
                        TextOrDash(
                            row.Cell(7).GetString()),

                    ScheduleDeviation =
                        TextOrDash(
                            scheduleDeviation),

                    DeliverableActivity =
                        TextOrDash(
                            row.Cell(9).GetString()),

                    DeviationReason =
                        TextOrDash(
                            row.Cell(10).GetString()),

                    DeliverableActualCompletedDate =
                        actualCompleted,

                    DeliverableActualCompletedDateSource =
                        TextOrDash(
                            row.Cell(11).GetString()),

                    MilestoneAchievedDate =
                        milestoneAchieved,

                    MilestoneAchievedDateSource =
                        TextOrDash(
                            row.Cell(12).GetString()),

                    Status =
                        TextOrDash(
                            status),

                    Remarks =
                        TextOrDash(
                            row.Cell(14).GetString()),

                    UGs =
                        TextOrDash(
                            row.Cell(16).GetString())
                };

                _db.MDRecords.Add(record);

                successfulRecords++;
            }

            /*
             * Save imported M&D records.
             */
            await _db.SaveChangesAsync();

            /*
             * Complete the ImportBatch.
             */
            importBatch.TotalRecords =
                generatedRecordNumber;

            importBatch.SuccessfulRecords =
                successfulRecords;

            importBatch.ExceptionCount =
                exceptionCount;

            importBatch.Status =
                "Completed";

            await _db.SaveChangesAsync();

            return successfulRecords;
        }

        private void AddImportException(
            long importBatchId,
            string recordId,
            string sourceSNo,
            string fieldName,
            string sourceValue,
            string reason,
            ref int exceptionCount)
        {
            var exception = new ImportException
            {
                ImportBatchId =
                    importBatchId,

                RecordID =
                    recordId,

                SourceSNo =
                    NullIfEmpty(sourceSNo),

                FieldName =
                    fieldName,

                SourceValue =
                    NullIfEmpty(sourceValue),

                ExceptionReason =
                    reason,

                IsReviewed =
                    false,

                CreatedAtUtc =
                    DateTime.UtcNow
            };

            _db.ImportExceptions.Add(exception);

            exceptionCount++;
        }

        private DateOnly? ParseDateWithException(
            IXLCell cell,
            long importBatchId,
            string recordId,
            string sourceSNo,
            string fieldName,
            ref int exceptionCount)
        {
            string value =
                cell.GetString().Trim();

            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals(
                    "NA",
                    StringComparison.OrdinalIgnoreCase) ||
                value == "-")
            {
                /*
                 * Blank, NA and - become NULL.
                 */
                return null;
            }

            if (cell.DataType ==
                XLDataType.DateTime)
            {
                return DateOnly.FromDateTime(
                    cell.GetDateTime());
            }

            string[] formats =
            {
                "dd-MMM-yyyy",
                "d-MMM-yyyy",
                "dd/MM/yyyy",
                "d/MM/yyyy",
                "dd-MM-yyyy",
                "d-MM-yyyy",
                "MM/dd/yyyy",
                "M/d/yyyy",
                "dd-MMM-yy",
                "d-MMM-yy"
            };

            if (DateTime.TryParseExact(
                    value,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsed))
            {
                return DateOnly.FromDateTime(parsed);
            }

            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsed))
            {
                return DateOnly.FromDateTime(parsed);
            }

            AddImportException(
                importBatchId,
                recordId,
                sourceSNo,
                fieldName,
                value,
                "Source date could not be parsed. Stored as NULL.",
                ref exceptionCount);

            return null;
        }

        private static bool IsPlaceholder(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string trimmed =
                value.Trim();

            return trimmed.Equals(
                       "NA",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   trimmed == "-";
        }

        private static string TextOrDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static string? NullIfEmpty(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }
    }
}