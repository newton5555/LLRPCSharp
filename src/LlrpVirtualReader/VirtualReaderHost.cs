using System.Net;
using System.Net.Sockets;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpVirtualReader;

/// <summary>Small stateful LLRP 1.0.1 TCP server for SDK and integration development.</summary>
public sealed class VirtualReaderHost : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly LlrpCodecRegistry registry = new();
    private readonly Dictionary<uint, ROSpec> roSpecs = [];
    private readonly HashSet<uint> enabledRoSpecs = [];
    private readonly CancellationTokenSource cancellation = new();
    private Task? acceptLoop;

    public VirtualReaderHost(int port = 0)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        Llrp101StandardModule.Register(registry);
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public void Start()
    {
        if (acceptLoop is not null)
        {
            throw new InvalidOperationException("The virtual reader is already running.");
        }
        listener.Start();
        acceptLoop = AcceptAsync(cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        listener.Stop();
        if (acceptLoop is not null)
        {
            await acceptLoop.ConfigureAwait(false);
        }
        cancellation.Dispose();
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = ServeAsync(client, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        {
            var headerBuffer = new byte[LlrpMessageHeader.EncodedLength];
            while (!token.IsCancellationRequested && await ReadExactAsync(stream, headerBuffer, token).ConfigureAwait(false))
            {
                LlrpMessageHeader header = LlrpMessageHeader.Decode(headerBuffer);
                byte[] frame = new byte[checked((int)header.MessageLength)];
                headerBuffer.CopyTo(frame, 0);
                if (!await ReadExactAsync(stream, frame.AsMemory(headerBuffer.Length), token).ConfigureAwait(false))
                {
                    return;
                }
                ILlrpMessage request = registry.DecodeMessage(frame);
                ILlrpMessage response = request switch
                {
                    GetReaderCapabilities => Capabilities(header.MessageId),
                    ADD_ROSPEC add => AddRoSpec(add),
                    GET_ROSPECS get => GetRoSpecs(get),
                    DELETE_ROSPEC delete => DeleteRoSpec(delete),
                    ENABLE_ROSPEC enable => EnableRoSpec(enable),
                    DISABLE_ROSPEC disable => DisableRoSpec(disable),
                    START_ROSPEC start => StartRoSpec(start),
                    STOP_ROSPEC stop => StopRoSpec(stop),
                    _ => new ErrorMessage(header.MessageId, new LLRPStatus(StatusCode.M_UnsupportedMessage, "Virtual reader does not implement this request.", null, null)),
                };
                byte[] responseFrame = registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
                await stream.WriteAsync(responseFrame, token).ConfigureAwait(false);
            }
        }
    }

    private static GetReaderCapabilitiesResponse Capabilities(uint messageId) => new(
        messageId,
        new LLRPStatus(StatusCode.M_Success, string.Empty, null, null),
        new GeneralDeviceCapabilities(4, true, true, 0, 0, "virtual-reader", [new ReceiveSensitivityTableEntry(1, 0)], [], new GPIOCapabilities(0, 0), [new PerAntennaAirProtocol(1, [AirProtocols.Unspecified])]),
        null, null, null, []);

    private ADD_ROSPEC_RESPONSE AddRoSpec(ADD_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryAdd(request.ROSpec.ROSpecID, request.ROSpec))
            {
                return new ADD_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_ParameterError, "ROSpec already exists."));
            }
        }

        return new ADD_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private GET_ROSPECS_RESPONSE GetRoSpecs(GET_ROSPECS request)
    {
        ROSpec[] items;
        lock (roSpecs)
        {
            items = roSpecs.Values.OrderBy(static item => item.ROSpecID).ToArray();
        }

        return new GET_ROSPECS_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty), items);
    }

    private DELETE_ROSPEC_RESPONSE DeleteRoSpec(DELETE_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.Remove(request.ROSpecID))
            {
                return new DELETE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            enabledRoSpecs.Remove(request.ROSpecID);
        }

        return new DELETE_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private ENABLE_ROSPEC_RESPONSE EnableRoSpec(ENABLE_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new ENABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            roSpecs[request.ROSpecID] = roSpec with { CurrentState = ROSpecState.Inactive };
            enabledRoSpecs.Add(request.ROSpecID);
        }

        return new ENABLE_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private DISABLE_ROSPEC_RESPONSE DisableRoSpec(DISABLE_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new DISABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            roSpecs[request.ROSpecID] = roSpec with { CurrentState = ROSpecState.Disabled };
            enabledRoSpecs.Remove(request.ROSpecID);
        }

        return new DISABLE_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private START_ROSPEC_RESPONSE StartRoSpec(START_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new START_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            if (!enabledRoSpecs.Contains(request.ROSpecID))
            {
                return new START_ROSPEC_RESPONSE(
                    request.MessageId,
                    Status(StatusCode.M_ParameterError, "ROSpec must be enabled before it can be started."));
            }

            roSpecs[request.ROSpecID] = roSpec with { CurrentState = ROSpecState.Active };
        }

        return new START_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private STOP_ROSPEC_RESPONSE StopRoSpec(STOP_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new STOP_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            roSpecs[request.ROSpecID] = roSpec with
            {
                CurrentState = enabledRoSpecs.Contains(request.ROSpecID)
                    ? ROSpecState.Inactive
                    : ROSpecState.Disabled,
            };
        }

        return new STOP_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private static LLRPStatus MissingRoSpec(uint roSpecId) =>
        Status(StatusCode.M_ParameterError, $"ROSpec {roSpecId} does not exist.");

    private static LLRPStatus Status(StatusCode code, string description) => new(code, description, null, null);

    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], token).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }
}
