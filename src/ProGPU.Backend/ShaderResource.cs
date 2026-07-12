using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace ProGPU.Backend;

public static class ShaderResource
{
    private static readonly ConcurrentDictionary<ResourceKey, string> s_sources = new();

    public static string Load<TAnchor>(string fileName)
    {
        return Load(typeof(TAnchor), fileName);
    }

    public static string Load(Type anchorType, string fileName)
    {
        ArgumentNullException.ThrowIfNull(anchorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("Shader resource names must be flat file names.", nameof(fileName));
        }

        return s_sources.GetOrAdd(new ResourceKey(anchorType.Assembly, fileName), static key => LoadCore(key));
    }

    private static string LoadCore(ResourceKey key)
    {
        string assemblyName = key.Assembly.GetName().Name
            ?? throw new InvalidOperationException("Shader resource assembly has no name.");
        string resourceName = $"{assemblyName}.Shaders.{key.FileName}";

        using Stream stream = key.Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded shader resource '{resourceName}' was not found in '{assemblyName}'.");
        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        return reader.ReadToEnd();
    }

    private readonly record struct ResourceKey(Assembly Assembly, string FileName);
}
