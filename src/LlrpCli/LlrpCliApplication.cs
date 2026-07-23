using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Protocol;
using LlrpCli.Commands;

namespace LlrpCli;

/// <summary>
/// Hosts the command-line protocol tools independently from process-global console state.
/// </summary>
public sealed class LlrpCliApplication
{
    /// <summary>
    /// Executes one CLI invocation.
    /// </summary>
    /// <param name="args">Command-line arguments excluding the executable name.</param>
    /// <param name="output">Standard output destination.</param>
    /// <param name="error">Standard error destination.</param>
    /// <returns>Zero on success, two for usage errors, or three for invalid protocol input.</returns>
    public int Run(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output),
            Ansi = AnsiSupport.No,
        });

        var app = new CommandApp(new TypeRegistrar(console));
        app.SetDefaultCommand<LiveCommand>()
            .WithDescription("Launch interactive live LLRP session.");

        app.Configure(config =>
        {
            config.ConfigureConsole(console);
            config.SetApplicationName("llrp");
            config.UseStrictParsing();
            config.PropagateExceptions();

            config.AddCommand<InspectCommand>("inspect")
                .WithDescription("Inspect metadata and header fields of one LLRP hex frame.");

            config.AddCommand<DecodeCommand>("decode")
                .WithDescription("Decode one LLRP hex frame into parameter tree or JSON format.");

            config.AddCommand<ValidateCommand>("validate")
                .WithDescription("Validate the length and structural integrity of an LLRP frame.");

            config.AddCommand<EncodeCommand>("encode")
                .WithDescription("Encode standard LLRP messages into hexadecimal format.");

            config.AddCommand<ConnectCommand>("connect")
                .WithDescription("Connect to an LLRP Reader and display device identity.");

            config.AddCommand<MonitorCommand>("monitor")
                .WithDescription("Connect to an LLRP Reader and stream real-time TX/RX LLRP frames.");
        });

        try
        {
            return app.Run(args);
        }
        catch (CommandParseException exception)
        {
            error.WriteLine(exception.Message);
            error.WriteLine("Run 'llrp --help' for usage.");
            return 2;
        }
        catch (CliUsageException exception)
        {
            error.WriteLine(exception.Message);
            error.WriteLine("Run 'llrp --help' for usage.");
            return 2;
        }
        catch (Exception exception) when (
            exception is FormatException
                or OverflowException
                or ArgumentException
                or LlrpProtocolException)
        {
            error.WriteLine($"Invalid LLRP input: {exception.Message}");
            return 3;
        }
    }

    private sealed class TypeRegistrar(IAnsiConsole console) : ITypeRegistrar
    {
        public void Register(Type service, Type implementation) { }
        public void RegisterInstance(Type service, object implementation) { }
        public void RegisterLazy(Type service, Func<object> factory) { }
        public ITypeResolver Build() => new TypeResolver(console);
    }

    private sealed class TypeResolver(IAnsiConsole console) : ITypeResolver
    {
        public object? Resolve(Type? type)
        {
            if (type == null)
            {
                return null;
            }
            if (type == typeof(IAnsiConsole))
            {
                return console;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                Type itemType = type.GetGenericArguments()[0];
                return Array.CreateInstance(itemType, 0);
            }
            if (type.IsInterface || type.IsAbstract)
            {
                return null;
            }
            try
            {
                if (type.GetConstructor(new[] { typeof(IAnsiConsole) }) != null)
                {
                    return Activator.CreateInstance(type, console);
                }
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }
    }
}
