using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Oloraculo.Web.Models;
using System.Text.Json;

namespace Oloraculo.Web.DAL
{
    public class OloraculoDbContext : DbContext
    {
        public OloraculoDbContext(DbContextOptions<OloraculoDbContext> options) : base(options)
        {
        }

        public DbSet<Team> Teams => Set<Team>();
        public DbSet<Group> Groups => Set<Group>();
        public DbSet<Fixture> Fixtures => Set<Fixture>();
        public DbSet<MatchResult> Results => Set<MatchResult>();
        public DbSet<Rating> Ratings => Set<Rating>();
        public DbSet<FixtureContext> FixtureContexts => Set<FixtureContext>();
        public DbSet<ApiMapping> ApiMappings => Set<ApiMapping>();
        public DbSet<AvailabilitySource> AvailabilitySources => Set<AvailabilitySource>();
        public DbSet<AvailabilityClaim> AvailabilityClaims => Set<AvailabilityClaim>();
        public DbSet<PredictionSnapshot> Snapshots => Set<PredictionSnapshot>();
        public DbSet<PredictionEvaluation> Evaluations => Set<PredictionEvaluation>();
        public DbSet<KnockoutMatch> KnockoutMatches => Set<KnockoutMatch>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var teamIdsComparer = new ValueComparer<IReadOnlyList<string>>(
                (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>()),
                value => (value ?? Array.Empty<string>()).Aggregate(0, (hash, item) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(item))),
                value => (value ?? Array.Empty<string>()).ToList());

            modelBuilder.Entity<Team>().HasKey(t => t.Id);
            modelBuilder.Entity<Fixture>().HasKey(f => f.Id);
            modelBuilder.Entity<MatchResult>().HasKey(r => r.Id);
            modelBuilder.Entity<FixtureContext>().HasKey(c => c.FixtureId);
            modelBuilder.Entity<Rating>().HasIndex(r => new { r.TeamId, r.Type });
            modelBuilder.Entity<Group>().HasIndex(g => g.Name).IsUnique();
            modelBuilder.Entity<Group>()
                .Property(g => g.TeamIds)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    value => JsonSerializer.Deserialize<List<string>>(value, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(teamIdsComparer);
            modelBuilder.Entity<ApiMapping>().HasIndex(m => m.LocalFixtureId).IsUnique();
            modelBuilder.Entity<AvailabilitySource>().HasIndex(s => s.Url).IsUnique();
            modelBuilder.Entity<AvailabilityClaim>().HasIndex(c => new { c.TeamId, c.PlayerKey, c.Status, c.SourceUrl });
            modelBuilder.Entity<PredictionSnapshot>().HasIndex(s => new { s.Kind, s.FixtureId, s.CreatedAt });
            modelBuilder.Entity<PredictionSnapshot>().HasIndex(s => new { s.Kind, s.BatchId, s.CreatedAt });
            modelBuilder.Entity<KnockoutMatch>().HasKey(m => m.MatchNumber);
            modelBuilder.Entity<KnockoutMatch>().HasIndex(m => m.ExternalFixtureId).IsUnique();
        }
    }
}
