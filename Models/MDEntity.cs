using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QMSApplication.Models
{
    public class MDEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string RecordID { get; set; } = string.Empty;

        public string? SNo { get; set; }

        public string? MilestoneName { get; set; }

        public string? DeliverableName { get; set; }

        public string? DeliverableType { get; set; }

        public DateOnly? MilestoneTargetDate { get; set; }

        public string? MilestoneTargetDateSource { get; set; }

        public DateOnly? DeliverableTargetDate { get; set; }

        public string? DeliverableTargetDateSource { get; set; }


        public string? AssignedTo { get; set; }

        public string? ScheduleDeviation { get; set; }

        public string? DeliverableActivity { get; set; }

        public string? DeviationReason { get; set; }

        public DateOnly? DeliverableActualCompletedDate { get; set; }

        public string? DeliverableActualCompletedDateSource { get; set; }

        public DateOnly? MilestoneAchievedDate { get; set; }

        public string? MilestoneAchievedDateSource { get; set; }
        public string? Status { get; set; }

        public string? Remarks { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public string? UpdatedBy { get; set; }

        public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
        public string? UGs { get; set; }
    }
}