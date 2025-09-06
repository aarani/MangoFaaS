using MangoFaaS.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MangoFaaS.Functions.Models;

public class MangoFunctionsDbContext: DbContext
{
    public MangoFunctionsDbContext(DbContextOptions<MangoFunctionsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Function> Functions { get; set; }
    public DbSet<FunctionVersion> FunctionVersions { get; set; }
    public DbSet<Runtime> Runtimes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FunctionVersion>()
            .Property(f => f.State)
            .HasConversion<string>();

        modelBuilder.Entity<FunctionVersion>()
            .Property(f => f.CompressionMethod)
            .HasConversion<string>()
            .HasDefaultValue(CompressionMethod.Deflate);

        modelBuilder.Entity<Runtime>()
            .Property(r => r.CompressionMethod)
            .HasConversion<string>();
    }
}