using MangoFaaS.Models.Enums;

namespace MangoFaaS.Models;

public class MangoFunctionManifest
{
    public Guid FunctionId { get; set; }
    public Guid VersionId { get; set; }
    public CompressionMethod OverlayCompression { get; set; }
    public required string RuntimeImage { get; set; }
    public CompressionMethod RuntimeCompression { get; set; }
}