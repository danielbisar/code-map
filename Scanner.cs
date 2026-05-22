using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

namespace code_map;

static class Scanner
{
    public static int Run(string dir)
    {
        var knownTypes = new Dictionary<string, string>(); // fullName → shortName
        CollectTypes(dir, knownTypes);

        // One set per unique full name; partial classes share the same set
        var deps = knownTypes.Keys.ToDictionary(k => k, _ => new HashSet<string>());
        ResolveDeps(dir, knownTypes, deps);

        foreach (var (fullName, targets) in deps.OrderBy(kv => kv.Key))
        {
            if (targets.Count > 0)
                Console.WriteLine($"{fullName} -> {string.Join(", ", targets.Order())}");
        }

        var nodes = deps
            .OrderBy(kv => kv.Key)
            .Select(kv => new { from = kv.Key, to = kv.Value.Order().ToArray() });

        File.WriteAllText("output.json",
            JsonSerializer.Serialize(nodes, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    static void CollectTypes(string dir, Dictionary<string, string> knownTypes)
    {
        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();
            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var shortName = typeDecl.Identifier.Text;
                var ns = GetNamespace(typeDecl);
                var fullName = string.IsNullOrEmpty(ns) ? shortName : $"{ns}.{shortName}";
                knownTypes.TryAdd(fullName, shortName);
            }
        }
    }

    static void ResolveDeps(string dir, Dictionary<string, string> knownTypes, Dictionary<string, HashSet<string>> deps)
    {
        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();

            var fileUsings = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Where(u => u.Alias == null && !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                .Select(u => u.Name?.ToString())
                .OfType<string>()
                .ToList();

            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var shortName = typeDecl.Identifier.Text;
                var ns = GetNamespace(typeDecl);
                var fullName = string.IsNullOrEmpty(ns) ? shortName : $"{ns}.{shortName}";

                if (!deps.TryGetValue(fullName, out var toSet))
                    continue;

                // Candidate namespace prefixes: file usings, own namespace, enclosing namespaces
                var candidates = new List<string>(fileUsings);
                var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
                for (int i = parts.Length; i >= 0; i--)
                    candidates.Add(string.Join(".", parts.Take(i)));

                var identifiers = typeDecl.DescendantNodes()
                    .Select(node => node switch
                    {
                        IdentifierNameSyntax id => id.Identifier.Text,
                        GenericNameSyntax gn => gn.Identifier.Text,
                        _ => null
                    })
                    .OfType<string>()
                    .Where(name => name != shortName)
                    .Distinct();

                foreach (var ident in identifiers)
                {
                    foreach (var prefix in candidates)
                    {
                        var candidate = string.IsNullOrEmpty(prefix) ? ident : $"{prefix}.{ident}";
                        if (knownTypes.ContainsKey(candidate) && candidate != fullName)
                            toSet.Add(candidate);
                    }
                }
            }
        }
    }

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
