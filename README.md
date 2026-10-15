# code-map

[![CI](https://github.com/danielbisar/code-map/actions/workflows/ci.yml/badge.svg)](https://github.com/danielbisar/code-map/actions/workflows/ci.yml)

Experimental C# dependency explorer that combines **Roslyn syntax parsing** with an **interactive WebGL graph**. Built for codebase reconnaissance: exploring namespace structure, local type references, and highly connected types.

**Dependency detection is heuristic and syntax-and-name based, not compiler-semantic symbol resolution.** Use the graph to decide where to investigate, not to prove architectural correctness, runtime reachability, or dead code.

I built the source scanner, deterministic JSON graph export, namespace-clustered layout, and browser interaction layer. Roslyn supplies the parser; Graphology, Sigma.js, and d3 supply the graph container, WebGL renderer, and force simulation. The implementation was developed with AI assistance, recorded in the commit history.

![Medja core dependency graph with Control and its detected neighbors highlighted](doc/example/Medja/namespace_clusters.png)

*Which types reference `Control`? Selecting it highlights its detected 1-hop neighborhood. This scan of Medja's public core library contains 128 types and 325 inferred references; it is an exploration aid, not a semantic coupling measurement. [Example source, revision, and attribution](doc/example/Medja/README.md).*

**Status:** small experimental tool, with regression tests for the supported scanner contract and CLI. It does not load solutions/projects or use `SemanticModel`. The production implementation remains three C# files.

## Why it exists

When entering an unfamiliar codebase, a list of files is not always enough to see its shape. code-map provides a lightweight first pass: scan a source directory, inspect namespace groups and connection counts, then search or select a type to explore its immediate references. No build of the target codebase is required.

## Requirements and quick start

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A browser with WebGL and internet access for the visualization libraries

From a fresh clone:

```sh
git clone https://github.com/danielbisar/code-map.git
cd code-map
dotnet restore code-map.slnx
dotnet build code-map.slnx --no-restore
dotnet test code-map.slnx --no-build
dotnet run --no-build -- scan ./doc/example/Synthetic
dotnet run --no-build -- gen-html
```

Open the generated `output.html` in your browser. On Linux, `xdg-open output.html`; on macOS, `open output.html`; on Windows PowerShell, `Start-Process output.html`.

The [synthetic input](doc/example/Synthetic/Types.cs) is a small, owned illustration of a scanning/export pipeline, including an isolated type. It does not implement a real scanner.

## Usage

### 1. Scan a directory

From the code-map repository directory:

```sh
dotnet run --no-build -- scan /path/to/project/src
```

Recursively scans `.cs` files, prints inferred dependency lines to stdout, and writes `output.json` in the **current working directory**. `bin`, `obj`, `.git`, and symlinked subdirectories are skipped. Other source, including tests or generated `.cs` files outside those directories, is included.

### 2. Generate HTML

```sh
dotnet run --no-build -- gen-html
```

Reads `output.json` and writes `output.html` in the current working directory. There is no globally installed `code-map` command.

To run from another working directory, use `dotnet run --project /path/to/code-map/code-map.csproj --no-build -- ...`. Output filenames are fixed; **successful commands overwrite existing outputs**.

Missing directories, C# syntax errors, invalid graph JSON, and filesystem failures produce an error on stderr and exit code 1. Invalid input is rejected before replacing an existing output. Ambiguous type-name matches are omitted with a warning rather than connected to every candidate.

> Exports contain source type names and dependency metadata. Review them before sharing, especially for employer/customer code. The HTML embeds the graph and executes version-pinned JavaScript from jsDelivr; it is not offline/self-contained. No target application code is executed by the scanner.

## Analysis contract

The scanner parses each file once, then runs two passes over the syntax trees:

1. **Collect declarations:** classes, records, structs, interfaces, enums, and delegates, including nested and generic declarations.
2. **Infer references:** inspect explicit type syntax and match names against declarations in the scanned directory.

Type evidence includes base types/interfaces, field/local/parameter/property/return types, explicit object creation, casts, `typeof`, type patterns, constraints, and explicit generic arguments. Ordinary variable identifiers and member-access expressions are not treated as type evidence.

Supported name handling:

- Qualified names and `global::` references.
- Nested identities such as `Example.Outer+Inner`.
- Generic arity such as ``Example.Box`1`` and ``Example.Box`2`` (a backtick followed by the parameter count); constructed types map to their declaration.
- Enclosing type/namespace lookup, lexically scoped namespace imports, and aliases to named types or namespaces.
- Explicit `global using` directives across scanned files.
- Generic type parameters are not mistaken for same-named declared types.
- Partial declarations with the same identity union their dependencies. Self-edges and duplicate edges are excluded.
- Nodes and targets are sorted using ordinal comparison, independent of file creation order or current culture.

### Intentional limitations

- **No compiler binding.** A matching name is a candidate reference, not proof of which symbol C# would bind. Missing external/project context can still produce missing or incorrect edges.
- Static-member calls such as `Factory.Create()` and attribute names alone do not create edges. Neither do inferred types, extension-method binding, reflection, dynamic dispatch, or string-based references.
- `using static`, `extern alias`, aliases to non-name type expressions (for example tuples/arrays), and type arguments introduced only inside a constructed-type alias are not analyzed.
- No project boundaries, build configuration, target-framework selection, implicit SDK usings, source generators, or project-defined preprocessor symbols are loaded. Parsing uses Roslyn defaults.
- Identical namespace/type/arity identities across different projects are merged; scan a focused source directory when that would be misleading.
- Inherited nested-type lookup, generic substitution, and other compiler-specific lookup rules are not modeled.
- Files with syntax errors fail the scan; semantically invalid but parseable C# is not compiler-validated.
- Zero detected incoming references does **not** mean unused. Entry points, framework callbacks, external consumers, and reflection are outside this graph's reachability model.

## Graph visualization

The generated HTML uses Graphology 0.26.0, Sigma.js 2.4.0, and d3 7.9.0:

- **Node size:** scales with detected in-degree + out-degree.
- **Node color:** namespace-based, using a 20-color qualitative palette that repeats.
- **Edges:** directed arrows; A → B means the scanner inferred a reference from A to B.
- **Default layout:** namespace-clustered grid computed in C#, with each group's nodes arranged in a circle.
- **Legend:** namespace colors; hidden for a single namespace.

### Interactivity

| Feature | Behavior |
|---|---|
| Pan / zoom | Built into Sigma |
| Hover | Shows the full type identity |
| Click node | Highlights its detected 1-hop neighborhood; click again or background to deselect |
| Layout switcher | Namespace Clusters, Force-directed, Circular, Random |
| Separate zero-incoming | Clusters only: moves nodes with no detected incoming edges to a separate row; not dead-code analysis |
| Animate | Force-directed only: advances the simulation until convergence or until stopped |
| Namespace filter | Hides other namespaces and cross-namespace edges; legend items toggle the same filter |
| Search | Dims nonmatching nodes; does not hide them |
| Node count | Shows matching / total when namespace/search criteria exclude matches; otherwise total |

The d3-force layout uses a five-second synchronous compute budget when animation is off; this can block browser interaction. Animation uses a 16 ms compute budget per frame, excluding graph synchronization and rendering. These are safeguards, not performance guarantees. Circular/cluster layouts are deterministic; Random is intentionally not.

## Output format

`output.json` contains one node per distinct collected identity, including isolated nodes:

```json
[
  { "from": "Example.Consumer", "to": ["Example.Outer+Inner"] },
  { "from": "Example.Outer", "to": [] },
  { "from": "Example.Outer+Inner", "to": [] }
]
```

`gen-html` accepts an array of nodes with nonempty `from` strings and `to` arrays. It rejects null/malformed nodes, duplicate identities or targets, self-edges, and references to absent nodes. An empty array is valid.

## Examples

- [Synthetic source](doc/example/Synthetic/Types.cs): small input used by the quick start.
- [Medja provenance and reproduction](doc/example/Medja/README.md): a pinned public-source snapshot, with [JSON](doc/example/Medja/output.json) and [HTML](doc/example/Medja/output.html). GitHub shows the HTML source; download/open it locally to interact.

![Zoomed force-directed detail of the same Medja core graph](doc/example/Medja/force_directed.png)

## Development

```sh
dotnet restore code-map.slnx
dotnet build code-map.slnx --configuration Release --no-restore
dotnet test code-map.slnx --configuration Release --no-build
git diff --check
```

The xUnit suite uses small synthetic source fixtures and graph assertions, plus isolated CLI-process tests for output and failure behavior. The Medja graph is a demonstration, not a correctness oracle. GitHub Actions runs the same Release restore/build/test sequence with .NET 10.

Architecture and implementation details: [doc/spec.md](doc/spec.md).

## License and third-party attribution

The code-map implementation and original synthetic examples are [MIT licensed](LICENSE), copyright Daniel Bisar. This does not assert exclusive ownership of Medja or relicense third-party libraries.

| Component | Role | License |
|---|---|---|
| [Roslyn](https://github.com/dotnet/roslyn) | C# syntax parser | MIT |
| [Graphology](https://github.com/graphology/graphology) | Browser graph structure | MIT |
| [Sigma.js](https://github.com/jacomyal/sigma.js) | WebGL graph rendering | MIT |
| [d3](https://github.com/d3/d3) | Force-directed simulation | ISC |
| [Medja](https://github.com/danielbisar/Medja) | Source of generated example metadata | [Upstream MIT notice and attribution](doc/example/Medja/README.md#attribution-and-license-boundary) |

Browser libraries are loaded from CDN, not vendored here. If redistributing bundled libraries, retain their respective license notices.
