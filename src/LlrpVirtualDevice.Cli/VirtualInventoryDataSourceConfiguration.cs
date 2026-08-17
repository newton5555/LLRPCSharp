using System.Text.Json;
using System.Text.Json.Serialization;
using LlrpDevice.Virtual;

namespace LlrpVirtualDevice.Cli;

/// <summary>Standalone JSON document for a virtual inventory data source.</summary>
public sealed record VirtualInventoryDataSourceDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = VirtualInventoryDataSources.DefaultId;
    public IReadOnlyList<VirtualDeviceTagConfiguration> Tags { get; init; } = [];
}

/// <summary>Loads independent tag-population documents for the virtual-device CLI.</summary>
internal static class VirtualInventoryDataSourceConfiguration
{
    private const string DefaultResourceName =
        "LlrpVirtualDevice.Cli.Config.llrp.data-sources.default.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IVirtualInventoryDataSource Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            string.Equals(reference, VirtualInventoryDataSources.DefaultId, StringComparison.OrdinalIgnoreCase))
        {
            return LoadEmbeddedDefault();
        }

        if (!File.Exists(reference))
        {
            throw new InvalidDataException(
                $"Unknown inventory data source '{reference}'. Use '{VirtualInventoryDataSources.DefaultId}' " +
                "or provide a JSON data-source path.");
        }

        return Load(reference);
    }

    public static IVirtualInventoryDataSource Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An inventory data-source path is required.", nameof(path));
        }

        using FileStream stream = File.OpenRead(path);
        return Read(stream, path);
    }

    private static IVirtualInventoryDataSource LoadEmbeddedDefault()
    {
        using Stream stream = typeof(VirtualInventoryDataSourceConfiguration).Assembly
            .GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidDataException("The built-in default inventory data source is not available.");
        return Read(stream, VirtualInventoryDataSources.DefaultId);
    }

    private static IVirtualInventoryDataSource Read(Stream stream, string sourceName)
    {
        VirtualInventoryDataSourceDocument document = JsonSerializer.Deserialize<VirtualInventoryDataSourceDocument>(
                stream,
                SerializerOptions)
            ?? throw new InvalidDataException($"Inventory data source '{sourceName}' is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported inventory data-source schema {document.SchemaVersion}; expected 1.");
        }

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            throw new InvalidDataException("An inventory data source identifier is required.");
        }

        return new InMemoryVirtualInventoryDataSource(
            document.Id,
            VirtualDeviceConfiguration.BuildTags(document.Tags));
    }
}
