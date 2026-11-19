
using Microsoft.EntityFrameworkCore;

namespace QMSApplication.Models
{
    public class QMSPortalDbContext : DbContext
    {
        public QMSPortalDbContext(
            DbContextOptions<QMSPortalDbContext> options)
            : base(options)
        {
        }

        public DbSet<MDEntity> MDRecords { get; set; } = null!;
        public DbSet<MDAuditLog> MDAuditLogs { get; set; } = null!;
        public DbSet<GuidelineReadStatus> GuidelineReadStatuses { get; set; } = null!;
        public DbSet<Guideline> Guidelines { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<NotificationUserStatus> NotificationUserStatuses { get; set; } = null!;
        public DbSet<PortalUser> PortalUsers { get; set; } = null!;
        public DbSet<ImportBatch> ImportBatches { get; set; } = null!;
        public DbSet<ImportException> ImportExceptions { get; set; } = null!;
        public DbSet<DeliverableTask> DeliverableTasks { get; set; } = null!;
        public DbSet<DeliverableTarget> DeliverableTargets { get; set; } = null!;
        public DbSet<TargetProgressStep> TargetProgressSteps { get; set; } = null!;
        public DbSet<Team> Teams { get; set; } = null!;
        public DbSet<TeamManager> TeamManagers { get; set; } = null!;
        public DbSet<TeamMembership> TeamMemberships { get; set; } = null!;
        public DbSet<TeamAuditEvent> TeamAuditEvents { get; set; } = null!;
        public DbSet<Department> Departments { get; set; } = null!;

        // Password reset and email OTP
        public DbSet<PasswordResetRequest> PasswordResetRequests { get; set; } = null!;
        public DbSet<RegistrationChallenge> RegistrationChallenges { get; set; } = null!;
        public DbSet<PasswordRecoveryChallenge> PasswordRecoveryChallenges { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Forgot Password OTP
            modelBuilder.Entity<PasswordRecoveryChallenge>(b =>
            {
                b.Property(x => x.Id).ValueGeneratedNever();
                b.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
                b.HasIndex(x => x.LastSentAtUtc);

                b.HasOne<PortalUser>()
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Guidelines
            modelBuilder.Entity<Guideline>(b =>
            {
                b.Property(x => x.Version).IsConcurrencyToken();
                b.Property(x => x.TargetRoles).HasDefaultValue(31);
                b.Property(x => x.ContentIsPlainText).HasDefaultValue(false);
                b.Property(x => x.UpdatedBy).HasDefaultValue("");
            });

            // Guideline read status
            modelBuilder.Entity<GuidelineReadStatus>(b =>
            {
                b.HasKey(x => new { x.UserId, x.GuidelineId });

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Guideline)
                    .WithMany()
                    .HasForeignKey(x => x.GuidelineId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Registration OTP
            modelBuilder.Entity<RegistrationChallenge>(b =>
            {
                b.Property(x => x.Id).ValueGeneratedNever();
                b.Property(x => x.Version).IsConcurrencyToken();

                b.HasIndex(x => new { x.Email, x.CreatedAtUtc });
                b.HasIndex(x => new { x.OwnerHash, x.LastSentAtUtc });
                b.HasIndex(x => x.CreatedAtUtc);
            });

            // Departments
            modelBuilder.Entity<Department>()
                .HasIndex(x => x.NormalizedName)
                .IsUnique();

            // Portal users
            modelBuilder.Entity<PortalUser>(b =>
            {
                b.Property(x => x.Version).IsConcurrencyToken();

                b.HasOne(x => x.Department)
                    .WithMany()
                    .HasForeignKey(x => x.DepartmentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Team department relationship
            modelBuilder.Entity<Team>()
                .HasOne(x => x.Department)
                .WithMany()
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Password reset requests
            modelBuilder.Entity<PasswordResetRequest>(b =>
            {
                b.HasIndex(x => x.TokenHash).IsUnique();
                b.HasIndex(x => new { x.UserId, x.RequestedAtUtc });
                b.Property(x => x.Version).IsConcurrencyToken();

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Teams
            modelBuilder.Entity<Team>(b =>
            {
                b.HasIndex(x => x.NormalizedName).IsUnique();
                b.HasIndex(x => x.LeadUserId).IsUnique();
                b.Property(x => x.Version).IsConcurrencyToken();

                b.HasOne(x => x.LeadUser)
                    .WithMany()
                    .HasForeignKey(x => x.LeadUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Team managers
            modelBuilder.Entity<TeamManager>(b =>
            {
                b.HasKey(x => new { x.TeamId, x.UserId });

                b.HasOne(x => x.Team)
                    .WithMany()
                    .HasForeignKey(x => x.TeamId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Team membership
            modelBuilder.Entity<TeamMembership>(b =>
            {
                b.HasKey(x => x.UserId);
                b.Property(x => x.UserId).ValueGeneratedNever();
                b.Property(x => x.Version).IsConcurrencyToken();

                b.HasOne(x => x.Team)
                    .WithMany()
                    .HasForeignKey(x => x.TeamId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Deliverable owning team
            modelBuilder.Entity<MDEntity>()
                .HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);

            // Deliverable tasks
            modelBuilder.Entity<DeliverableTask>(b =>
            {
                b.HasIndex(x => new { x.DeliverableId, x.Language })
                    .IsUnique();

                b.HasOne(x => x.Deliverable)
                    .WithMany()
                    .HasForeignKey(x => x.DeliverableId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.AssignedUser)
                    .WithMany()
                    .HasForeignKey(x => x.AssignedUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Deliverable targets
            modelBuilder.Entity<DeliverableTarget>(b =>
            {
                b.HasIndex(x => new { x.TaskId, x.TargetNumber })
                    .IsUnique();

                b.HasOne(x => x.Task)
                    .WithMany(x => x.Targets)
                    .HasForeignKey(x => x.TaskId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.AssignedUser)
                    .WithMany()
                    .HasForeignKey(x => x.AssignedUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.Property(x => x.ConcurrencyToken)
                    .IsConcurrencyToken();
            });

            // Target workflow steps
            modelBuilder.Entity<TargetProgressStep>(b =>
            {
                b.HasIndex(x => new { x.TargetId, x.StepNumber })
                    .IsUnique();

                b.HasOne(x => x.Target)
                    .WithMany(x => x.Steps)
                    .HasForeignKey(x => x.TargetId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Deliverable concurrency control
            modelBuilder.Entity<MDEntity>()
                .Property(x => x.ConcurrencyToken)
                .IsConcurrencyToken();
        }
    }
}
