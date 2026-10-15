// Synthetic input illustrating a pipeline, not a real scanner implementation.
namespace Example;

public record SourceFile(string Path);
public record DependencyGraph;

public class SourceScanner
{
    public DependencyGraph Scan(SourceFile source) => new();
}

public class HtmlWriter
{
    public string Write(DependencyGraph graph) => "<html></html>";
}

public class Explorer(SourceScanner scanner, HtmlWriter writer)
{
    public string Explore(SourceFile source) => writer.Write(scanner.Scan(source));
}

public class IsolatedExtension;
