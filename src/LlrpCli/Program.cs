namespace LlrpCli;

public static class Program
{
    public static int Main(string[] args)
    {
        return new LlrpCliApplication().Run(args, Console.Out, Console.Error);
    }
}

