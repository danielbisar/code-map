# code-map

A CLI tool for visualizing C# codebase dependencies. It uses Roslyn to parse `.cs` files, resolves type-level dependencies, and renders an interactive graph in the browser.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build

```sh
dotnet build
```

## Usage

### 1. Scan a directory

```sh
code-map scan <dir>
```

Recursively walks `<dir>` for `.cs` files, resolves type-level dependencies using Roslyn, prints a summary to stdout, and writes `output.json` to the current working directory.

### 2. Generate HTML visualization

```sh
code-map gen-html
```

Reads `output.json` from the current working directory and writes `output.html`. Open `output.html` in a browser to explore the dependency graph.

> Requires internet access — the HTML file loads Graphology, Sigma.js, and d3 from CDN.

### Typical workflow

```sh
cd my-project
code-map scan ./src
code-map gen-html
open output.html
```

## How it works

The scanner runs a two-pass Roslyn analysis:

1. **Collect types** — walks all `.cs` files and records every declared type with its fully-qualified name.
2. **Resolve dependencies** — for each type, resolves identifier references to known types using `using` directives, the type's own namespace, and enclosing namespaces. External/BCL types are ignored.

Partial class declarations are merged — their dependency sets are unioned into a single node.

## Graph visualization

The generated HTML provides an interactive graph powered by Sigma.js (WebGL):

- **Node size** scales with connection count (in-degree + out-degree)
- **Node color** is assigned per namespace from a 20-color qualitative palette
- **Edges** are directed arrows: A → B means A depends on B
- **Legend** lists namespaces with their colors (hidden for single-namespace projects)

### Interactivity

| Feature | Description |
|---|---|
| Pan / zoom | Built-in |
| Hover | Shows fully-qualified type name |
| Click node | Highlights 1-hop neighborhood; click again or background to deselect |
| Layout switcher | Namespace Clusters (default), Force-directed, Circular, Random |
| Separate unused | (Namespace Clusters only) Breaks out nodes with no incoming edges into a separate row |
| Animate | (Force-directed only) Runs the simulation continuously |
| Namespace filter | Dropdown to show only one namespace; clicking a legend item does the same |
| Search | Dims non-matching nodes as you type (matches full qualified name) |
| Node count | Shows `M / N nodes` when a filter is active |

## Output format

`output.json` is an array of objects — one per declared type, including isolated nodes:

```json
[
  { "from": "Namespace.ClassA", "to": ["Namespace.ClassB", "Namespace.ClassC"] },
  { "from": "Namespace.ClassD", "to": [] }
]
```

## Example output

[output.json](doc/example/Medja/output.json)
[output.html](doc/example/Medja/output.html)

![Namespace clustered view](doc/example/Medja/namespace_clusters.png)
![Force directed view](doc/example/Medja/force_directed.png)

## License

MIT
