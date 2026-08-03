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
        : base(CreateMessage(operation, statusCode, errorDescription))
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

    private static string CreateMessage(string operation, ushort statusCode, string errorDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        string description = string.IsNullOrWhiteSpace(errorDescription)
            ? "No error description was supplied."
            : errorDescription;
        string statusName = Enum.GetName(
            typeof(LlrpNet.Protocol.Enumerations.V1_1.StatusCode),
            (long)statusCode) ?? "Unknown";
        return $"Reader operation {operation} failed with LLRP status " +
            $"{statusName} ({statusCode}): {description}";
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
