using Microsoft.EntityFrameworkCore;

namespace MangoFaaS.Gateway.Models;

public class MangoGatewayDbContext: DbContext
{
    public MangoGatewayDbContext(DbContextOptions<MangoGatewayDbContext> options)
        : base(options)
    {
    }

    public DbSet<Route> Routes { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder
            .Entity<Route>()
            .Property(route => route.Type)
            .HasConversion<string>();
    }
}