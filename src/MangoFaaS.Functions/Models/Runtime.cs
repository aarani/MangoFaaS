using MangoFaaS.Models.Enums;

namespace MangoFaaS.Functions.Models;

public class Runtime
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public required string Description { get; set; }

    public string FileName { get; set; } = string.Empty;

    public CompressionMethod CompressionMethod { get; set; } = CompressionMethod.Deflate;

    public bool IsActive { get; set; } = false;
}