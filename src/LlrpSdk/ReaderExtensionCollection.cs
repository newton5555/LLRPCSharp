using LlrpSdk.Extensions;

namespace LlrpSdk;

internal sealed class ReaderExtensionCollection : IReaderExtensionCollection
{
    private IReadOnlyList<IReaderExtension> items = [];

    public int Count => items.Count;

    public TExtension? Get<TExtension>()
        where TExtension : class, IReaderExtension => items.OfType<TExtension>().FirstOrDefault();

    public IEnumerator<IReaderExtension> GetEnumerator() => items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public void Replace(IEnumerable<IReaderExtension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        items = Array.AsReadOnly(extensions.ToArray());
    }
}
