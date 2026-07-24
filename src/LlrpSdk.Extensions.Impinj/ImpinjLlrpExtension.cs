using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>Registers the generated Impinj LLRP 1.0.1 custom codecs before a reader connects.</summary>
public sealed class ImpinjProtocolModule : ILlrpProtocolModule
{
    /// <summary>Gets the singleton Impinj protocol module.</summary>
    public static ImpinjProtocolModule Instance { get; } = new();

    /// <inheritdoc />
    public string Id => "impinj-llrp-1.0.1";

    /// <inheritdoc />
    public void Register(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry.V1_0_1.ImpinjProtocolModule.Register(registry);
    }
}

/// <summary>Marks a connected LLRP 1.0.1 reader as an Impinj reader.</summary>
public sealed class ImpinjReaderExtension : IReaderExtension
{
    /// <summary>Gets the IANA manufacturer identifier assigned to Impinj.</summary>
    public const uint ManufacturerId = 25882;

    /// <summary>Gets the singleton reader extension.</summary>
    public static ImpinjReaderExtension Instance { get; } = new();

    /// <inheritdoc />
    public string Id => "impinj-reader-llrp-1.0.1";

    /// <inheritdoc />
    public string? MutualExclusionGroup => "reader-vendor";

    /// <inheritdoc />
    public bool Matches(ReaderExtensionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ManufacturerId == ManufacturerId &&
            context.ProtocolVersion == LlrpProtocolVersion.Version101;
    }
}

/// <summary>Configures an <see cref="LlrpReaderBuilder"/> for Impinj LLRP 1.0.1 custom data.</summary>
public static class ImpinjLlrpReaderBuilderExtensions
{
    /// <summary>
    /// Registers Impinj codecs before connection and activates the Impinj reader extension after standard initialization.
    /// </summary>
    /// <param name="builder">The reader builder to configure.</param>
    /// <returns>The same builder.</returns>
    public static LlrpReaderBuilder UseImpinj(this LlrpReaderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseProtocolModule(ImpinjProtocolModule.Instance)
            .UseReaderExtension(ImpinjReaderExtension.Instance);
    }
}
