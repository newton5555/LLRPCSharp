using LlrpNet.Protocol.Messages;

namespace LlrpSdk;

using LlrpNet.Protocol.Parameters;

/// <summary>
/// Indicates that a correlated response decoded successfully but had a different CLR message type than requested.
/// </summary>
public sealed class LlrpUnexpectedResponseException : Exception
{
    /// <summary>
    /// Initializes an unexpected-response exception.
    /// </summary>
    /// <param name="requestType">The CLR type of the sent request.</param>
    /// <param name="expectedResponseType">The CLR response type requested by the caller.</param>
    /// <param name="actualResponse">The decoded response received with the request message identifier.</param>
    public LlrpUnexpectedResponseException(
        Type requestType,
        Type expectedResponseType,
        ILlrpMessage actualResponse)
        : base(CreateMessage(requestType, expectedResponseType, actualResponse))
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(expectedResponseType);
        ArgumentNullException.ThrowIfNull(actualResponse);

        RequestType = requestType;
        ExpectedResponseType = expectedResponseType;
        ActualResponse = actualResponse;
    }

    /// <summary>
    /// Gets the CLR type of the sent request.
    /// </summary>
    public Type RequestType { get; }

    /// <summary>
    /// Gets the CLR response type requested by the caller.
    /// </summary>
    public Type ExpectedResponseType { get; }

    /// <summary>
    /// Gets the actual decoded response.
    /// </summary>
    public ILlrpMessage ActualResponse { get; }

    private static string CreateMessage(
        Type requestType,
        Type expectedResponseType,
        ILlrpMessage actualResponse)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(expectedResponseType);
        ArgumentNullException.ThrowIfNull(actualResponse);

        return $"LLRP request {requestType.FullName} with message ID {actualResponse.MessageId} expected " +
            $"response {expectedResponseType.FullName}, but received {actualResponse.GetType().FullName}.";
    }
}

/// <summary>
/// Indicates that a reader session stopped receiving without an explicit disconnect request.
/// </summary>
public sealed class LlrpReaderConnectionException : IOException
{
    /// <summary>
    /// Initializes a reader connection exception.
    /// </summary>
    /// <param name="connectionId">The underlying transport connection identifier.</param>
    /// <param name="message">A description of the interruption.</param>
    /// <param name="innerException">The underlying failure, when available.</param>
    public LlrpReaderConnectionException(
        string connectionId,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ConnectionId = connectionId;
    }

    /// <summary>
    /// Gets the underlying transport connection identifier.
    /// </summary>
    public string ConnectionId { get; }
}

/// <summary>
/// Indicates that a reader operation returned a non-success LLRP status.
/// </summary>
public sealed class LlrpReaderOperationException : Exception
{
    /// <summary>
    /// Initializes a reader operation exception from a normalized LLRP status.
    /// </summary>
    /// <param name="operation">The logical reader operation.</param>
    /// <param name="statusCode">The non-success LLRP status code.</param>
    /// <param name="errorDescription">The reader-provided error description.</param>
    /// <param name="rawStatus">The exact versioned status parameter.</param>
    public LlrpReaderOperationException(
        string operation,
        ushort statusCode,
        string errorDescription,
        ILlrpParameter rawStatus)
        : this(operation, statusCode, errorDescription, rawStatus, statusName: null)
    {
    }

    /// <summary>
    /// Initializes a reader operation exception with an explicit LLRP status name. The name is resolved by the
    /// version boundary (which owns the versioned StatusCode enum); the exception itself stays version-neutral.
    /// </summary>
    internal LlrpReaderOperationException(
        string operation,
        ushort statusCode,
        string errorDescription,
        ILlrpParameter rawStatus,
        string? statusName)
        : base(CreateMessage(operation, statusCode, errorDescription, statusName))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(rawStatus);

        Operation = operation;
        StatusCode = statusCode;
        ErrorDescription = errorDescription ?? string.Empty;
        RawStatus = rawStatus;
    }

    /// <summary>
    /// Gets the logical reader operation.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the exact versioned status parameter, including nested error parameters.
    /// </summary>
    public ILlrpParameter RawStatus { get; }

    /// <summary>
    /// Gets the standard LLRP status code.
    /// </summary>
    public ushort StatusCode { get; }

    /// <summary>
    /// Gets the reader-provided error description.
    /// </summary>
    public string ErrorDescription { get; }

    private static string CreateMessage(
        string operation,
        ushort statusCode,
        string errorDescription,
        string? statusName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        string description = string.IsNullOrWhiteSpace(errorDescription)
            ? "No error description was supplied."
            : errorDescription;
        string status = statusName is null
            ? $"({statusCode})"
            : $"{statusName} ({statusCode})";
        return $"Reader operation {operation} failed with LLRP status {status}: {description}";
    }
}

/// <summary>
/// Indicates that a managed SDK deployment or tag-access operation would exceed a reader resource limit.
/// </summary>
public sealed class LlrpResourceCapacityException : InvalidOperationException
{
    /// <summary>Initializes a capacity diagnostic.</summary>
    public LlrpResourceCapacityException(
        string resourceType,
        uint? limit,
        uint current,
        uint required,
        ResourceTakeoverPolicy takeoverPolicy,
        string? detail = null)
        : base(CreateMessage(resourceType, limit, current, required, takeoverPolicy, detail))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ResourceType = resourceType;
        Limit = limit;
        Current = current;
        Required = required;
        TakeoverPolicy = takeoverPolicy;
        Detail = detail ?? string.Empty;
    }

    /// <summary>Gets the logical resource type, such as <c>ROSpec</c> or <c>AccessSpec</c>.</summary>
    public string ResourceType { get; }

    /// <summary>Gets the advertised limit, or <see langword="null"/> when unknown.</summary>
    public uint? Limit { get; }

    /// <summary>Gets the number of resources observed before deployment.</summary>
    public uint Current { get; }

    /// <summary>Gets the final number required by the requested deployment.</summary>
    public uint Required { get; }

    /// <summary>Gets the takeover policy used for the planned deployment.</summary>
    public ResourceTakeoverPolicy TakeoverPolicy { get; }

    /// <summary>Gets optional operation-specific detail.</summary>
    public string Detail { get; }

    private static string CreateMessage(
        string resourceType,
        uint? limit,
        uint current,
        uint required,
        ResourceTakeoverPolicy takeoverPolicy,
        string? detail)
    {
        string limitText = limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
        string policyText = takeoverPolicy == ResourceTakeoverPolicy.PreserveForeign
            ? "PreserveForeign"
            : "ReplaceAll";
        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return $"Reader {resourceType} capacity is insufficient (current {current}, required {required}, limit {limitText}, policy {policyText})." + suffix;
    }
}

/// <summary>
/// Indicates that a successful protocol response could not initialize a consistent reader model.
/// </summary>
public sealed class LlrpReaderInitializationException : Exception
{
    /// <summary>
    /// Initializes a reader model validation failure.
    /// </summary>
    /// <param name="message">A description of the invalid initialization response.</param>
    /// <param name="innerException">The underlying decoding or validation failure, when applicable.</param>
    public LlrpReaderInitializationException(string message, Exception? innerException = null)
        : base(ValidateMessage(message), innerException)
    {
    }

    private static string ValidateMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return message;
    }
}

/// <summary>
/// Indicates that application consumers did not drain reader-initiated messages before the bounded queue filled.
/// </summary>
public sealed class LlrpReaderBackpressureException : Exception
{
    /// <summary>
    /// Initializes a decoded-message backpressure failure.
    /// </summary>
    /// <param name="connectionId">The underlying transport connection identifier.</param>
    /// <param name="capacity">The configured decoded-message queue capacity.</param>
    public LlrpReaderBackpressureException(string connectionId, int capacity)
        : base(
            $"LLRP reader {connectionId} filled its bounded queue of {capacity} decoded " +
            "reader-initiated messages because application consumers did not keep up.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ConnectionId = connectionId;
        Capacity = capacity;
    }

    /// <summary>
    /// Gets the transport connection identifier.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the configured decoded-message queue capacity.
    /// </summary>
    public int Capacity { get; }
}
