using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Questionable.Tests.Validation;

public class NamespaceShadowing
{
    [Fact]
    public void MainAssembly_HasNoTypesInModelNamespace()
    {
        // Read the assembly's metadata directly from disk. Using reflection
        // (Assembly.GetTypes()) forces the CLR to resolve every type's base
        // classes and interfaces, which drags in Dalamud/Lumina at test time
        // and throws ReflectionTypeLoadException. MetadataReader inspects the
        // PE file without executing any type resolution.
        string path = Path.Combine(AppContext.BaseDirectory, "Questionable.dll");
        Assert.True(File.Exists(path), $"Questionable.dll not found at {path}");

        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new PEReader(stream);
        MetadataReader md = pe.GetMetadataReader();

        List<string> offenders = new();
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            TypeDefinition td = md.GetTypeDefinition(handle);

            // Skip nested types — their Namespace field is empty in metadata;
            // the enclosing type carries the namespace and is visited on its
            // own iteration.
            if (td.IsNested)
                continue;

            string ns = md.GetString(td.Namespace);
            if (ns == "Questionable.Model" ||
                ns.StartsWith("Questionable.Model.", StringComparison.Ordinal))
            {
                offenders.Add($"{ns}.{md.GetString(td.Name)}");
            }
        }

        Assert.Empty(offenders);
    }
}
