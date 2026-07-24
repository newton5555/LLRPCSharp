using System.Reflection;
using System.Text;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpCli.Analysis;

public sealed record LlrpSemanticNode(string Name, string? Value = null, IReadOnlyList<LlrpSemanticNode>? Children = null)
{
    public IReadOnlyList<LlrpSemanticNode> Items { get; } = Children ?? Array.Empty<LlrpSemanticNode>();
}

public sealed record FrameAnalysisResult(
    LlrpMessageHeader Header,
    ILlrpMessage? DecodedMessage,
    LlrpStatus? Status,
    LlrpSemanticNode SemanticTree,
    string Summary);

public static class LlrpFrameAnalyzer
{
    public static FrameAnalysisResult Analyze(byte[] frame, ILlrpMessage? message)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(frame);
        LlrpStatus? status = ExtractStatus(message);

        string messageTypeName = message?.GetType().Name ?? header.MessageType.ToString();
        string summary = BuildSummary(header, message, status);
        LlrpSemanticNode tree = BuildSemanticTree(header, message, frame);

        return new FrameAnalysisResult(header, message, status, tree, summary);
    }

    private static LlrpStatus? ExtractStatus(ILlrpMessage? message)
    {
        if (message is null)
        {
            return null;
        }

        PropertyInfo? prop = message.GetType().GetProperty("Status");
        if (prop != null && prop.GetValue(message) is LlrpStatus status)
        {
            return status;
        }

        return null;
    }

    private static string BuildSummary(LlrpMessageHeader header, ILlrpMessage? message, LlrpStatus? status)
    {
        var sb = new StringBuilder();
        string name = message?.GetType().Name ?? $"MessageType({header.MessageType})";
        sb.Append(name);

        if (status != null)
        {
            sb.Append($" [{status.StatusCode}]");
            if (!string.IsNullOrEmpty(status.ErrorDescription))
            {
                sb.Append($" {status.ErrorDescription}");
            }
        }

        return sb.ToString();
    }

    private static LlrpSemanticNode BuildSemanticTree(LlrpMessageHeader header, ILlrpMessage? message, byte[] frame)
    {
        var rootChildren = new List<LlrpSemanticNode>
        {
            new("Header", null, new LlrpSemanticNode[]
            {
                new("ProtocolVersion", $"{header.Version} ({(byte)header.Version})"),
                new("MessageType", $"{header.MessageType} ({(ushort)header.MessageType})"),
                new("MessageId", header.MessageId.ToString()),
                new("DeclaredLength", $"{header.MessageLength} bytes"),
                new("PayloadLength", $"{frame.Length - LlrpMessageHeader.EncodedLength} bytes"),
            })
        };

        if (message != null)
        {
            var bodyChildren = new List<LlrpSemanticNode>();
            BuildObjectTreeNodes(bodyChildren, message, depth: 0);
            rootChildren.Add(new("MessageBody", message.GetType().Name, bodyChildren));
        }

        return new LlrpSemanticNode(message?.GetType().Name ?? "LLRPFrame", $"ID {header.MessageId}", rootChildren);
    }

    private static void BuildObjectTreeNodes(List<LlrpSemanticNode> nodes, object target, int depth)
    {
        if (target is null || depth > 6)
        {
            return;
        }

        PropertyInfo[] properties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (PropertyInfo prop in properties)
        {
            if (prop.Name is "MessageId" or "MessageType" or "Version")
            {
                continue;
            }

            object? value = prop.GetValue(target);
            if (value is null)
            {
                continue;
            }

            string propName = prop.Name;

            if (value is System.Collections.IEnumerable enumerable and not string and not byte[])
            {
                var listChildren = new List<LlrpSemanticNode>();
                int index = 0;
                foreach (object item in enumerable)
                {
                    if (item is null)
                    {
                        continue;
                    }
                    var itemChildren = new List<LlrpSemanticNode>();
                    BuildObjectTreeNodes(itemChildren, item, depth + 1);
                    listChildren.Add(new($"[{index++}] {item.GetType().Name}", null, itemChildren));
                }
                nodes.Add(new(propName, $"Collection ({listChildren.Count} items)", listChildren));
            }
            else if (value.GetType().Assembly == typeof(LlrpMessageHeader).Assembly
                || value.GetType().Assembly == typeof(LlrpTlvParameterHeader).Assembly)
            {
                var childNodes = new List<LlrpSemanticNode>();
                BuildObjectTreeNodes(childNodes, value, depth + 1);
                nodes.Add(new(propName, value.GetType().Name, childNodes));
            }
            else
            {
                string displayValue = value switch
                {
                    byte[] byteArray => Convert.ToHexString(byteArray),
                    _ => value.ToString() ?? string.Empty
                };

                nodes.Add(new(propName, displayValue));
            }
        }
    }
}
