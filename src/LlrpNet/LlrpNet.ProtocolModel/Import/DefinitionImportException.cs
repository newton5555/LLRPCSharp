namespace LlrpNet.ProtocolModel.Import;

/// <summary>
/// Reports a malformed or unsupported external protocol definition.
/// </summary>
public sealed class DefinitionImportException : FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionImportException"/> class.
    /// </summary>
    public DefinitionImportException(
        string sourceName,
        int lineNumber,
        int linePosition,
        string message,
        Exception? innerException = null)
        : base(FormatMessage(sourceName, lineNumber, linePosition, message), innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        SourceName = sourceName;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>
    /// Gets the logical source name.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the one-based line number, or zero when unavailable.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the one-based line position, or zero when unavailable.
    /// </summary>
    public int LinePosition { get; }

    private static string FormatMessage(
        string sourceName,
        int lineNumber,
        int linePosition,
        string message)
    {
        string location = lineNumber > 0
            ? $"{sourceName}({lineNumber},{linePosition})"
            : sourceName;
        return $"{location}: {message}";
    }
}
