using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MangoFaaS.Models.Enums;

namespace MangoFaaS.Functions.Models
{
    public class FunctionVersion
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(500)]
        public required string Description { get; set; }

        [MaxLength(500)]
        public required string FilePath { get; set; }

        public CompressionMethod CompressionMethod { get; set; } = CompressionMethod.Tar;

        [MaxLength(100)]
        public required string Entrypoint { get; set; }

        [DefaultValue(FunctionState.Created)]
        public FunctionState State { get; set; } = FunctionState.Created;

        public Guid FunctionId { get; set; }
        public virtual Function Function { get; set; } = null!;
    }


    public enum FunctionState
    {
        Created,
        Deployed,
        Failed
    }
}