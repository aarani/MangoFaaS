using System.ComponentModel.DataAnnotations;

namespace MangoFaaS.Secrets.Models;

public class Secret
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public required string Name { get; set; }

    public required string Value { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public required string OwnerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<FunctionSecret> FunctionSecrets { get; set; } = null!;
}
