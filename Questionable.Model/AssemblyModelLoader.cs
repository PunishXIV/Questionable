using System.IO;
namespace Questionable.Model;

public static class AssemblyModelLoader
{
    public static Stream CommonAetheryteStream =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonAetheryte")!;
    public static Stream CommonAethernetShardStream =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonAethernetShard")!;
    public static Stream CommonClassJobStream =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonClassJob")!;
    public static Stream CommonCompletionFlagsStream =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonCompletionFlags")!;
    public static Stream CommonVector3Stream =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonVector3")!;
}
