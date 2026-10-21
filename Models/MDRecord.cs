namespace QMSApplication.Models
{
    public class MDRecord
    {
        public string? RecordID { get; set; }
        public string? SNo { get; set; }
        public string? MilestoneName { get; set; }
        public string? DeliverableName { get; set; }
        public string? DeliverableType { get; set; }
        public string? MilestoneTargetDate { get; set; }
        public string? DeliverableTargetDate { get; set; }
        public string? AssignedTo { get; set; }
        public string? ScheduleDeviation { get; set; }
        public string? DeliverableActivity { get; set; }
        public string? DeviationReason { get; set; }
        public string? DeliverableActualCompletedDate { get; set; }
        public string? MilestoneAchievedDate { get; set; }
        public string? Status { get; set; }
        public string? Remarks { get; set; }

        // UGs column from the source Excel
        public string? UGs { get; set; }
        public Guid ConcurrencyToken { get; set; }
        public bool IsOverdue { get; set; }
    }
}