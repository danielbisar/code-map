using System.Text.Json;

namespace code_map.Tests;

public class CliTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("""[{"from":" ","to":[]}]""")]
    [InlineData("""[{"from":"A","to":null}]""")]
    [InlineData("""[{"from":"A","to":[""]}]""")]
    [InlineData("""[{"from":"A","to":[null]}]""")]
    [InlineData("""[{"from":"A","to":[]},{"from":"A","to":[]}]""")]
    [InlineData("""[{"from":"A","to":["A"]}]""")]
    [InlineData("""[{"from":"A","to":["B"]}]""")]
    [InlineData("""[{"from":"A","to":["B","B"]},{"from":"B","to":[]}]""")]
    public void RejectsMalformedGraphData(string json) =>
        Assert.Throws<InvalidDataException>(() => HtmlGenerator.ReadGraph(json));

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""[{"from":"A","to":1}]""")]
    public void RejectsInvalidJsonOrPropertyTypes(string json) =>
        Assert.Throws<JsonException>(() => HtmlGenerator.ReadGraph(json));

    [Fact]
    public void AcceptsEmptyGraphsAndCaseInsensitivePropertyNames()
    {
        Assert.Empty(HtmlGenerator.ReadGraph("[]"));
        var node = Assert.Single(HtmlGenerator.ReadGraph("""[{"From":"A","To":[]}]"""));
        Assert.Equal("A", node.From);
        Assert.Empty(node.To);
    }

    [Fact]
    public async Task ScanAndGenerateHtmlWorkAsAProcess()
    {
        using var dir = new TestDirectory(("Types.cs", "class Consumer { Target value; } class Target {}"));
        var scan = await dir.Run("scan", ".");
        Assert.Equal(0, scan.ExitCode);
        Assert.Empty(scan.Error);
        Assert.Contains("Consumer -> Target", scan.Output);
        Assert.Equal(2, HtmlGenerator.ReadGraph(File.ReadAllText(System.IO.Path.Combine(dir.Path, "output.json"))).Length);
        var html = await dir.Run("gen-html");
        Assert.Equal(0, html.ExitCode);
        Assert.Empty(html.Error);
        var text = File.ReadAllText(System.IO.Path.Combine(dir.Path, "output.html"));
        Assert.Contains("const nodesData = [", text);
        Assert.Contains("d3@7.9.0", text);
        Assert.Contains("Separate zero-incoming", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("scan")]
    [InlineData("scan . extra")]
    [InlineData("gen-html extra")]
    [InlineData("unknown")]
    public async Task InvalidArgumentsPrintUsage(string arguments)
    {
        using var dir = new TestDirectory();
        var result = await dir.Run(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage:", result.Error);
    }

    [Fact]
    public async Task MissingDirectoryPrintsActionableError()
    {
        using var dir = new TestDirectory();
        var result = await dir.Run("scan", "missing");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error: Input directory not found:", result.Error);
        Assert.False(File.Exists(System.IO.Path.Combine(dir.Path, "output.json")));
    }

    [Fact]
    public async Task MissingJsonPrintsActionableError()
    {
        using var dir = new TestDirectory();
        var result = await dir.Run("gen-html");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("output.json not found", result.Error);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""[{"from":"A","to":["missing"]}]""")]
    public async Task InvalidInputDoesNotOverwriteExistingHtml(string json)
    {
        using var dir = new TestDirectory(("output.json", json), ("output.html", "keep"));
        var result = await dir.Run("gen-html");
        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("error:", result.Error);
        Assert.Equal("keep", File.ReadAllText(System.IO.Path.Combine(dir.Path, "output.html")));
    }

    [Fact]
    public async Task InvalidSourceDoesNotOverwriteExistingGraph()
    {
        using var dir = new TestDirectory(("Bad.cs", "class Broken {"), ("output.json", "keep"));
        var result = await dir.Run("scan", ".");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Invalid C# syntax:", result.Error);
        Assert.Equal("keep", File.ReadAllText(System.IO.Path.Combine(dir.Path, "output.json")));
    }

    [Theory]
    [InlineData("scan", "output.json")]
    [InlineData("gen-html", "output.html")]
    public async Task FilesystemWriteFailuresAreReported(string command, string blockedOutput)
    {
        using var dir = new TestDirectory(("Types.cs", "class Example {}"), ("output.json", "[]"));
        if (File.Exists(System.IO.Path.Combine(dir.Path, blockedOutput)))
            File.Delete(System.IO.Path.Combine(dir.Path, blockedOutput));
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, blockedOutput));
        var result = command == "scan" ? await dir.Run("scan", ".") : await dir.Run("gen-html");
        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("error:", result.Error);
        Assert.DoesNotContain("Unhandled exception", result.Error);
    }

    [Fact]
    public async Task NodeNamesCannotBeReplacedAsTemplatePlaceholders()
    {
        using var dir = new TestDirectory(("output.json", """[{"from":"__EDGES__","to":[]}]"""));
        Assert.Equal(0, (await dir.Run("gen-html")).ExitCode);
        Assert.Contains("\"id\":\"__EDGES__\"", File.ReadAllText(System.IO.Path.Combine(dir.Path, "output.html")));
    }
}
