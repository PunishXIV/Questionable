using System.IO;

namespace Questionable.Model;

public static class AssemblyModelLoader
{
    public static Stream CommonAetheryte =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonAetheryte")!;
    public static Stream CommonAethernetShard =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonAethernetShard")!;
    public static Stream CommonClassJob =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonClassJob")!;
    public static Stream CommonCompletionFlags =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonCompletionFlags")!;
    public static Stream CommonVector3 =>
        typeof(AssemblyModelLoader).Assembly.GetManifestResourceStream("Questionable.Model.CommonVector3")!;
}
