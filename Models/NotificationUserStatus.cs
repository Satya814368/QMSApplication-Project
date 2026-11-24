using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace QMSApplication.Models
{
    [Index(nameof(NotificationId), nameof(Username), nameof(Revision))]
    public class NotificationUserStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long NotificationId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        public int Revision { get; set; }

        public bool IsRead { get; set; }

        public bool IsAcknowledged { get; set; }

        public DateTime? ReadAtUtc { get; set; }

        public DateTime? AcknowledgedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}