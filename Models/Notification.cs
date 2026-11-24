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

        // Null means this draft has never been published. Zero is the first published version.
        [ConcurrencyCheck]
        public int? Revision { get; set; }

        public bool ApplyEdit(string title, string message, string? targetRole, bool isPublished)
        {
            if (Title == title && Message == message && TargetRole == targetRole && IsPublished == isPublished)
                return false;

            var contentChanged = Title != title || Message != message || TargetRole != targetRole;
            if ((Revision.HasValue || IsPublished) && (contentChanged || (isPublished && !IsPublished)))
                Revision = (Revision ?? 0) + 1;
            else if (isPublished || IsPublished)
                Revision ??= 0;

            Title = title;
            Message = message;
            TargetRole = targetRole;
            IsPublished = isPublished;
            UpdatedAtUtc = DateTime.UtcNow;
            return true;
        }

        public void SetPublication(bool isPublished) => ApplyEdit(Title, Message, TargetRole, isPublished);

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