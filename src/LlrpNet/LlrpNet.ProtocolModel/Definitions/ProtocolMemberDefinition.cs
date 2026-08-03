namespace LlrpNet.ProtocolModel.Definitions;

/// <summary>
/// Represents one ordered member of a message or parameter value.
/// </summary>
public abstract record ProtocolMemberDefinition;

/// <summary>
/// Defines a scalar, bit field, or length-prefixed field.
/// </summary>
public sealed record FieldDefinition(
    string Name,
    ProtocolFieldType FieldType,
    string? Enumeration,
    string? Format) : ProtocolMemberDefinition;

/// <summary>
/// Identifies the scalar and length-delimited field forms supported by LTK binary definitions.
/// </summary>
public enum ProtocolFieldType
{
    /// <summary>One unsigned bit.</summary>
    U1 = 0,

    /// <summary>Two unsigned bits.</summary>
    U2 = 1,

    /// <summary>One signed octet.</summary>
    S8 = 2,

    /// <summary>One unsigned octet.</summary>
    U8 = 3,

    /// <summary>A signed 16-bit integer.</summary>
    S16 = 4,

    /// <summary>An unsigned 16-bit integer.</summary>
    U16 = 5,

    /// <summary>An unsigned 32-bit integer.</summary>
    U32 = 6,

    /// <summary>An unsigned 64-bit integer.</summary>
    U64 = 7,

    /// <summary>A fixed 96-bit unsigned value.</summary>
    U96 = 8,

    /// <summary>All remaining octets in the current wire boundary.</summary>
    BytesToEnd = 9,

    /// <summary>A length-prefixed vector of bits.</summary>
    U1Vector = 10,

    /// <summary>A length-prefixed vector of unsigned octets.</summary>
    U8Vector = 11,

    /// <summary>A length-prefixed vector of unsigned 16-bit values.</summary>
    U16Vector = 12,

    /// <summary>A length-prefixed vector of unsigned 32-bit values.</summary>
    U32Vector = 13,

    /// <summary>A length-prefixed UTF-8 byte sequence.</summary>
    Utf8Vector = 14,

    /// <summary>One signed 32-bit integer.</summary>
    S32 = 15,
}

/// <summary>
/// Defines reserved zero bits in a binary layout.
/// </summary>
public sealed record ReservedBitsDefinition(int BitCount) : ProtocolMemberDefinition;

/// <summary>
/// References a parameter at an exact position in a container.
/// </summary>
public sealed record ParameterReferenceDefinition(
    string ParameterType,
    Cardinality Cardinality) : ProtocolMemberDefinition;

/// <summary>
/// References a named choice at an exact position in a container.
/// </summary>
public sealed record ChoiceReferenceDefinition(
    string ChoiceType,
    Cardinality Cardinality) : ProtocolMemberDefinition;

/// <summary>
/// Represents a minimum and optional maximum occurrence count.
/// </summary>
public readonly record struct Cardinality
{
    /// <summary>
    /// Initializes and validates an occurrence range.
    /// </summary>
    /// <param name="minimum">The minimum required count.</param>
    /// <param name="maximum">The maximum count, or <see langword="null"/> when unbounded.</param>
    public Cardinality(int minimum, int? maximum)
    {
        if (minimum < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }

        if (maximum is < 0 || maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// Gets the minimum required count.
    /// </summary>
    public int Minimum { get; }

    /// <summary>
    /// Gets the maximum count, or <see langword="null"/> when unbounded.
    /// </summary>
    public int? Maximum { get; }

    /// <summary>
    /// Gets a value indicating whether the upper bound is unbounded.
    /// </summary>
    public bool IsUnbounded => Maximum is null;

    /// <summary>
    /// Creates and validates a cardinality.
    /// </summary>
    public static Cardinality Create(int minimum, int? maximum)
    {
        return new Cardinality(minimum, maximum);
    }
}
