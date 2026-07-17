using LoxoneSolarForecast.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProductionHistory> ProductionHistory => Set<ProductionHistory>();
    public DbSet<ConsumptionHistory> ConsumptionHistory => Set<ConsumptionHistory>();
    public DbSet<BatteryHistory> BatteryHistory => Set<BatteryHistory>();
    public DbSet<GridHistory> GridHistory => Set<GridHistory>();
    public DbSet<ForecastHistory> ForecastHistory => Set<ForecastHistory>();
    public DbSet<HourlyForecast> HourlyForecasts => Set<HourlyForecast>();
    public DbSet<LearningData> LearningData => Set<LearningData>();
    public DbSet<ShadingProfile> ShadingProfiles => Set<ShadingProfile>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();
    public DbSet<LoxonePushLog> LoxonePushLogs => Set<LoxonePushLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProductionHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.Timestamp, x.ArrayId });
        });

        modelBuilder.Entity<ConsumptionHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<BatteryHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<GridHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<ForecastHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ForecastedFor);
            e.HasIndex(x => x.GeneratedAt);
        });

        modelBuilder.Entity<HourlyForecast>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Hour);
            e.HasIndex(x => new { x.Hour, x.GeneratedAt });
        });

        modelBuilder.Entity<LearningData>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Date).IsUnique();
        });

        modelBuilder.Entity<ShadingProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.HourOfDay, x.MonthOfYear }).IsUnique();
        });

        modelBuilder.Entity<AppLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.Level);
            e.HasIndex(x => x.Component);
        });

        modelBuilder.Entity<LoxonePushLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
        });
    }
}
