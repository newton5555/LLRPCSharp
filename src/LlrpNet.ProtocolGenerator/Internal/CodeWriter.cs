using System.Text;

namespace LlrpNet.ProtocolGenerator.Internal;

internal sealed class CodeWriter
{
    private readonly StringBuilder builder = new();
    private int indentation;

    public void WriteLine(string value = "")
    {
        if (value.Length > 0)
        {
            builder.Append(' ', indentation * 4);
            builder.Append(value);
        }

        builder.Append('\n');
    }

    public IDisposable Indent()
    {
        indentation++;
        return new Indentation(this);
    }

    public override string ToString() => builder.ToString();

    private sealed class Indentation(CodeWriter owner) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (!disposed)
            {
                owner.indentation--;
                disposed = true;
            }
        }
    }
}
