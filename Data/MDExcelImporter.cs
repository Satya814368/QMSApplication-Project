using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using System.Globalization;

namespace QMSApplication.Data
{
    public class MDExcelImporter
    {
        private readonly QMSPortalDbContext _db;


        public MDExcelImporter(QMSPortalDbContext db)
        {
            _db = db;
        }

        public async Task<int> ImportAsync(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                throw new InvalidOperationException(
                    "Please select a valid Excel file.");
            }

            

            using var stream = excelFile.OpenReadStream();
            using var workbook = new XLWorkbook(stream);

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
                    Path.GetFileName(excelFile.FileName),

                ImportedAtUtc =
                    DateTime.UtcNow,

                ImportedBy =
                    "System",

                Status =
                    "Started"
            };

            _db.ImportBatches.Add(importBatch);

            await _db.SaveChangesAsync();

            var existingRecordIds = await _db.MDRecords
                .Select(r => r.RecordID)
                .ToListAsync();

            int highestRecordNumber = 0;

            foreach (var existingId in existingRecordIds)
            {
                if (!string.IsNullOrWhiteSpace(existingId) &&
                    existingId.StartsWith(
                        "MD-",
                        StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(existingId.Substring(3), out int number))
                {
                    if (number > highestRecordNumber)
                    {
                        highestRecordNumber = number;
                    }
                }
            }

            int generatedRecordNumber = highestRecordNumber;
            int totalRecords = 0;
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
                totalRecords++;

                string recordId =
                    $"MD-{generatedRecordNumber:0000}";

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
                        NullIfPlaceholder(
                            row.Cell(1).GetString()),

                    MilestoneName =
                        NullIfPlaceholder(
                            row.Cell(2).GetString()),

                    DeliverableName =
                        NullIfPlaceholder(
                            row.Cell(3).GetString()),

                    DeliverableType =
                        NullIfPlaceholder(
                            row.Cell(4).GetString()),

                    MilestoneTargetDate =
                        milestoneTarget,

                    MilestoneTargetDateSource =
                        NullIfEmpty(
                            row.Cell(5).GetString()),

                    DeliverableTargetDate =
                        deliverableTarget,

                    DeliverableTargetDateSource =
                        NullIfEmpty(
                            row.Cell(6).GetString()),

                    AssignedTo =
                        NullIfPlaceholder(
                            row.Cell(7).GetString()),

                    ScheduleDeviation =
                        NullIfPlaceholder(
                            scheduleDeviation),

                    DeliverableActivity =
                        NullIfPlaceholder(
                            row.Cell(9).GetString()),

                    DeviationReason =
                        NullIfPlaceholder(
                            row.Cell(10).GetString()),

                    DeliverableActualCompletedDate =
                        actualCompleted,

                    DeliverableActualCompletedDateSource =
                        NullIfEmpty(
                            row.Cell(11).GetString()),

                    MilestoneAchievedDate =
                        milestoneAchieved,

                    MilestoneAchievedDateSource =
                        NullIfEmpty(
                            row.Cell(12).GetString()),

                    Status =
                        NullIfPlaceholder(
                            status),

                    Remarks =
                        NullIfPlaceholder(
                            row.Cell(14).GetString()),

                    UGs =
                        NullIfPlaceholder(
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
                totalRecords;

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

        private static string? NullIfPlaceholder(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed =
                value.Trim();

            if (trimmed.Equals(
                    "NA",
                    StringComparison.OrdinalIgnoreCase) ||
                trimmed == "-")
            {
                return null;
            }

            return trimmed;
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