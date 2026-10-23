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
        public DbSet<Guideline> Guidelines { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<NotificationUserStatus> NotificationUserStatuses { get; set; } = null!;
        public DbSet<PortalUser> PortalUsers { get; set; } = null!;
        public DbSet<ImportBatch> ImportBatches { get; set; } = null!;

        public DbSet<ImportException> ImportExceptions { get; set; } = null!;
        public DbSet<DeliverableTask> DeliverableTasks { get; set; } = null!;
        public DbSet<DeliverableTarget> DeliverableTargets { get; set; } = null!;
        public DbSet<TargetProgressStep> TargetProgressSteps { get; set; } = null!;
        protected override void OnModelCreating(
    ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DeliverableTask>(b =>
            {
                b.HasIndex(x => new { x.DeliverableId, x.Language }).IsUnique();
                b.HasOne(x => x.Deliverable).WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<DeliverableTarget>(b =>
            {
                b.HasIndex(x => new { x.TaskId, x.TargetNumber }).IsUnique();
                b.HasOne(x => x.Task).WithMany(x => x.Targets).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
                b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            });
            modelBuilder.Entity<TargetProgressStep>(b =>
            {
                b.HasIndex(x => new { x.TargetId, x.StepNumber }).IsUnique();
                b.HasOne(x => x.Target).WithMany(x => x.Steps).HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MDEntity>()
                .Property(x => x.ConcurrencyToken)
                .IsConcurrencyToken();
        }
    }
}