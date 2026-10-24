using System.Collections;
using System.Resources;
using Mono.Cecil;

// The official launcher bundles its dependencies as byte arrays in a .resources stream.
// Resolve those original bytes in memory instead of loading or executing the launcher.
internal sealed class EmbeddedAssemblyResolver : DefaultAssemblyResolver {
    private readonly Dictionary<string, byte[]> assemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AssemblyDefinition> loaded = new(StringComparer.OrdinalIgnoreCase);

    internal EmbeddedAssemblyResolver(string executable) {
        AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(executable))!);
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
        if (Directory.Exists(framework)) AddSearchDirectory(framework);
        using var launcher = AssemblyDefinition.ReadAssembly(executable);
        foreach (var resource in launcher.MainModule.Resources.OfType<EmbeddedResource>().Where(r => r.Name.EndsWith(".resources", StringComparison.Ordinal))) {
            using var stream = resource.GetResourceStream();
            using var reader = new ResourceReader(stream);
            IDictionaryEnumerator entries = reader.GetEnumerator();
            while (entries.MoveNext()) {
                var key = (string)entries.Key;
                reader.GetResourceData(key, out var type, out var data);
                if (type != "ResourceTypeCode.ByteArray") continue;
                using var bytes = new BinaryReader(new MemoryStream(data));
                var length = bytes.ReadInt32();
                if (length < 2 || length > data.Length - 4) continue;
                var content = bytes.ReadBytes(length);
                if (content[0] == 'M' && content[1] == 'Z') assemblies[key] = content;
            }
        }
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
        if (loaded.TryGetValue(name.Name, out var result)) return result;
        if (assemblies.TryGetValue(name.Name, out var content)) {
            result = AssemblyDefinition.ReadAssembly(new MemoryStream(content), new ReaderParameters { AssemblyResolver = this });
            loaded[name.Name] = result;
            return result;
        }
        return base.Resolve(name, parameters);
    }

    protected override void Dispose(bool disposing) {
        foreach (var assembly in loaded.Values) assembly.Dispose();
        loaded.Clear();
        base.Dispose(disposing);
    }
}
