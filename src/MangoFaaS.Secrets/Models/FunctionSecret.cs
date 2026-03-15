using System.ComponentModel.DataAnnotations;

namespace MangoFaaS.Secrets.Models;

public class FunctionSecret
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FunctionId { get; set; }

    public Guid SecretId { get; set; }

    public Secret Secret { get; set; } = null!;
}
