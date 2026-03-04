using Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Api.Auth;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<RawDocument> RawDocuments => Set<RawDocument>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<BiomarkerReading> BiomarkerReadings => Set<BiomarkerReading>();
    public DbSet<ScoreSnapshot> ScoreSnapshots => Set<ScoreSnapshot>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();

    protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);

    builder.Entity<BiomarkerReading>()
        .HasIndex(x => new { x.UserId, x.BiomarkerCode, x.ObservedAtUtc, x.DocumentId })
        .HasDatabaseName("IX_biomarker_dedupe");

    builder.Entity<BiomarkerReading>()
        .HasIndex(x => new { x.UserId, x.DocumentId, x.BiomarkerCode })
        .HasDatabaseName("IX_biomarker_document_code");

    builder.Entity<RawDocument>()
        .HasIndex(x => new { x.UserId, x.Bucket, x.ObjectKey })
        .IsUnique();

    builder.Entity<ScoreSnapshot>()
        .HasIndex(x => new { x.UserId, x.DocumentId, x.CreatedAtUtc })
        .HasDatabaseName("IX_score_document_latest");

    builder.Entity<Recommendation>()
        .HasIndex(x => new { x.UserId, x.DocumentId, x.CreatedAtUtc })
        .HasDatabaseName("IX_recommendation_document_latest");

    builder.Entity<Recommendation>()
        .HasIndex(x => x.ScoreSnapshotId)
        .HasDatabaseName("IX_recommendation_snapshot");
}
}