using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

namespace code_map;

record GraphNode(string From, string[] To);

static class Scanner
{
    public static int Run(string dir)
    {
        var nodes = Scan(dir);
        foreach (var node in nodes.Where(n => n.To.Length > 0))
            Console.WriteLine($"{node.From} -> {string.Join(", ", node.To)}");

        File.WriteAllText("output.json", JsonSerializer.Serialize(nodes, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
        return 0;
    }

    internal static GraphNode[] Scan(string dir)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Input directory not found: {dir}");

        var roots = SourceFiles(dir).Order(StringComparer.Ordinal).Select(file =>
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new InvalidDataException($"Invalid C# syntax: {errors[0]}");
            return tree.GetCompilationUnitRoot();
        }).ToArray();

        var declarations = roots.SelectMany(r => r.DescendantNodes().Where(IsDeclaration)).ToArray();
        var deps = declarations.Select(TypeId).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var lookup = deps.Keys.ToLookup(id => id.Replace('+', '.'), StringComparer.Ordinal);
        var namespaces = declarations.SelectMany(d => NamespacePrefixes(GetNamespace(d)))
            .ToHashSet(StringComparer.Ordinal);
        var globalUsings = roots.SelectMany(r => r.Usings)
            .Where(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).ToArray();

        foreach (var declaration in declarations)
        {
            var from = TypeId(declaration);
            var usings = declaration.Ancestors().SelectMany(scope => scope switch
            {
                BaseNamespaceDeclarationSyntax ns => ns.Usings.AsEnumerable(),
                CompilationUnitSyntax root => root.Usings.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.None)),
                _ => []
            }).Concat(globalUsings).Where(u => !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)).ToArray();

            foreach (var name in TypeReferences(declaration).Distinct())
            {
                if (IsTypeParameter(name) || name is IdentifierNameSyntax { Identifier.Text: "var" or "dynamic" })
                    continue;

                var target = Resolve(name, declaration, usings, lookup, namespaces);
                if (target != null && target != from)
                    deps[from].Add(target);
            }
        }

        return deps.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new GraphNode(kv.Key, kv.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();
    }

    static IEnumerable<string> SourceFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
            yield return file;
        foreach (var child in Directory.EnumerateDirectories(dir))
        {
            if (Path.GetFileName(child) is "bin" or "obj" or ".git" ||
                (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                continue;
            foreach (var file in SourceFiles(child))
                yield return file;
        }
    }

    static bool IsDeclaration(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;

    static string TypePart(SyntaxNode declaration) => declaration switch
    {
        TypeDeclarationSyntax t => t.Identifier.ValueText + Arity(t.TypeParameterList?.Parameters.Count ?? 0),
        EnumDeclarationSyntax e => e.Identifier.ValueText,
        DelegateDeclarationSyntax d => d.Identifier.ValueText + Arity(d.TypeParameterList?.Parameters.Count ?? 0),
        _ => throw new InvalidOperationException("Expected a type declaration.")
    };

    static string Arity(int count) => count == 0 ? "" : $"`{count}";

    static string TypeId(SyntaxNode declaration)
    {
        var nestedName = string.Join("+", declaration.AncestorsAndSelf().Where(IsDeclaration).Reverse().Select(TypePart));
        var ns = GetNamespace(declaration);
        return ns.Length == 0 ? nestedName : $"{ns}.{nestedName}";
    }

    static string GetNamespace(SyntaxNode node) => string.Join(".",
        node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => NameKey(ns.Name)));

    static IEnumerable<string> NamespacePrefixes(string ns)
    {
        yield return ns;
        while (ns.Contains('.'))
        {
            ns = ns[..ns.LastIndexOf('.')];
            yield return ns;
        }
        if (ns.Length > 0)
            yield return "";
    }

    static string Join(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";

    static string NameKey(NameSyntax name) => name switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText + Arity(generic.TypeArgumentList.Arguments.Count),
        QualifiedNameSyntax qualified => $"{NameKey(qualified.Left)}.{NameKey(qualified.Right)}",
        AliasQualifiedNameSyntax alias => $"{alias.Alias.Identifier.ValueText}::{NameKey(alias.Name)}",
        _ => throw new InvalidOperationException("Unexpected name syntax.")
    };

    static IEnumerable<NameSyntax> TypeReferences(SyntaxNode declaration)
    {
        // Only explicit type positions: ordinary value/member identifiers are not type evidence.
        foreach (var node in declaration.DescendantNodes(n => n == declaration || !IsDeclaration(n)))
        {
            TypeSyntax? type = node switch
            {
                BaseTypeSyntax n => n.Type,
                VariableDeclarationSyntax n => n.Type,
                ParameterSyntax n => n.Type,
                MethodDeclarationSyntax n => n.ReturnType,
                LocalFunctionStatementSyntax n => n.ReturnType,
                PropertyDeclarationSyntax n => n.Type,
                IndexerDeclarationSyntax n => n.Type,
                EventDeclarationSyntax n => n.Type,
                ObjectCreationExpressionSyntax n => n.Type,
                ArrayCreationExpressionSyntax n => n.Type,
                StackAllocArrayCreationExpressionSyntax n => n.Type,
                CastExpressionSyntax n => n.Type,
                TypeOfExpressionSyntax n => n.Type,
                DefaultExpressionSyntax n => n.Type,
                SizeOfExpressionSyntax n => n.Type,
                DeclarationExpressionSyntax n => n.Type,
                DeclarationPatternSyntax n => n.Type,
                TypePatternSyntax n => n.Type,
                RecursivePatternSyntax n => n.Type,
                CatchDeclarationSyntax n => n.Type,
                ConversionOperatorDeclarationSyntax n => n.Type,
                OperatorDeclarationSyntax n => n.ReturnType,
                TypeConstraintSyntax n => n.Type,
                ForEachStatementSyntax n => n.Type,
                ExplicitInterfaceSpecifierSyntax n => n.Name,
                BinaryExpressionSyntax n when n.IsKind(SyntaxKind.AsExpression) || n.IsKind(SyntaxKind.IsExpression)
                    => n.Right as TypeSyntax,
                _ => null
            };
            if (type != null)
                foreach (var name in NamesInType(type))
                    yield return name;
            if (node is TypeArgumentListSyntax arguments)
                foreach (var argument in arguments.Arguments)
                    foreach (var name in NamesInType(argument))
                        yield return name;
        }
        if (declaration is DelegateDeclarationSyntax d)
            foreach (var name in NamesInType(d.ReturnType))
                yield return name;
    }

    static IEnumerable<NameSyntax> NamesInType(TypeSyntax type) =>
        type.DescendantNodesAndSelf().OfType<NameSyntax>()
            .Where(n => n.Parent is not QualifiedNameSyntax and not AliasQualifiedNameSyntax);

    static bool IsTypeParameter(NameSyntax name) => name is IdentifierNameSyntax id &&
        name.Ancestors().SelectMany(n => n switch
        {
            TypeDeclarationSyntax t => t.TypeParameterList?.Parameters.AsEnumerable() ?? [],
            DelegateDeclarationSyntax d => d.TypeParameterList?.Parameters.AsEnumerable() ?? [],
            MethodDeclarationSyntax m => m.TypeParameterList?.Parameters.AsEnumerable() ?? [],
            LocalFunctionStatementSyntax f => f.TypeParameterList?.Parameters.AsEnumerable() ?? [],
            _ => []
        }).Any(p => p.Identifier.ValueText == id.Identifier.ValueText);

    static string? Resolve(NameSyntax name, SyntaxNode declaration, UsingDirectiveSyntax[] usings,
        ILookup<string, string> lookup, HashSet<string> namespaces)
    {
        string? Match(IEnumerable<string> keys)
        {
            var matches = keys.SelectMany(key => lookup[key]).Distinct(StringComparer.Ordinal).ToArray();
            if (matches.Length > 1)
                Console.Error.WriteLine($"warning: {name.GetLocation().GetLineSpan()}: ambiguous type reference '{name}'; omitted.");
            return matches.Length == 1 ? matches[0] : null;
        }

        var key = NameKey(name);
        if (key.StartsWith("global::", StringComparison.Ordinal))
            return Match([key["global::".Length..]]);
        if (key.Contains("::", StringComparison.Ordinal))
            return null; // extern aliases require project/reference information.

        var first = key.Split('.')[0];
        var alias = usings.FirstOrDefault(u => u.Alias?.Name.Identifier.ValueText == first);
        if (alias != null)
        {
            if (alias.Name == null)
                return null;
            var target = NameKey(alias.Name);
            var suffix = key[first.Length..];
            foreach (var candidate in ImportTargets(target, GetNamespace(alias)))
            {
                if (namespaces.Contains(candidate) || lookup.Contains(candidate))
                    return Match([candidate + suffix]);
            }
            return null;
        }

        foreach (var enclosing in declaration.AncestorsAndSelf().Where(IsDeclaration))
        {
            var candidate = Join(TypeId(enclosing).Replace('+', '.'), key);
            if (lookup.Contains(candidate))
                return Match([candidate]);
        }

        foreach (var prefix in NamespacePrefixes(GetNamespace(declaration)))
        {
            var local = Join(prefix, key);
            if (lookup.Contains(local))
                return Match([local]);

            var imported = usings.Where(u => u.Alias == null && u.Name != null && GetNamespace(u) == prefix)
                .Select(u => ImportTargets(NameKey(u.Name!), GetNamespace(u)).FirstOrDefault(namespaces.Contains))
                .OfType<string>().Select(ns => Join(ns, key)).Where(lookup.Contains).ToArray();
            if (imported.Length > 0)
                return Match(imported);
        }
        return null;
    }

    static IEnumerable<string> ImportTargets(string target, string ns) =>
        target.StartsWith("global::", StringComparison.Ordinal)
            ? [target["global::".Length..]]
            : NamespacePrefixes(ns).Select(prefix => Join(prefix, target));
}
