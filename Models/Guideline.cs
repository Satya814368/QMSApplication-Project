using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QMSApplication.Models
{
    public class Guideline
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string GuidelineCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        // Bit flags: Admin=1, Manager=2, Team Lead=4, User=8, Guest=16. Existing guidance targets everyone.
        public int TargetRoles { get; set; } = 31;
        public Guid Version { get; set; } = Guid.NewGuid();
        public bool ContentIsPlainText { get; set; }
        [MaxLength(100)] public string UpdatedBy { get; set; } = "";

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}