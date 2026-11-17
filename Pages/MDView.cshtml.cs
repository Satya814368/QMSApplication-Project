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
        public long DeliverableId { get; set; }


        public IActionResult OnGet(string? id)
        {
            var access = new QMSApplication.Security.AccessService(_db);
            var actor = access.Current(HttpContext).GetAwaiter().GetResult();
            var username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrWhiteSpace(username)) return RedirectToPage("/Login");

            if (actor == null) return RedirectToPage("/AccessDenied");
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var role = QMSApplication.Data.TeamSetupService.Role(actor.Role);
            var entity = access.Records(actor).FirstOrDefault(x => x.RecordID == id);
            if (entity == null) return NotFound();

            DeliverableId = entity.Id;
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
            return Page();
        }
    }
}