using System.Text;

namespace LlrpCli;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        return new LlrpCliApplication().Run(args, Console.Out, Console.Error);
    }
}
