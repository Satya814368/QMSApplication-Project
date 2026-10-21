using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class MDViewModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public MDViewModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public MDRecord? Record { get; set; }


        public void OnGet(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            var entity = _db.MDRecords
                .AsNoTracking()
                .FirstOrDefault(x =>
                    x.RecordID == id);

            if (entity == null)
            {
                return;
            }


            // Normalize imported status values
            string status = entity.Status ?? "";

            if (status.Equals(
                    "Deffered",
                    StringComparison.OrdinalIgnoreCase) ||
                status.Equals(
                    "Defferred",
                    StringComparison.OrdinalIgnoreCase))
            {
                status = "Deferred";
            }


            Record = new MDRecord
            {
                RecordID = entity.RecordID,
                DepartmentName = entity.DepartmentName,
                SNo = entity.SNo,

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
                    entity.ScheduleDeviation,

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
    }
}