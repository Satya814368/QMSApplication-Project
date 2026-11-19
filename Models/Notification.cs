using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QMSApplication.Models
{
    public class Notification
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        public DateTime NotificationDateUtc { get; set; }

        public bool IsPublished { get; set; }

        [MaxLength(50)]
        public string? TargetRole { get; set; }

        public bool IsRead { get; set; }

        public bool IsAcknowledged { get; set; }

        public DateTime? ReadAtUtc { get; set; }

        public DateTime? AcknowledgedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}