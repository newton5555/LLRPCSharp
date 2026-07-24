namespace LlrpSdk.Extensions;

/// <summary>Provides the reader extensions activated for one connected reader.</summary>
public interface IReaderExtensionCollection : IReadOnlyCollection<IReaderExtension>
{
    /// <summary>Returns the activated extension of type <typeparamref name="TExtension"/>, if present.</summary>
    public TExtension? Get<TExtension>()
        where TExtension : class, IReaderExtension;
}
