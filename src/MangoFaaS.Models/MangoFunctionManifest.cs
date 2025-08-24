namespace MangoFaaS.Models;

public class MangoFunctionManifest
{
    public Guid FunctionId { get; set; }
    public Guid VersionId { get; set; }
    public Guid RuntimeImage { get; set; }
}