using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QMSApplication.Models
{
    public class PortalUser
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Role { get; set; } = "User";

        public bool IsApproved { get; set; } = true;
        public Guid SecurityStamp { get; set; } = Guid.NewGuid();
        public Guid Version { get; set; } = Guid.NewGuid();
        public long? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public bool ManagerDepartmentScope { get; set; }
        public bool IsActive { get; set; } = true;
        [MaxLength(255)]
        public string? PasswordHash { get; set; }
        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? MobileNumber { get; set; }
    }
}