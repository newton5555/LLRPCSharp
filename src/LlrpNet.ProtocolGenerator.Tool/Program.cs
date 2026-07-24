using System.Text;
using LlrpNet.ProtocolGenerator;
using LlrpNet.ProtocolGenerator.Generation;
using LlrpNet.ProtocolModel.Definitions;
using LlrpNet.ProtocolModel.Import;
using LlrpNet.ProtocolModel.Validation;

namespace LlrpNet.ProtocolGenerator.Tool;

/// <summary>Command-line entry point for deterministic protocol source generation.</summary>
public static class Program
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Runs the protocol generator.</summary>
    public static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            ProtocolDefinition definition = Import(options.InputPath);
            ProtocolDefinition[] dependencies = options.DependencyPaths.Select(Import).ToArray();
            ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(
                definition,
                new ProtocolGenerationOptions
                {
                    RootNamespace = options.RootNamespace,
                    VersionNamespace = options.VersionNamespace,
                    GenerateCodecs = options.GenerateCodecs,
                    ProtocolVersionValue = checked((byte)options.ProtocolVersion),
                    RegistryModuleName = options.RegistryModuleName,
                },
                new ProtocolDefinitionValidationContext(dependencies));

            if (!result.Succeeded)
            {
                foreach (ProtocolGenerationDiagnostic diagnostic in result.Diagnostics)
                {
                    Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
                }

                return 1;
            }

            int changed = WriteSources(options, result.Sources);
            Console.WriteLine($"Generated {result.Sources.Count} source files; {changed} changed.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or DefinitionImportException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static ProtocolDefinition Import(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant() switch
        {
            ".xml" => new LtkXmlDefinitionImporter().Import(inputPath),
            ".yaml" or ".yml" => new YamlProtocolDefinitionImporter().Import(inputPath),
            _ => throw new ArgumentException("The input definition must have a .xml, .yaml, or .yml extension.", nameof(inputPath)),
        };
    }

    private static int WriteSources(Options options, IReadOnlyList<GeneratedSourceFile> sources)
    {
        string outputRoot = Path.GetFullPath(options.OutputPath);
        int changed = 0;
        foreach (GeneratedSourceFile source in sources)
        {
            string relativePath = source.HintName.Replace('/', Path.DirectorySeparatorChar);
            string targetPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
            if (!targetPath.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Generated source path escapes the output directory: {source.HintName}.");
            }

            string expected = source.SourceText.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
            if (File.Exists(targetPath) && File.ReadAllText(targetPath) == expected)
            {
                continue;
            }

            changed++;
            if (options.Verify)
            {
                Console.Error.WriteLine($"Generated source is missing or stale: {targetPath}");
                continue;
            }

            string? directory = Path.GetDirectoryName(targetPath);
            if (directory is null)
            {
                throw new InvalidOperationException($"Unable to determine output directory for {targetPath}.");
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(targetPath, expected, Utf8Bom);
        }

        return options.Verify && changed != 0 ? throw new InvalidOperationException("Generated sources are not up to date.") : changed;
    }

    private sealed record Options(
        string InputPath,
        string OutputPath,
        string RootNamespace,
        string VersionNamespace,
        int ProtocolVersion,
        IReadOnlyList<string> DependencyPaths,
        string? RegistryModuleName,
        bool GenerateCodecs,
        bool Verify)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var dependencies = new List<string>();
            bool codecs = false;
            bool verify = false;
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (argument == "--codecs")
                {
                    codecs = true;
                    continue;
                }

                if (argument == "--verify")
                {
                    verify = true;
                    continue;
                }

                if (argument == "--dependency")
                {
                    if (index + 1 >= args.Length)
                    {
                        throw Usage();
                    }

                    dependencies.Add(args[++index]);
                    continue;
                }

                if (argument is not ("--input" or "--output" or "--root-namespace" or "--version-namespace" or "--protocol-version" or "--registry-module-name") || index + 1 >= args.Length)
                {
                    throw Usage();
                }

                if (!values.TryAdd(argument, args[++index]))
                {
                    throw new ArgumentException($"{argument} was specified more than once.");
                }
            }

            string input = Required(values, "--input");
            if (!File.Exists(input))
            {
                throw new ArgumentException($"Input definition does not exist: {input}.");
            }

            foreach (string dependency in dependencies)
            {
                if (!File.Exists(dependency))
                {
                    throw new ArgumentException($"Dependency definition does not exist: {dependency}.");
                }
            }

            string output = Required(values, "--output");
            string rootNamespace = Required(values, "--root-namespace");
            string versionNamespace = Required(values, "--version-namespace");
            string protocolVersionText = Required(values, "--protocol-version");
            if (!int.TryParse(protocolVersionText, out int protocolVersion) || protocolVersion is < 1 or > 3)
            {
                throw new ArgumentException("--protocol-version must be 1, 2, or 3.");
            }

            values.TryGetValue("--registry-module-name", out string? registryModuleName);
            return new Options(input, output, rootNamespace, versionNamespace, protocolVersion, dependencies, registryModuleName, codecs, verify);
        }

        private static string Required(IReadOnlyDictionary<string, string> values, string name)
        {
            return values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : throw Usage();
        }

        private static ArgumentException Usage() => new(
            "Usage: --input <definition.xml|yaml> --output <directory> --root-namespace <namespace> " +
            "--version-namespace <V1_0_1> --protocol-version <1|2|3> [--dependency <definition>]... " +
            "[--registry-module-name <name>] [--codecs] [--verify]");
    }
}
