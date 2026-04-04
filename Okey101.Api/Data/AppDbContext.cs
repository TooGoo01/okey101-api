using Microsoft.EntityFrameworkCore;
using Okey101.Api.Models.Entities;
using Okey101.Api.Services;

namespace Okey101.Api.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<GameCenter> GameCenters => Set<GameCenter>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameTeam> GameTeams => Set<GameTeam>();
    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();
    public DbSet<ScoreEntry> ScoreEntries => Set<ScoreEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Player configuration
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PhoneNumberHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Username).HasMaxLength(50);
            entity.Property(e => e.PasswordHash).HasMaxLength(256);
            entity.HasIndex(e => e.Username).IsUnique().HasFilter("[Username] IS NOT NULL").HasDatabaseName("IX_Players_Username");
            entity.HasIndex(e => e.PhoneNumberHash).HasDatabaseName("IX_Players_PhoneNumberHash");

            // Global tenant query filter — null TenantId on provider means superadmin (bypasses filter)
            entity.HasQueryFilter(e => _tenantProvider.TenantId == null || e.TenantId == _tenantProvider.TenantId);
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(256);
            entity.HasIndex(e => e.Token).HasDatabaseName("IX_RefreshTokens_Token");
            entity.HasIndex(e => e.PlayerId).HasDatabaseName("IX_RefreshTokens_PlayerId");
            entity.HasOne(e => e.Player)
                .WithMany(p => p.RefreshTokens)
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // OtpCode configuration
        modelBuilder.Entity<OtpCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PhoneNumberHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CodeHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.PhoneNumberHash).HasDatabaseName("IX_OtpCodes_PhoneNumberHash");
        });

        // Table configuration
        modelBuilder.Entity<Table>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.QrCodeIdentifier).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => e.QrCodeIdentifier).IsUnique().HasDatabaseName("IX_Tables_QrCodeIdentifier");
            entity.HasIndex(e => e.GameCenterId).HasDatabaseName("IX_Tables_GameCenterId");
            entity.HasOne(e => e.GameCenter)
                .WithMany()
                .HasForeignKey(e => e.GameCenterId)
                .OnDelete(DeleteBehavior.Cascade);

            // Global tenant query filter
            entity.HasQueryFilter(e => _tenantProvider.TenantId == null || e.TenantId == _tenantProvider.TenantId);
        });

        // GameSession configuration
        modelBuilder.Entity<GameSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GameName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => e.TableId).HasDatabaseName("IX_GameSessions_TableId");
            entity.HasIndex(e => e.CreatedByPlayerId).HasDatabaseName("IX_GameSessions_CreatedByPlayerId");
            entity.HasOne(e => e.Table)
                .WithMany()
                .HasForeignKey(e => e.TableId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByPlayer)
                .WithMany()
                .HasForeignKey(e => e.CreatedByPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(e => _tenantProvider.TenantId == null || e.TenantId == _tenantProvider.TenantId);
        });

        // GameTeam configuration
        modelBuilder.Entity<GameTeam>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TeamName).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.GameSessionId).HasDatabaseName("IX_GameTeams_GameSessionId");
            entity.HasOne(e => e.GameSession)
                .WithMany(gs => gs.Teams)
                .HasForeignKey(e => e.GameSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GamePlayer configuration
        modelBuilder.Entity<GamePlayer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GuestName).HasMaxLength(100);
            entity.HasIndex(e => e.GameTeamId).HasDatabaseName("IX_GamePlayers_GameTeamId");
            entity.HasIndex(e => e.PlayerId).HasDatabaseName("IX_GamePlayers_PlayerId");
            entity.HasOne(e => e.GameTeam)
                .WithMany(gt => gt.Players)
                .HasForeignKey(e => e.GameTeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Player)
                .WithMany()
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ScoreEntry configuration
        modelBuilder.Entity<ScoreEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScoreType).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => e.GameSessionId).HasDatabaseName("IX_ScoreEntries_GameSessionId");
            entity.HasIndex(e => e.CreatedByPlayerId).HasDatabaseName("IX_ScoreEntries_CreatedByPlayerId");
            entity.HasOne(e => e.GameSession)
                .WithMany()
                .HasForeignKey(e => e.GameSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CreatedByPlayer)
                .WithMany()
                .HasForeignKey(e => e.CreatedByPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(e => _tenantProvider.TenantId == null ||
                e.GameSession.TenantId == _tenantProvider.TenantId);
        });

        // GameCenter configuration
        modelBuilder.Entity<GameCenter>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Location).IsRequired().HasMaxLength(300);
            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("IX_GameCenters_Name");
        });
    }
}
