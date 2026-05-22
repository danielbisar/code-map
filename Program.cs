namespace code_map;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        return args[0] switch
        {
            "scan" when args.Length >= 2 => Scanner.Run(args[1]),
            "gen-html" => HtmlGenerator.Run(),
            _ => Usage()
        };
    }

    static int Usage()
    {
        Console.Error.WriteLine("Usage: code-map <scan <dir> | gen-html>");
        return 1;
    }
}
