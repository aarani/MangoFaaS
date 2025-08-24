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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FunctionVersion>()
            .Property(f => f.State)
            .HasConversion<string>();
    }
}