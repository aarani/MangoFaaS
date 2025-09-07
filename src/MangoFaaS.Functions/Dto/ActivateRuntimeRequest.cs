using MangoFaaS.Models.Enums;

namespace MangoFaaS.Functions.Dto;

public class ActivateRuntimeRequest
{
    public CompressionMethod CompressionMethod { get; set; } = CompressionMethod.Deflate;
}