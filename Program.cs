namespace code_map;

class Program : IDisposable
{
    static void Main(string[] args)
    {
        var dir = args[0];
        using var program = new Program();

        program.TraverseDirs(dir);
    }

    //private readonly StreamWriter _sw;

    public Program()
    {
        //  _sw = new StreamWriter("code.map", false);
    }

    private void TraverseDirs(string dir)
    {
        string ns = string.Empty;

        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            // simplified assumption: namespace ....
            var lines = File.ReadAllLines(file);

            foreach (var line in lines)
            {
                var token = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                if (token.Length > 1)
                {
                    if (token[0] == "namespace")
                        ns = token[1];
                    else if (token.Any(p => p == "class"))
                    {
                        var res = string.Format("{0}\t{1}", ns, token[token.IndexOf("class") + 1]);
                        //_sw.WriteLine(res);
                        Console.WriteLine(res);
                    }
                }

            }
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            TraverseDirs(subDir);
        }
    }

    public void Dispose()
    {
        //_sw.Close();
    }
}
