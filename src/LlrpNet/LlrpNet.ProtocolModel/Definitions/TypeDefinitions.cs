using System.Collections.ObjectModel;

namespace LlrpNet.ProtocolModel.Definitions;

/// <summary>
/// Defines an LLRP message and its ordered payload members.
/// </summary>
public sealed class MessageDefinition
{
    /// <summary>
    /// Initializes a new message definition.
    /// </summary>
    public MessageDefinition(
        string name,
        ushort typeNumber,
        bool required,
        string? responseType,
        IEnumerable<ProtocolMemberDefinition> members)
    {
        Name = RequireName(name, nameof(name));
        TypeNumber = typeNumber;
        Required = required;
        ResponseType = string.IsNullOrWhiteSpace(responseType) ? null : responseType;
        Members = CopyMembers(members);
    }

    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the ten-bit wire message type.
    /// </summary>
    public ushort TypeNumber { get; }

    /// <summary>
    /// Gets a value indicating whether the standard requires support for this message.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Gets the declared response message name, when this is a request.
    /// </summary>
    public string? ResponseType { get; }

    /// <summary>
    /// Gets ordered payload members.
    /// </summary>
    public IReadOnlyList<ProtocolMemberDefinition> Members { get; }

    internal static string RequireName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    internal static ReadOnlyCollection<ProtocolMemberDefinition> CopyMembers(
        IEnumerable<ProtocolMemberDefinition> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        ProtocolMemberDefinition[] copy = members.ToArray();
        if (copy.Any(static member => member is null))
        {
            throw new ArgumentException("A member collection cannot contain null entries.", nameof(members));
        }

        return Array.AsReadOnly(copy);
    }
}

/// <summary>
/// Defines an LLRP TV or TLV parameter and its ordered value members.
/// </summary>
public sealed class ParameterDefinition
{
    /// <summary>
    /// Initializes a new parameter definition.
    /// </summary>
    public ParameterDefinition(
        string name,
        ushort typeNumber,
        bool required,
        IEnumerable<ProtocolMemberDefinition> members)
    {
        Name = MessageDefinition.RequireName(name, nameof(name));
        TypeNumber = typeNumber;
        Required = required;
        Members = MessageDefinition.CopyMembers(members);
    }

    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the wire type number.
    /// </summary>
    public ushort TypeNumber { get; }

    /// <summary>
    /// Gets a value indicating whether the standard requires support for this parameter.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Gets the encoding implied by the LLRP type-number ranges.
    /// </summary>
    public ParameterEncodingKind Encoding =>
        TypeNumber <= 127 ? ParameterEncodingKind.Tv : ParameterEncodingKind.Tlv;

    /// <summary>
    /// Gets ordered parameter-value members.
    /// </summary>
    public IReadOnlyList<ProtocolMemberDefinition> Members { get; }
}

/// <summary>
/// Identifies the two standard LLRP parameter encodings.
/// </summary>
public enum ParameterEncodingKind
{
    /// <summary>
    /// One-octet type-value header with a definition-derived fixed length.
    /// </summary>
    Tv,

    /// <summary>
    /// Four-octet type-length-value header.
    /// </summary>
    Tlv,
}

/// <summary>
/// Defines a named enumeration.
/// </summary>
public sealed class EnumerationDefinition
{
    /// <summary>
    /// Initializes a new enumeration definition.
    /// </summary>
    public EnumerationDefinition(string name, IEnumerable<EnumerationEntryDefinition> entries)
    {
        Name = MessageDefinition.RequireName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(entries);
        EnumerationEntryDefinition[] copy = entries.ToArray();
        if (copy.Any(static entry => entry is null))
        {
            throw new ArgumentException("An enumeration cannot contain null entries.", nameof(entries));
        }

        Entries = Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets entries in source order.
    /// </summary>
    public IReadOnlyList<EnumerationEntryDefinition> Entries { get; }
}

/// <summary>
/// Defines one named enumeration value.
/// </summary>
public sealed record EnumerationEntryDefinition(string Name, long Value);

/// <summary>
/// Defines a named choice between parameter types.
/// </summary>
public sealed class ChoiceDefinition
{
    /// <summary>
    /// Initializes a new choice definition.
    /// </summary>
    public ChoiceDefinition(string name, IEnumerable<string> parameterTypes)
    {
        Name = MessageDefinition.RequireName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(parameterTypes);
        string[] copy = parameterTypes
            .Select(static type => MessageDefinition.RequireName(type, nameof(parameterTypes)))
            .ToArray();
        ParameterTypes = Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets allowed parameter type names in source order.
    /// </summary>
    public IReadOnlyList<string> ParameterTypes { get; }
}

/// <summary>
/// Associates an LTK vendor name with its 32-bit wire identifier.
/// </summary>
public sealed record VendorDefinition(string Name, uint VendorId);

/// <summary>
/// Defines an LLRP custom message and its ordered payload members.
/// </summary>
public sealed class CustomMessageDefinition
{
    /// <summary>
    /// Initializes a custom message definition.
    /// </summary>
    public CustomMessageDefinition(
        string name,
        string vendor,
        byte subtype,
        string xmlNamespace,
        string? responseType,
        IEnumerable<ProtocolMemberDefinition> members)
    {
        Name = MessageDefinition.RequireName(name, nameof(name));
        Vendor = MessageDefinition.RequireName(vendor, nameof(vendor));
        Subtype = subtype;
        Namespace = MessageDefinition.RequireName(xmlNamespace, nameof(xmlNamespace));
        ResponseType = string.IsNullOrWhiteSpace(responseType) ? null : responseType;
        Members = MessageDefinition.CopyMembers(members);
    }

    /// <summary>Gets the generated CLR/schema name.</summary>
    public string Name { get; }

    /// <summary>Gets the referenced vendor name.</summary>
    public string Vendor { get; }

    /// <summary>Gets the vendor-scoped one-octet custom-message subtype.</summary>
    public byte Subtype { get; }

    /// <summary>Gets the referenced XML namespace prefix.</summary>
    public string Namespace { get; }

    /// <summary>Gets the declared response message name, when present.</summary>
    public string? ResponseType { get; }

    /// <summary>Gets ordered payload members after the custom-message discriminator.</summary>
    public IReadOnlyList<ProtocolMemberDefinition> Members { get; }
}

/// <summary>
/// Defines an LLRP custom TLV parameter and its allowed containers.
/// </summary>
public sealed class CustomParameterDefinition
{
    /// <summary>
    /// Initializes a custom parameter definition.
    /// </summary>
    public CustomParameterDefinition(
        string name,
        string vendor,
        uint subtype,
        string xmlNamespace,
        IEnumerable<ProtocolMemberDefinition> members,
        IEnumerable<AllowedInDefinition> allowedIn)
    {
        Name = MessageDefinition.RequireName(name, nameof(name));
        Vendor = MessageDefinition.RequireName(vendor, nameof(vendor));
        Subtype = subtype;
        Namespace = MessageDefinition.RequireName(xmlNamespace, nameof(xmlNamespace));
        Members = MessageDefinition.CopyMembers(members);
        ArgumentNullException.ThrowIfNull(allowedIn);
        AllowedInDefinition[] allowedInCopy = allowedIn.ToArray();
        if (allowedInCopy.Any(static item => item is null))
        {
            throw new ArgumentException("An allowed-in collection cannot contain null entries.", nameof(allowedIn));
        }

        AllowedIn = Array.AsReadOnly(allowedInCopy);
    }

    /// <summary>Gets the generated CLR/schema name.</summary>
    public string Name { get; }

    /// <summary>Gets the referenced vendor name.</summary>
    public string Vendor { get; }

    /// <summary>Gets the vendor-scoped 32-bit custom-parameter subtype.</summary>
    public uint Subtype { get; }

    /// <summary>Gets the referenced XML namespace prefix.</summary>
    public string Namespace { get; }

    /// <summary>Gets ordered payload members after the custom-parameter discriminator.</summary>
    public IReadOnlyList<ProtocolMemberDefinition> Members { get; }

    /// <summary>Gets the containers in which this parameter is allowed.</summary>
    public IReadOnlyList<AllowedInDefinition> AllowedIn { get; }
}

/// <summary>
/// Describes one container and cardinality accepted for a custom parameter.
/// </summary>
public sealed record AllowedInDefinition(string Type, Cardinality Cardinality);

/// <summary>
/// Defines a vendor extension enumeration in a referenced XML namespace.
/// </summary>
public sealed class CustomEnumerationDefinition
{
    /// <summary>
    /// Initializes a custom enumeration definition.
    /// </summary>
    public CustomEnumerationDefinition(
        string name,
        string xmlNamespace,
        IEnumerable<EnumerationEntryDefinition> entries)
    {
        Name = MessageDefinition.RequireName(name, nameof(name));
        Namespace = MessageDefinition.RequireName(xmlNamespace, nameof(xmlNamespace));
        ArgumentNullException.ThrowIfNull(entries);
        EnumerationEntryDefinition[] copy = entries.ToArray();
        if (copy.Any(static entry => entry is null))
        {
            throw new ArgumentException("An enumeration cannot contain null entries.", nameof(entries));
        }

        Entries = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the generated CLR/schema name.</summary>
    public string Name { get; }

    /// <summary>Gets the referenced XML namespace prefix.</summary>
    public string Namespace { get; }

    /// <summary>Gets entries in source order.</summary>
    public IReadOnlyList<EnumerationEntryDefinition> Entries { get; }
}
