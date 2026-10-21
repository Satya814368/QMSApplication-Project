using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using System.Globalization;

namespace QMSApplication.Pages
{
    public class MDEditModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        private readonly string usersFilePath =
            @"C:\QMSData\Users.xlsx";

        public MDEditModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public MDRecord? Record { get; set; }

        public string Message { get; set; } = "";

        public List<string> AssignedToUsers { get; set; } = new();


        public void OnGet(string? id)
        {
            LoadAssignedToUsers();

            if (string.IsNullOrWhiteSpace(id))
            {
                Message = "Record ID is missing.";
                return;
            }

            Record = FindRecord(id);

            if (Record == null)
            {
                Message = "Record not found.";
            }
        }


        public IActionResult OnPost()
        {
            LoadAssignedToUsers();

            if (Record == null)
            {
                Message = "Record information is missing.";
                return Page();
            }


            // =========================================
            // REQUIRED FIELDS
            // =========================================

            if (string.IsNullOrWhiteSpace(Record.RecordID))
            {
                Message = "Record ID is missing.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Record.MilestoneName))
            {
                Message = "Milestone Name is required.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Record.DeliverableName))
            {
                Message = "Deliverable Name is required.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Record.DeliverableType))
            {
                Message = "Deliverable Type is required.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Record.MilestoneTargetDate))
            {
                Message = "Milestone Target Date is required.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Record.DeliverableTargetDate))
            {
                Message = "Deliverable Target Date is required.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Record.AssignedTo))
            {
                Message = "Assigned To is required.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Record.Status))
            {
                Message = "Status is required.";
                return Page();
            }


            // =========================================
            // DELIVERABLE TYPE
            // =========================================

            string deliverableType =
                Record.DeliverableType.Trim();

            if (deliverableType.Equals(
                "Other",
                StringComparison.OrdinalIgnoreCase))
            {
                string? customDeliverableType =
                    Request.Form["CustomDeliverableType"];

                if (string.IsNullOrWhiteSpace(
                    customDeliverableType))
                {
                    Message =
                        "Please enter the Custom Deliverable Type.";

                    return Page();
                }

                Record.DeliverableType =
                    customDeliverableType.Trim();
            }


            // =========================================
            // STATUS
            // =========================================

            string status =
                Record.Status.Trim();

            if (status.Equals(
                "Deffered",
                StringComparison.OrdinalIgnoreCase) ||
                status.Equals(
                "Defferred",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.Status = "Deferred";
            }
            else if (status.Equals(
                "Not Started",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.Status = "Not Started";
            }
            else if (status.Equals(
                "In Progress",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.Status = "In Progress";
            }
            else if (status.Equals(
                "Completed",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.Status = "Completed";
            }
            else if (status.Equals(
                "Cancelled",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.Status = "Cancelled";
            }
            else if (status.Equals(
                "Deferred",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.Status = "Deferred";
            }
            else
            {
                Message = "Invalid Status selected.";
                return Page();
            }


            // =========================================
            // SCHEDULE DEVIATION
            // =========================================

            if (string.IsNullOrWhiteSpace(
                Record.ScheduleDeviation))
            {
                Message =
                    "Please select Yes or No for Schedule Deviation.";

                return Page();
            }

            if (!Record.ScheduleDeviation.Equals(
                    "Yes",
                    StringComparison.OrdinalIgnoreCase) &&
                !Record.ScheduleDeviation.Equals(
                    "No",
                    StringComparison.OrdinalIgnoreCase))
            {
                Message =
                    "Schedule Deviation must be Yes or No.";

                return Page();
            }


            if (Record.ScheduleDeviation.Equals(
                "Yes",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.ScheduleDeviation = "Yes";

                if (string.IsNullOrWhiteSpace(
                    Record.DeliverableActivity))
                {
                    Message =
                        "Please select at least one activity.";

                    return Page();
                }


                string[] allowedActivities =
                {
                    "GN",
                    "AdHoc",
                    "Agile",
                    "Other"
                };


                string[] selectedActivities =
                    Record.DeliverableActivity
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToArray();


                foreach (string activity in selectedActivities)
                {
                    bool validActivity =
                        allowedActivities.Any(x =>
                            x.Equals(
                                activity,
                                StringComparison.OrdinalIgnoreCase));

                    if (!validActivity)
                    {
                        Message =
                            "Invalid activity selected.";

                        return Page();
                    }
                }


                Record.DeliverableActivity =
                    string.Join(
                        ", ",
                        selectedActivities.Select(activity =>
                            allowedActivities.First(x =>
                                x.Equals(
                                    activity,
                                    StringComparison.OrdinalIgnoreCase))));


                if (string.IsNullOrWhiteSpace(
                    Record.DeviationReason))
                {
                    Message =
                        "Please enter the reason for Schedule Deviation.";

                    return Page();
                }


                Record.DeviationReason =
                    Record.DeviationReason.Trim();
            }


            if (Record.ScheduleDeviation.Equals(
                "No",
                StringComparison.OrdinalIgnoreCase))
            {
                Record.ScheduleDeviation = "No";

                Record.DeliverableActivity = "N/A";

                Record.DeviationReason = "N/A";
            }


            // =========================================
            // FIND RECORD IN MYSQL
            // =========================================

            var entity = _db.MDRecords
                .FirstOrDefault(x =>
                    x.RecordID == Record.RecordID);

            if (entity == null)
            {
                Message =
                    "Record could not be found in MySQL.";

                return Page();
            }
            // =========================================
            // CONCURRENCY CHECK
            // =========================================

            if (Record.ConcurrencyToken == Guid.Empty)
            {
                Message =
                    "Record version information is missing. " +
                    "Please reload the record and try again.";

                return Page();
            }

            _db.Entry(entity)
                .Property(x => x.ConcurrencyToken)
                .OriginalValue = Record.ConcurrencyToken;

            entity.ConcurrencyToken = Guid.NewGuid();

            // =========================================
            // COMPLETION DATE
            // =========================================

            if (Record.Status.Equals(
                "Completed",
                StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(
                    Record.DeliverableActualCompletedDate))
                {
                    Record.DeliverableActualCompletedDate =
                        DateTime.Today.ToString("dd-MMM-yyyy");
                }
            }


            // =========================================
            // VALIDATE DATES
            // =========================================

            if (!TryParseBusinessDate(
                Record.MilestoneTargetDate,
                out DateOnly? milestoneTargetDate))
            {
                Message =
                    "Invalid Milestone Target Date.";

                return Page();
            }


            if (!TryParseBusinessDate(
                Record.DeliverableTargetDate,
                out DateOnly? deliverableTargetDate))
            {
                Message =
                    "Invalid Deliverable Target Date.";

                return Page();
            }


            if (!TryParseBusinessDate(
                Record.DeliverableActualCompletedDate,
                out DateOnly? actualCompletedDate))
            {
                Message =
                    "Invalid Deliverable Actual Completed Date.";

                return Page();
            }


            if (!TryParseBusinessDate(
                Record.MilestoneAchievedDate,
                out DateOnly? milestoneAchievedDate))
            {
                Message =
                    "Invalid Milestone Achieved Date.";

                return Page();
            }


            // =========================================
            // AUTOMATIC MILESTONE ACHIEVED DATE
            // =========================================

            if (Record.Status.Equals(
                "Completed",
                StringComparison.OrdinalIgnoreCase))
            {
                string currentMilestone =
                    Record.MilestoneName?.Trim() ?? "";

                bool allDeliverablesCompleted =
                    true;

                bool foundMilestoneDeliverable =
                    false;


                var milestoneRecords = _db.MDRecords
                    .Where(x =>
                        x.MilestoneName != null &&
                        x.MilestoneName.Trim()
                            .ToLower() ==
                        currentMilestone.ToLower())
                    .ToList();


                foreach (var milestoneRecord
                    in milestoneRecords)
                {
                    foundMilestoneDeliverable = true;

                    if (milestoneRecord.RecordID ==
                        Record.RecordID)
                    {
                        continue;
                    }


                    string rowStatus =
                        milestoneRecord.Status?.Trim() ?? "";


                    if (rowStatus.Equals(
                            "Deffered",
                            StringComparison.OrdinalIgnoreCase) ||
                        rowStatus.Equals(
                            "Defferred",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        rowStatus = "Deferred";
                    }


                    if (!rowStatus.Equals(
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        allDeliverablesCompleted =
                            false;

                        break;
                    }
                }


                if (foundMilestoneDeliverable &&
                    allDeliverablesCompleted &&
                    string.IsNullOrWhiteSpace(
                        Record.MilestoneAchievedDate))
                {
                    Record.MilestoneAchievedDate =
                        DateTime.Today.ToString("dd-MMM-yyyy");

                    milestoneAchievedDate =
                        DateOnly.FromDateTime(
                            DateTime.Today);
                }
            }


            // =========================================
            // UPDATE MYSQL ENTITY
            // =========================================
            entity.DepartmentName =
    Record.DepartmentName;

            entity.SNo =
                Record.SNo;

            entity.MilestoneName =
                Record.MilestoneName;

            entity.DeliverableName =
                Record.DeliverableName;

            entity.DeliverableType =
                Record.DeliverableType;

            entity.MilestoneTargetDate =
                milestoneTargetDate;

            entity.MilestoneTargetDateSource =
                Record.MilestoneTargetDate;

            entity.DeliverableTargetDate =
                deliverableTargetDate;

            entity.DeliverableTargetDateSource =
                Record.DeliverableTargetDate;

            entity.AssignedTo =
                Record.AssignedTo;

            entity.ScheduleDeviation =
                Record.ScheduleDeviation;

            entity.DeliverableActivity =
                Record.DeliverableActivity;

            entity.DeviationReason =
                Record.DeviationReason;

            entity.DeliverableActualCompletedDate =
                actualCompletedDate;

            entity.DeliverableActualCompletedDateSource =
                Record.DeliverableActualCompletedDate;

            entity.MilestoneAchievedDate =
                milestoneAchievedDate;

            entity.MilestoneAchievedDateSource =
                Record.MilestoneAchievedDate;

            entity.Status =
                Record.Status;

            entity.Remarks =
                Record.Remarks;

            entity.UGs =
                Record.UGs;
            // =========================================
            // UPDATE AUDIT INFORMATION
            // =========================================

            entity.UpdatedAtUtc = DateTime.UtcNow;

            entity.UpdatedBy =
                HttpContext.Session.GetString("Username")
                ?? HttpContext.Session.GetString("LoggedInUser")
                ?? "System";
            // =========================================
            // CREATE M&D AUDIT LOGS FOR CHANGED FIELDS
            // =========================================

            string auditUsername =
    HttpContext.Session.GetString("Username")
    ?? HttpContext.Session.GetString("LoggedInUser")
    ?? "System";

            var entry = _db.Entry(entity);

            string FormatAuditValue(object? value)
            {
                if (value == null)
                    return string.Empty;

                if (value is DateOnly dateOnly)
                    return dateOnly.ToString("yyyy-MM-dd");

                return value.ToString() ?? string.Empty;
            }

            void AddAuditLog(string propertyName, string displayName)
            {
                string oldValue = FormatAuditValue(
                    entry.OriginalValues[propertyName]);

                string newValue = FormatAuditValue(
                    entry.CurrentValues[propertyName]);

                if (oldValue == newValue)
                    return;

                _db.MDAuditLogs.Add(new MDAuditLog
                {
                    TimestampUtc = DateTime.UtcNow,
                    Username = auditUsername,
                    RecordCode = entity.RecordID,
                    Action = "Update",
                    ChangedField = displayName,
                    OldValue = oldValue,
                    NewValue = newValue
                });
            }
            AddAuditLog(nameof(MDEntity.DepartmentName), "Department Name");
            AddAuditLog(nameof(MDEntity.MilestoneName), "MilestoneName");
            AddAuditLog(nameof(MDEntity.DeliverableName), "DeliverableName");
            AddAuditLog(nameof(MDEntity.DeliverableType), "DeliverableType");
            AddAuditLog(nameof(MDEntity.MilestoneTargetDate), "MilestoneTargetDate");
            AddAuditLog(nameof(MDEntity.DeliverableTargetDate), "DeliverableTargetDate");
            AddAuditLog(nameof(MDEntity.AssignedTo), "AssignedTo");
            AddAuditLog(nameof(MDEntity.ScheduleDeviation), "ScheduleDeviation");
            AddAuditLog(nameof(MDEntity.DeliverableActivity), "DeliverableActivity");
            AddAuditLog(nameof(MDEntity.DeviationReason), "DeviationReason");
            AddAuditLog(
                nameof(MDEntity.DeliverableActualCompletedDate),
                "DeliverableActualCompletedDate");
            AddAuditLog(
                nameof(MDEntity.MilestoneAchievedDate),
                "MilestoneAchievedDate");
            AddAuditLog(nameof(MDEntity.Status), "Status");
            AddAuditLog(nameof(MDEntity.Remarks), "Remarks");
            AddAuditLog(nameof(MDEntity.UGs), "UGs");

            // =========================================
            // SAVE TO MYSQL
            // =========================================



            try
            {
                _db.SaveChanges();

                Message = "Record updated successfully in MySQL.";
            }
            catch (DbUpdateConcurrencyException)
            {
                Message = "This record was changed by another user. Please reload the record and try again.";
            }
            catch (Exception ex)
            {
                Message = "Error while saving record: " + ex.Message;
            }

            return Page();

        }
        // =========================================
        // FIND RECORD FROM MYSQL
        // =========================================

        private MDRecord? FindRecord(
            string recordID)
        {
            var entities = _db.MDRecords
     .AsNoTracking()
     .ToList();

            var entity = entities
                .FirstOrDefault(x =>
                    x != null &&
                    string.Equals(
                        x.RecordID,
                        recordID,
                        StringComparison.OrdinalIgnoreCase));

            if (entity == null)
            {
                return null;
            }
           

            string status =
                entity.Status?.Trim() ?? "";


            if (status.Equals(
                    "Deffered",
                    StringComparison.OrdinalIgnoreCase) ||
                status.Equals(
                    "Defferred",
                    StringComparison.OrdinalIgnoreCase))
            {
                status = "Deferred";
            }


            string scheduleDeviation =
                entity.ScheduleDeviation?.Trim() ?? "";


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


            return new MDRecord
            {
                RecordID =
                    entity.RecordID,

                DepartmentName =
        entity.DepartmentName,
                ConcurrencyToken =
                    entity.ConcurrencyToken,
                SNo =
                    entity.SNo,

                MilestoneName =
                    entity.MilestoneName,

                DeliverableName =
                    entity.DeliverableName,

                DeliverableType =
                    entity.DeliverableType,

                MilestoneTargetDate =
                    entity.MilestoneTargetDateSource,

                DeliverableTargetDate =
                    entity.DeliverableTargetDateSource,

                AssignedTo =
                    entity.AssignedTo,

                ScheduleDeviation =
                    scheduleDeviation,

                DeliverableActivity =
                    entity.DeliverableActivity,

                DeviationReason =
                    entity.DeviationReason,

                DeliverableActualCompletedDate =
                    entity.DeliverableActualCompletedDateSource,

                MilestoneAchievedDate =
                    entity.MilestoneAchievedDateSource,

                Status =
                    status,

                Remarks =
                    entity.Remarks,

                UGs =
                    entity.UGs
            };
        }


        // =========================================
        // LOAD REGISTERED USERS
        // =========================================

        private void LoadAssignedToUsers()
        {
            AssignedToUsers =
                new List<string>();


            if (!System.IO.File.Exists(
                usersFilePath))
            {
                return;
            }


            using var workbook =
                new ClosedXML.Excel.XLWorkbook(
                    usersFilePath);


            var worksheet =
                workbook.Worksheets
                    .FirstOrDefault();


            if (worksheet == null)
            {
                return;
            }


            int headerRow = 0;

            int usernameColumn = 0;


            foreach (var row in worksheet.RowsUsed())
            {
                foreach (var cell in row.CellsUsed())
                {
                    if (cell.GetString()
                        .Trim()
                        .Equals(
                            "Username",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        headerRow =
                            row.RowNumber();

                        usernameColumn =
                            cell.Address.ColumnNumber;

                        break;
                    }
                }


                if (usernameColumn > 0)
                {
                    break;
                }
            }


            if (headerRow == 0 ||
                usernameColumn == 0)
            {
                return;
            }


            foreach (var row in worksheet.RowsUsed()
                .Where(r =>
                    r.RowNumber() > headerRow))
            {
                string username =
                    row.Cell(usernameColumn)
                        .GetString()
                        .Trim();


                if (!string.IsNullOrWhiteSpace(username) &&
                    !AssignedToUsers.Contains(
                        username,
                        StringComparer.OrdinalIgnoreCase))
                {
                    AssignedToUsers.Add(username);
                }
            }


            AssignedToUsers =
                AssignedToUsers
                    .OrderBy(x => x)
                    .ToList();
        }


        // =========================================
        // DATE PARSER
        // =========================================

        private static bool TryParseBusinessDate(
            string? value,
            out DateOnly? result)
        {
            result = null;


            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }


            string text =
                value.Trim();


            if (text.Equals(
                    "NA",
                    StringComparison.OrdinalIgnoreCase) ||
                text == "-")
            {
                return true;
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
                "yyyy-MM-dd"
            };
            

            if (DateTime.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
            {
                result =
                    DateOnly.FromDateTime(parsed);

                return true;
            }


            if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
            {
                result =
                    DateOnly.FromDateTime(parsed);

                return true;
            }


            return false;
        }
    }
}