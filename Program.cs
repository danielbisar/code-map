namespace code_map;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        try
        {
            return args switch
            {
                ["scan", var dir] => Scanner.Run(dir),
                ["gen-html"] => HtmlGenerator.Run(),
                _ => Usage()
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
                                       System.Text.Json.JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    static int Usage()
    {
        Console.Error.WriteLine("Usage: code-map <scan <dir> | gen-html>");
        return 1;
    }
}
