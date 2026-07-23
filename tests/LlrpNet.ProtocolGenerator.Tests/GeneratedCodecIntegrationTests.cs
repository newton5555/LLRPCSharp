using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using System.Text;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpNet.ProtocolGenerator.Generation;
using LlrpNet.ProtocolModel.Definitions;
using LlrpNet.ProtocolModel.Import;

namespace LlrpNet.ProtocolGenerator.Tests;

public sealed class GeneratedCodecIntegrationTests
{
    [Fact]
    public void GenerateCodecs_CoreSourcesCompileAndRoundTripGoldenVectors()
    {
        string definitionPath = Path.Combine(AppContext.BaseDirectory, "TestData", "llrp-1x0-def.xml");
        ProtocolDefinition definition = new LtkXmlDefinitionImporter().Import(definitionPath);
        var options = new ProtocolGenerationOptions
        {
            RootNamespace = "Generated.Llrp",
            VersionNamespace = "V1_0_1",
            GenerateCodecs = true,
            ProtocolVersionValue = 1,
        };

        ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(definition, options);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Diagnostics.Count(static diagnostic => diagnostic.Code == "LLRPG005"));
        Assert.Equal(153, result.Sources.Count(static source => source.Kind == GeneratedSourceKind.Codec));
        Assert.Single(result.Sources, static source => source.Kind == GeneratedSourceKind.CodecRuntime);
        GeneratedSourceFile moduleSource = Assert.Single(
            result.Sources,
            static source => source.Kind == GeneratedSourceKind.RegistryModule);
        int firstParameterRegistration = moduleSource.SourceText.IndexOf(
            "RegisterTvParameter",
            StringComparison.Ordinal);
        int firstMessageRegistration = moduleSource.SourceText.IndexOf(
            "RegisterMessage",
            StringComparison.Ordinal);
        Assert.InRange(firstParameterRegistration, 0, firstMessageRegistration - 1);

        Assembly generatedAssembly = CompileGeneratedSources(result.Sources);
        var registry = new LlrpCodecRegistry();
        RegisterGeneratedModule(generatedAssembly, registry);

        AssertGetReaderCapabilitiesVector(generatedAssembly, registry);
        AssertAntennaPropertiesVector(generatedAssembly, registry);
        AssertBitVectorParameter(generatedAssembly, registry);
        AssertEnumerationVectorParameter(generatedAssembly, registry);
        AssertCustomSlotsPreserveRawAndTypedParameters(generatedAssembly, registry);
    }

    private static void AssertGetReaderCapabilitiesVector(
        Assembly assembly,
        LlrpCodecRegistry registry)
    {
        Type requestedDataType = RequireType(
            assembly,
            "Generated.Llrp.Enumerations.V1_0_1.GetReaderCapabilitiesRequestedData");
        object requestedAll = Enum.ToObject(requestedDataType, 0);
        object message = Create(
            assembly,
            "Generated.Llrp.Messages.V1_0_1.GET_READER_CAPABILITIES",
            0x01020304U,
            requestedAll,
            Array.Empty<ILlrpParameter>());

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, (LlrpNet.Protocol.Messages.ILlrpMessage)message);

        Assert.Equal(
            [0x04, 0x01, 0x00, 0x00, 0x00, 0x0B, 0x01, 0x02, 0x03, 0x04, 0x00],
            encoded);
        object decoded = registry.DecodeMessage(encoded);
        Assert.Equal(message.GetType(), decoded.GetType());
        Assert.Equal(0x01020304U, decoded.GetType().GetProperty("MessageId")!.GetValue(decoded));
    }

    private static void AssertAntennaPropertiesVector(
        Assembly assembly,
        LlrpCodecRegistry registry)
    {
        object parameter = Create(
            assembly,
            "Generated.Llrp.Parameters.V1_0_1.AntennaProperties",
            true,
            (ushort)0x1234,
            (short)-200);

        byte[] encoded = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            (ILlrpParameter)parameter);

        Assert.Equal([0x00, 0xDD, 0x00, 0x09, 0x80, 0x12, 0x34, 0xFF, 0x38], encoded);
        object decoded = registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded).Parameter;
        Assert.Equal(true, decoded.GetType().GetProperty("AntennaConnected")!.GetValue(decoded));
        Assert.Equal((short)-200, decoded.GetType().GetProperty("AntennaGain")!.GetValue(decoded));
    }

    private static void AssertBitVectorParameter(
        Assembly assembly,
        LlrpCodecRegistry registry)
    {
        object parameter = Create(
            assembly,
            "Generated.Llrp.Parameters.V1_0_1.EPCData",
            new[] { true, false, true, false, true });

        byte[] encoded = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            (ILlrpParameter)parameter);

        Assert.Equal([0x00, 0xF1, 0x00, 0x07, 0x00, 0x05, 0xA8], encoded);
        object decoded = registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded).Parameter;
        var values = Assert.IsAssignableFrom<IReadOnlyList<bool>>(
            decoded.GetType().GetProperty("EPC")!.GetValue(decoded));
        Assert.Equal([true, false, true, false, true], values);
    }

    private static void AssertEnumerationVectorParameter(
        Assembly assembly,
        LlrpCodecRegistry registry)
    {
        Type enumerationType = RequireType(assembly, "Generated.Llrp.Enumerations.V1_0_1.AirProtocols");
        Array protocols = Array.CreateInstance(enumerationType, 2);
        protocols.SetValue(Enum.ToObject(enumerationType, 0), 0);
        protocols.SetValue(Enum.ToObject(enumerationType, 1), 1);
        object parameter = Create(
            assembly,
            "Generated.Llrp.Parameters.V1_0_1.PerAntennaAirProtocol",
            (ushort)0x1234,
            protocols);

        byte[] encoded = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            (ILlrpParameter)parameter);

        Assert.Equal([0x00, 0x8C, 0x00, 0x0A, 0x12, 0x34, 0x00, 0x02, 0x00, 0x01], encoded);
        object decoded = registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded).Parameter;
        var values = Assert.IsAssignableFrom<IEnumerable>(
            decoded.GetType().GetProperty("ProtocolID")!.GetValue(decoded));
        Assert.Equal(2, values.Cast<object>().Count());
    }

    private static void AssertCustomSlotsPreserveRawAndTypedParameters(
        Assembly assembly,
        LlrpCodecRegistry registry)
    {
        var raw = new RawCustomParameter(
            LlrpProtocolVersion.Version101,
            vendorId: 25882,
            subtype: 7,
            data: new byte[] { 0xAA, 0x55 });
        object rawMessage = CreateCapabilitiesMessage(assembly, raw);
        byte[] rawFrame = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            (LlrpNet.Protocol.Messages.ILlrpMessage)rawMessage);
        object decodedRawMessage = registry.DecodeMessage(rawFrame);
        Assert.IsType<RawCustomParameter>(GetSingleCustom(decodedRawMessage));

        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            vendorId: 25882,
            parameterSubtype: 8,
            new TestCustomParameterCodec());
        var typed = new TestCustomParameter(0x5A);
        object typedMessage = CreateCapabilitiesMessage(assembly, typed);
        byte[] typedFrame = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            (LlrpNet.Protocol.Messages.ILlrpMessage)typedMessage);
        object decodedTypedMessage = registry.DecodeMessage(typedFrame);
        Assert.Equal(typed, Assert.IsType<TestCustomParameter>(GetSingleCustom(decodedTypedMessage)));
    }

    private static object CreateCapabilitiesMessage(Assembly assembly, ILlrpParameter custom)
    {
        Type requestedDataType = RequireType(
            assembly,
            "Generated.Llrp.Enumerations.V1_0_1.GetReaderCapabilitiesRequestedData");
        return Create(
            assembly,
            "Generated.Llrp.Messages.V1_0_1.GET_READER_CAPABILITIES",
            91U,
            Enum.ToObject(requestedDataType, 0),
            new[] { custom });
    }

    private static object GetSingleCustom(object message)
    {
        var customValues = Assert.IsAssignableFrom<IEnumerable>(
            message.GetType().GetProperty("CustomItems")!.GetValue(message));
        return Assert.Single(customValues.Cast<object>());
    }

    private static void RegisterGeneratedModule(Assembly assembly, LlrpCodecRegistry registry)
    {
        Type module = RequireType(
            assembly,
            "Generated.Llrp.Registry.V1_0_1.V1_0_1ProtocolModule");
        module.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(obj: null, [registry]);
    }

    private static object Create(Assembly assembly, string typeName, params object?[] arguments)
    {
        return Activator.CreateInstance(RequireType(assembly, typeName), arguments)
            ?? throw new InvalidOperationException($"Could not create generated type '{typeName}'.");
    }

    private static Type RequireType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)!;
    }

    private static Assembly CompileGeneratedSources(IReadOnlyList<GeneratedSourceFile> sources)
    {
        string repositoryRoot = FindRepositoryRoot();
        string temporaryDirectory = Path.Combine(
            repositoryRoot,
            "tests",
            "LlrpNet.ProtocolGenerator.Tests",
            "obj",
            "GeneratedCodecSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string coreProject = Path.Combine(repositoryRoot, "src", "LlrpNet.Core", "LlrpNet.Core.csproj");
            string protocolProject = Path.Combine(repositoryRoot, "src", "LlrpNet.Protocol", "LlrpNet.Protocol.csproj");
            string projectPath = Path.Combine(temporaryDirectory, "GeneratedCodecSmoke.csproj");
            string project = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{SecurityElement.Escape(coreProject)}}" />
                    <ProjectReference Include="{{SecurityElement.Escape(protocolProject)}}" />
                  </ItemGroup>
                </Project>
                """;
            File.WriteAllText(projectPath, project, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            foreach (GeneratedSourceFile source in sources)
            {
                string path = Path.Combine(temporaryDirectory, source.HintName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, source.SourceText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }

            string localPackages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
            Assert.True(Directory.Exists(localPackages), $"Local NuGet package source not found: {localPackages}");
            RunDotnet(
                temporaryDirectory,
                timeoutMilliseconds: 60_000,
                "restore",
                projectPath,
                "--source",
                localPackages,
                "--ignore-failed-sources");
            RunDotnet(
                temporaryDirectory,
                timeoutMilliseconds: 120_000,
                "build",
                projectPath,
                "--configuration",
                "Debug",
                "--no-restore",
                "--nologo",
                "--verbosity",
                "minimal");
            string assemblyPath = Path.Combine(
                temporaryDirectory,
                "bin",
                "Debug",
                "net10.0",
                "GeneratedCodecSmoke.dll");
            using var stream = new MemoryStream(File.ReadAllBytes(assemblyPath));
            return AssemblyLoadContext.Default.LoadFromStream(stream);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static void RunDotnet(
        string workingDirectory,
        int timeoutMilliseconds,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet compile smoke process.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet {arguments[0]} exceeded {timeoutMilliseconds / 1000} seconds.");
        }

        string output = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LLRPCSharp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed record TestCustomParameter(byte Value) : ILlrpParameter;

    private sealed class TestCustomParameterCodec : LlrpCustomParameterCodec<TestCustomParameter>
    {
        public override TestCustomParameter Decode(
            LlrpProtocolVersion version,
            ReadOnlySpan<byte> data)
        {
            Assert.Equal(LlrpProtocolVersion.Version101, version);
            return new TestCustomParameter(Assert.Single(data.ToArray()));
        }

        public override int GetEncodedDataLength(
            LlrpProtocolVersion version,
            TestCustomParameter parameter)
        {
            Assert.Equal(LlrpProtocolVersion.Version101, version);
            return 1;
        }

        public override int EncodeData(
            LlrpProtocolVersion version,
            TestCustomParameter parameter,
            Span<byte> destination)
        {
            Assert.Equal(LlrpProtocolVersion.Version101, version);
            Assert.Single(destination.ToArray());
            destination[0] = parameter.Value;
            return 1;
        }
    }
}
