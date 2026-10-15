using System.Diagnostics;

namespace code_map.Tests;

sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "code-map-tests", Guid.NewGuid().ToString("N"));

    public TestDirectory(params (string Name, string Source)[] files)
    {
        Directory.CreateDirectory(Path);
        foreach (var (name, source) in files)
            Write(name, source);
    }

    public void Write(string name, string content)
    {
        var file = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    public GraphNode[] Scan() => Scanner.Scan(Path);

    public async Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Scanner).Assembly.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start code-map.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("code-map did not exit within 30 seconds.");
        }
        return (process.ExitCode, await output, await error);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
