using System.ComponentModel.DataAnnotations;

namespace MangoFaaS.Functions.Models
{
    public class Function
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(500)]
        public required string Description { get; set; }

        [MaxLength(100)]
        public required string Runtime { get; set; }

        public required string OwnerId { get; set; }

        public virtual ICollection<FunctionVersion> FunctionVersions { get; set; } = null!;
    }
}