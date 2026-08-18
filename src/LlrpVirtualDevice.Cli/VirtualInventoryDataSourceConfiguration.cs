using System.Text.Json;
using System.Text.Json.Serialization;
using LlrpDevice.Virtual.Hosting;

namespace LlrpVirtualDevice.Cli;

public sealed record VirtualInventoryDataSourceDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = VirtualInventoryOptions.DefaultSourceId;
    public IReadOnlyList<VirtualDeviceTagConfiguration> Tags { get; init; } = [];
}

internal static class VirtualInventoryDataSourceConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static VirtualInventoryOptions Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            string.Equals(reference, VirtualInventoryOptions.DefaultSourceId, StringComparison.OrdinalIgnoreCase))
        {
            return new VirtualInventoryOptions { SourceId = VirtualInventoryOptions.DefaultSourceId };
        }

        if (!File.Exists(reference))
        {
            throw new InvalidDataException(
                $"Unknown inventory data source '{reference}'. Use '{VirtualInventoryOptions.DefaultSourceId}' " +
                "or provide a JSON data-source path.");
        }

        using FileStream stream = File.OpenRead(reference);
        VirtualInventoryDataSourceDocument document = JsonSerializer.Deserialize<VirtualInventoryDataSourceDocument>(
                stream, SerializerOptions)
            ?? throw new InvalidDataException($"Inventory data source '{reference}' is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported inventory data-source schema {document.SchemaVersion}; expected 1.");
        }

        return new VirtualInventoryOptions
        {
            SourceId = document.Id,
            Tags = VirtualDeviceConfiguration.BuildTags(document.Tags),
        };
    }
}
