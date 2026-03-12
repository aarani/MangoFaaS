using Microsoft.EntityFrameworkCore;

namespace MangoFaaS.Secrets.Models;

public class MangoSecretsDbContext(DbContextOptions<MangoSecretsDbContext> options)
    : DbContext(options)
{
    public DbSet<Secret> Secrets { get; set; }
    public DbSet<FunctionSecret> FunctionSecrets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Secret>()
            .HasIndex(s => new { s.OwnerId, s.Name })
            .IsUnique();

        modelBuilder.Entity<FunctionSecret>()
            .HasIndex(fs => new { fs.FunctionId, fs.SecretId })
            .IsUnique();

        modelBuilder.Entity<FunctionSecret>()
            .HasOne(fs => fs.Secret)
            .WithMany(s => s.FunctionSecrets)
            .HasForeignKey(fs => fs.SecretId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
