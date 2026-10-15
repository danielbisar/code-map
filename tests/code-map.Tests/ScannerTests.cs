namespace code_map.Tests;

public class ScannerTests
{
    static void AssertGraph(GraphNode[] actual, params (string From, string[] To)[] expected)
    {
        Assert.Equal(expected.Select(n => n.From), actual.Select(n => n.From));
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i].To, actual[i].To);
    }

    [Fact]
    public void FindsSimpleDependencyAndKeepsIsolatedTypes()
    {
        using var dir = new TestDirectory(("Types.cs", """
            namespace Demo;
            class Consumer { Dependency value; }
            class Dependency {}
            class Isolated {}
            """));
        AssertGraph(dir.Scan(), ("Demo.Consumer", ["Demo.Dependency"]), ("Demo.Dependency", []), ("Demo.Isolated", []));
    }

    [Fact]
    public void QualifiedReferencesDistinguishSameShortNamesIncludingTheDeclaringName()
    {
        using var dir = new TestDirectory(("Types.cs", """
            namespace One { class Widget {} }
            namespace Two { class Widget { One.Widget a; global::One.Widget b; } }
            """));
        AssertGraph(dir.Scan(), ("One.Widget", []), ("Two.Widget", ["One.Widget"]));
    }

    [Fact]
    public void OwnNamespaceTakesPrecedenceOverImportedNames()
    {
        using var dir = new TestDirectory(("Types.cs", """
            using One;
            namespace One { class Widget {} }
            namespace Two { class Consumer { Widget value; } class Widget {} }
            """));
        AssertGraph(dir.Scan(), ("One.Widget", []), ("Two.Consumer", ["Two.Widget"]), ("Two.Widget", []));
    }

    [Fact]
    public void NestedTypesHaveDistinctIdentitiesAndDoNotLeakDependenciesToTheirContainers()
    {
        using var dir = new TestDirectory(("Types.cs", """
            namespace Demo;
            class Client { Outer.Inner value; }
            class Other { public class Inner {} }
            class Outer { public class Inner { Target value; } }
            class Target {}
            """));
        AssertGraph(dir.Scan(),
            ("Demo.Client", ["Demo.Outer+Inner"]), ("Demo.Other", []), ("Demo.Other+Inner", []),
            ("Demo.Outer", []), ("Demo.Outer+Inner", ["Demo.Target"]), ("Demo.Target", []));
    }

    [Fact]
    public void ResolvesTypesFromEnclosingTypeScope()
    {
        using var dir = new TestDirectory(("Types.cs", """
            class Outer { public class Item {} class Consumer { Item value; } }
            """));
        AssertGraph(dir.Scan(), ("Outer", []), ("Outer+Consumer", ["Outer+Item"]), ("Outer+Item", []));
    }

    [Fact]
    public void GenericArityAndNestedGenericIdentityArePreserved()
    {
        using var dir = new TestDirectory(("Types.cs", """
            class Box {}
            class Box<T> {}
            class Box<T, U> {}
            class Outer<T> { public class Inner<U> {} }
            class Target {}
            class Consumer {
                Box plain;
                Box<Target> single;
                Box<Target, Target> pair;
                Outer<Target>.Inner<Target> nested;
            }
            """));
        AssertGraph(dir.Scan(), ("Box", []), ("Box`1", []), ("Box`2", []),
            ("Consumer", ["Box", "Box`1", "Box`2", "Outer`1+Inner`1", "Target"]),
            ("Outer`1", []), ("Outer`1+Inner`1", []), ("Target", []));
    }

    [Fact]
    public void PartialDeclarationsUnionDependenciesAndExcludeSelfReferences()
    {
        using var dir = new TestDirectory(
            ("First.cs", "namespace Demo; partial class Consumer { A a; Consumer self; } class A {}"),
            ("Second.cs", "namespace Demo; partial class Consumer { B b; A again; } class B {}"));
        AssertGraph(dir.Scan(), ("Demo.A", []), ("Demo.B", []), ("Demo.Consumer", ["Demo.A", "Demo.B"]));
    }

    [Fact]
    public void SupportsNamedTypeAndNamespaceAliases()
    {
        using var dir = new TestDirectory(("Types.cs", """
            using W = Library.Widget;
            using L = global::Library;
            namespace Library { class Widget {} }
            namespace App { class Consumer { W a; L.Widget b; } }
            """));
        AssertGraph(dir.Scan(), ("App.Consumer", ["Library.Widget"]), ("Library.Widget", []));
    }

    [Fact]
    public void NamespaceUsingsDoNotEscapeTheirLexicalScope()
    {
        using var dir = new TestDirectory(("Types.cs", """
            namespace First { class Widget {} }
            namespace Second { class Widget {} }
            namespace A { using First; class Consumer { Widget value; } }
            namespace B { using Second; class Consumer { Widget value; } }
            """));
        AssertGraph(dir.Scan(), ("A.Consumer", ["First.Widget"]), ("B.Consumer", ["Second.Widget"]),
            ("First.Widget", []), ("Second.Widget", []));
    }

    [Fact]
    public void InnerAliasesShadowOuterAliases()
    {
        using var dir = new TestDirectory(("Types.cs", """
            using W = First.Widget;
            namespace First { class Widget {} }
            namespace Second { class Widget {} }
            namespace App { using W = Second.Widget; class Consumer { W value; } }
            """));
        AssertGraph(dir.Scan(), ("App.Consumer", ["Second.Widget"]), ("First.Widget", []), ("Second.Widget", []));
    }

    [Fact]
    public void UnsupportedTypeExpressionAliasesDoNotFallBackToUnrelatedTypes()
    {
        using var dir = new TestDirectory(("Types.cs", """
            using Alias = int[];
            namespace Model { class Alias {} class Consumer { Alias value; } }
            """));
        AssertGraph(dir.Scan(), ("Model.Alias", []), ("Model.Consumer", []));
    }

    [Fact]
    public void ResolvesRelativeNamespaceImports()
    {
        using var dir = new TestDirectory(("Types.cs", """
            namespace App { using Models; class Consumer { Widget value; } }
            namespace App.Models { class Widget {} }
            """));
        AssertGraph(dir.Scan(), ("App.Consumer", ["App.Models.Widget"]), ("App.Models.Widget", []));
    }

    [Fact]
    public void GlobalUsingsApplyAcrossScannedFiles()
    {
        using var dir = new TestDirectory(
            ("Imports.cs", "global using Library; global using W = Library.Widget;"),
            ("Consumer.cs", "namespace App; class Consumer { Widget a; W b; }"),
            ("Widget.cs", "namespace Library; class Widget {}"));
        AssertGraph(dir.Scan(), ("App.Consumer", ["Library.Widget"]), ("Library.Widget", []));
    }

    [Fact]
    public async Task AmbiguousImportsDoNotInventMultipleEdges()
    {
        using var dir = new TestDirectory(("Types.cs", """
            using First;
            using Second;
            namespace First { class Widget {} }
            namespace Second { class Widget {} }
            namespace App { class Consumer { Widget value; } }
            """));
        var result = await dir.Run("scan", ".");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ambiguous type reference 'Widget'; omitted.", result.Error);
        AssertGraph(HtmlGenerator.ReadGraph(File.ReadAllText(System.IO.Path.Combine(dir.Path, "output.json"))),
            ("App.Consumer", []), ("First.Widget", []), ("Second.Widget", []));
    }

    [Fact]
    public void ValueIdentifiersAndMemberAccessAreNotTypeEvidence()
    {
        using var dir = new TestDirectory(("Types.cs", """
            class Widget { public static void Run() {} }
            class Consumer {
                void M() {
                    int Widget = 1;
                    System.Console.WriteLine(Widget);
                    Widget++;
                }
                void StaticOnly() { Widget.Run(); }
            }
            """));
        AssertGraph(dir.Scan(), ("Consumer", []), ("Widget", []));
    }

    [Fact]
    public void TypeParametersDoNotMatchSameNamedDeclaredTypes()
    {
        using var dir = new TestDirectory(("Types.cs", """
            class T {}
            class U {}
            class Consumer<T> { T value; U M<U>(U input) => input; }
            """));
        AssertGraph(dir.Scan(), ("Consumer`1", []), ("T", []), ("U", []));
    }

    [Fact]
    public void FindsExplicitTypePositionsAndGenericArgumentsWithoutExternalNodes()
    {
        using var dir = new TestDirectory(("Types.cs", """
            using System.Collections.Generic;
            class Target {}
            interface ITarget {}
            class Consumer : ITarget {
                List<Target[]> values;
                (Target first, Target second) Pair(Target arg) => (arg, arg);
                object M(object value) {
                    var created = new Target();
                    var type = typeof(Target);
                    var cast = (Target)value;
                    var test = value is Target;
                    var converted = value as Target;
                    Generic<Target>();
                    return created;
                }
                void Generic<T>() {}
            }
            """));
        AssertGraph(dir.Scan(), ("Consumer", ["ITarget", "Target"]), ("ITarget", []), ("Target", []));
    }

    [Fact]
    public void IncludesRecordsStructsInterfacesEnumsAndDelegates()
    {
        using var dir = new TestDirectory(("Types.cs", """
            delegate Target Factory<T>(T input);
            interface ITarget {}
            record Item(Target Value);
            enum Kind { First }
            struct Target : ITarget {}
            """));
        AssertGraph(dir.Scan(), ("Factory`1", ["Target"]), ("ITarget", []),
            ("Item", ["Target"]), ("Kind", []), ("Target", ["ITarget"]));
    }

    [Fact]
    public void IgnoresBuildOutputsAndGitDirectories()
    {
        using var dir = new TestDirectory(("Main.cs", "class Main {}"),
            ("bin/Bad.cs", "not valid C#"), ("obj/Bad.cs", "not valid C#"), (".git/Bad.cs", "not valid C#"));
        AssertGraph(dir.Scan(), ("Main", []));
    }

    [Fact]
    public void MissingDirectoryAndInvalidSyntaxFailExplicitly()
    {
        using var dir = new TestDirectory(("Bad.cs", "class Broken {"));
        Assert.Throws<DirectoryNotFoundException>(() => Scanner.Scan(System.IO.Path.Combine(dir.Path, "missing")));
        Assert.Contains("Bad.cs", Assert.Throws<InvalidDataException>(() => dir.Scan()).Message);
    }

    [Fact]
    public async Task OutputIsOrdinalAndIndependentOfFileCreationOrder()
    {
        const string first = "namespace Demo; partial class Consumer { Zebra z; } class Zebra {}";
        const string second = "namespace Demo; partial class Consumer { apple a; } class apple {}";
        using var a = new TestDirectory(("Z.cs", first), ("A.cs", second));
        using var b = new TestDirectory(("A.cs", second), ("Z.cs", first));
        Assert.Equal(0, (await a.Run("scan", ".")).ExitCode);
        Assert.Equal(0, (await b.Run("scan", ".")).ExitCode);
        Assert.Equal(File.ReadAllBytes(System.IO.Path.Combine(a.Path, "output.json")),
            File.ReadAllBytes(System.IO.Path.Combine(b.Path, "output.json")));
        AssertGraph(a.Scan(), ("Demo.Consumer", ["Demo.Zebra", "Demo.apple"]), ("Demo.Zebra", []), ("Demo.apple", []));
    }
}
