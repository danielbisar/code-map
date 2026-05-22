using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace code_map;

class Program
{
    static void Main(string[] args)
    {
        var dir = args[0];

        // Pass 1: collect all declared types (full name → short name)
        var knownTypes = new Dictionary<string, string>();
        CollectTypes(dir, knownTypes);

        // Pass 2: for each type, find which other types it references
        PrintRelations(dir, knownTypes);
    }

    // ── Pass 1 ────────────────────────────────────────────────────────────────

    static void CollectTypes(string dir, Dictionary<string, string> knownTypes)
    {
        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var root = tree.GetCompilationUnitRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var shortName = typeDecl.Identifier.Text;
                var fullName = $"{GetNamespace(typeDecl)}.{shortName}".TrimStart('.');
                knownTypes.TryAdd(fullName, shortName);
            }
        }

        foreach (var sub in Directory.GetDirectories(dir))
            CollectTypes(sub, knownTypes);
    }

    // ── Pass 2 ────────────────────────────────────────────────────────────────

    static void PrintRelations(string dir, Dictionary<string, string> knownTypes)
    {
        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var root = tree.GetCompilationUnitRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var shortName = typeDecl.Identifier.Text;
                var fullName = $"{GetNamespace(typeDecl)}.{shortName}".TrimStart('.');

                var used = typeDecl.DescendantNodes()
                    .Select(node => node switch
                    {
                        IdentifierNameSyntax id => id.Identifier.Text,
                        GenericNameSyntax gn => gn.Identifier.Text,
                        _ => null
                    })
                    .Where(name => name != null && name != shortName)
                    .SelectMany(name => knownTypes
                        .Where(kv => kv.Value == name && kv.Key != fullName)
                        .Select(kv => kv.Key))
                    .ToHashSet();

                if (used.Count > 0)
                    Console.WriteLine($"{fullName} -> {string.Join(", ", used.Order())}");
            }
        }

        foreach (var sub in Directory.GetDirectories(dir))
            PrintRelations(sub, knownTypes);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string GetNamespace(SyntaxNode node)
    {
        var parts = new List<string>();
        var current = node.Parent;
        while (current != null)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
                parts.Insert(0, ns.Name.ToString());
            current = current.Parent;
        }
        return string.Join(".", parts);
    }
}
