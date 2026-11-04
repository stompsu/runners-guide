using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain;

namespace RunnersDiary.Infrastructure;

public sealed class DiaryDbContext(DbContextOptions<DiaryDbContext> options) : DbContext(options)
{
    public DbSet<DiaryUser> Users => Set<DiaryUser>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunType> RunTypes => Set<RunType>();
    public DbSet<GpxTrack> GpxTracks => Set<GpxTrack>();
    public DbSet<RaceDetails> RaceDetails => Set<RaceDetails>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiaryUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasMaxLength(200);
            entity.Property(user => user.DisplayName).HasMaxLength(200);
            entity.HasIndex(user => user.ExpiresAtUtc);
        });

        modelBuilder.Entity<RunType>(entity =>
        {
            entity.HasKey(type => type.Code);
            entity.Property(type => type.Code).HasMaxLength(50);
            entity.Property(type => type.Name).HasMaxLength(100).IsRequired();
            entity.Property(type => type.Category).HasMaxLength(30).IsRequired();
            entity.HasData(RunTypeCatalog.All);
        });

        modelBuilder.Entity<Run>(entity =>
        {
            entity.HasKey(run => run.Id);
            entity.Property(run => run.UserId).HasMaxLength(200).IsRequired();
            entity.Property(run => run.RunTypeCode).HasMaxLength(50).IsRequired();
            entity.Property(run => run.Title).HasMaxLength(200).IsRequired();
            entity.Property(run => run.Notes).HasMaxLength(4000);
            entity.Property(run => run.ShoesOrGear).HasMaxLength(200);
            entity.Property(run => run.Weather).HasMaxLength(100);
            entity.Property(run => run.Surface).HasMaxLength(50);
            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(run => new { run.UserId, run.StartAtUtc });
            entity.HasOne<DiaryUser>()
                .WithMany()
                .HasForeignKey(run => run.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RunType>()
                .WithMany()
                .HasForeignKey(run => run.RunTypeCode)
                .OnDelete(DeleteBehavior.Restrict);

            entity.OwnsOne(run => run.Location, location =>
            {
                location.Property(value => value.DisplayName).HasMaxLength(250);
                location.Property(value => value.Coordinates).HasColumnType("geography");
            });

            entity.OwnsOne(run => run.Rating, rating =>
            {
                rating.Property(value => value.Comment).HasMaxLength(1000);
            });
        });

        modelBuilder.Entity<RaceDetails>(entity =>
        {
            entity.HasKey(details => details.RunId);
            entity.Property(details => details.RaceName).HasMaxLength(200);
            entity.Property(details => details.OfficialDistanceCategory).HasMaxLength(100);
            entity.Property(details => details.BibNumber).HasMaxLength(50);
            entity.Property(details => details.ResultsUrl).HasMaxLength(2048);
            entity.HasOne<Run>()
                .WithOne(run => run.RaceDetails)
                .HasForeignKey<RaceDetails>(details => details.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GpxTrack>(entity =>
        {
            entity.HasKey(track => track.RunId);
            entity.Property(track => track.BlobName).HasMaxLength(1024).IsRequired();
            entity.HasOne<Run>()
                .WithOne(run => run.GpxTrack)
                .HasForeignKey<GpxTrack>(track => track.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
