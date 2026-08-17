using System.Text;

namespace LlrpVirtualDevice.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return await new VirtualDeviceCliApplication().RunAsync(args, Console.Out, Console.Error).ConfigureAwait(false);
    }
}
