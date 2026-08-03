using System.Collections.ObjectModel;

namespace LlrpNet.ProtocolModel.Definitions;

/// <summary>
/// Represents one normalized LLRP protocol definition source.
/// </summary>
public sealed class ProtocolDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolDefinition"/> class.
    /// </summary>
    public ProtocolDefinition(
        string sourceName,
        ProtocolNamespaceDefinition? xmlNamespace,
        IEnumerable<MessageDefinition> messages,
        IEnumerable<ParameterDefinition> parameters,
        IEnumerable<EnumerationDefinition> enumerations,
        IEnumerable<ChoiceDefinition> choices,
        IEnumerable<VendorDefinition>? vendors = null,
        IEnumerable<CustomMessageDefinition>? customMessages = null,
        IEnumerable<CustomParameterDefinition>? customParameters = null,
        IEnumerable<CustomEnumerationDefinition>? customEnumerations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(enumerations);
        ArgumentNullException.ThrowIfNull(choices);

        SourceName = sourceName;
        XmlNamespace = xmlNamespace;
        Messages = Copy(messages, nameof(messages));
        Parameters = Copy(parameters, nameof(parameters));
        Enumerations = Copy(enumerations, nameof(enumerations));
        Choices = Copy(choices, nameof(choices));
        Vendors = Copy(vendors ?? [], nameof(vendors));
        CustomMessages = Copy(customMessages ?? [], nameof(customMessages));
        CustomParameters = Copy(customParameters ?? [], nameof(customParameters));
        CustomEnumerations = Copy(customEnumerations ?? [], nameof(customEnumerations));
    }

    /// <summary>
    /// Gets the logical source name used in diagnostics.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the XML namespace emitted for the protocol's XML representation, when declared.
    /// </summary>
    public ProtocolNamespaceDefinition? XmlNamespace { get; }

    /// <summary>
    /// Gets message definitions in source order.
    /// </summary>
    public IReadOnlyList<MessageDefinition> Messages { get; }

    /// <summary>
    /// Gets parameter definitions in source order.
    /// </summary>
    public IReadOnlyList<ParameterDefinition> Parameters { get; }

    /// <summary>
    /// Gets enumeration definitions in source order.
    /// </summary>
    public IReadOnlyList<EnumerationDefinition> Enumerations { get; }

    /// <summary>
    /// Gets choice definitions in source order.
    /// </summary>
    public IReadOnlyList<ChoiceDefinition> Choices { get; }

    /// <summary>
    /// Gets vendor declarations in source order.
    /// </summary>
    public IReadOnlyList<VendorDefinition> Vendors { get; }

    /// <summary>
    /// Gets vendor-defined messages in source order.
    /// </summary>
    public IReadOnlyList<CustomMessageDefinition> CustomMessages { get; }

    /// <summary>
    /// Gets vendor-defined parameters in source order.
    /// </summary>
    public IReadOnlyList<CustomParameterDefinition> CustomParameters { get; }

    /// <summary>
    /// Gets vendor-defined enumerations in source order.
    /// </summary>
    public IReadOnlyList<CustomEnumerationDefinition> CustomEnumerations { get; }

    private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
        where T : class
    {
        T[] copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("A definition collection cannot contain null entries.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}

/// <summary>
/// Describes the protocol XML namespace declared by an LTK definition.
/// </summary>
public sealed record ProtocolNamespaceDefinition(
    string Prefix,
    string Uri,
    string? SchemaLocation);
