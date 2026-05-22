# Implementation Plan

## Commands

### `scan <dir>`
- Recursively walks `<dir>` for `.cs` files using Roslyn syntax trees
- Outputs dependency lines to stdout (only types that have at least one dependency)
- Always also writes `output.json` in the current working directory

### `gen-html`
- Reads `output.json` from the current working directory
- Writes `output.html` to the current working directory
- If `output.json` does not exist: prints a clear error to stderr and exits with code 1
- No flags, no path overrides

### Dispatch
`args[0]` is the subcommand (`scan` or `gen-html`). The old bare-directory invocation (`code-map <dir>`) is dropped.

---

## Code organisation

Three files:

- `Program.cs` — entry point and subcommand dispatch only
- `Scanner.cs` — Roslyn pass: type collection, dependency resolution, JSON serialisation
- `HtmlGenerator.cs` — reads `output.json`, writes `output.html`

---

## Intermediate format — `output.json`

Array of objects, one per declared type (including isolated nodes):

```json
[
  { "from": "Namespace.ClassA", "to": ["Namespace.ClassB", "Namespace.ClassC"] },
  { "from": "Namespace.ClassD", "to": [] }
]
```

All declared types are included, even those with an empty `to` list.

---

## Scanner details

### Two-pass algorithm

**Pass 1 — collect all declared types**
Walk all `.cs` files recursively. For each `BaseTypeDeclarationSyntax`, record:
- full name: `Namespace.ShortName` (key)
- short name: `ShortName` (value)

**Pass 2 — resolve dependencies**
For each type, resolve identifiers to known fully-qualified names using:
1. `using` namespace directives declared in the same file (for identifier `Foo` and `using Bar;`, check `Bar.Foo`)
2. The declaring type's own namespace
3. Enclosing namespaces of the declaring type

Match only against types in `knownTypes`; ignore all external/BCL references.

### Partial classes
Multiple `partial` declarations of the same type are merged: their `to` sets are unioned into a single node in the output.

### Self-references
A type is never added to its own `to` list.

---

## HTML Visualisation

### Library
Graphology 0.26.0 + Sigma.js 2.4.0 (WebGL renderer) + d3 7, all loaded from CDN:
```
https://cdn.jsdelivr.net/npm/graphology@0.26.0/dist/graphology.umd.min.js
https://cdn.jsdelivr.net/npm/sigma@2.4.0/build/sigma.min.js
https://cdn.jsdelivr.net/npm/d3@7/dist/d3.min.js
```
Not self-contained — requires internet to open.

### Layout
Default layout is pre-computed server-side: namespace-clustered grid (ceil(√N) columns), nodes within each namespace arranged in a circle. Additional layouts can be selected in the browser (see Interactivity below).

### Nodes
- **Label**: short class name (e.g. `ClassA`)
- **Tooltip on hover**: full qualified name (e.g. `Namespace.ClassA`) — custom CSS tooltip (not native `title`)
- **Color**: determined by full namespace (e.g. `MyApp.Services` and `MyApp.Data` get different colors); assigned from a fixed 20-color qualitative palette, cycling if more than 20 namespaces
- **Size**: scales linearly with total connection count (in-degree + out-degree); Sigma.js size units 3 (degree 0) to 20 (max degree)

### Edges
Directed arrows from the using class to the used class (A → B means A uses B).

### Legend
Fixed-position panel in the top-right corner listing each namespace with its color swatch. Hidden when there is only one namespace.

### Interactivity
- Pan and zoom (built-in)
- **Hover**: custom CSS tooltip showing full qualified name
- **Click**: highlights the node's direct 1-hop neighborhood (the node itself, its immediate outgoing dependencies, and immediate classes that use it); everything else is dimmed. Click again or click the background to deselect.
- **Layout switcher**: dropdown at the left of the toolbar with four options: _Namespace Clusters_ (default, static server-side layout), _Force-directed_ (d3-force, Barnes-Hut), _Circular_, _Random_. Switching snaps nodes instantly to the new layout.
  - When _Namespace Clusters_ is selected (the default) the **Separate unused** checkbox is visible; when checked, nodes with zero in-degree (nothing depends on them) are broken out into an extra row of namespace-clustered groups below the main grid; unchecking restores the original server-side positions. In-degree is computed browser-side from the edge data.
  - When _Force-directed_ is selected an **Animate** checkbox appears; unchecked (default) runs until convergence or a 5-second wall-clock timeout — a timeout is logged to the browser console as `[code-map] force layout timed out after N iterations (M nodes)`; checked runs continuously with a 16ms per-frame compute budget, stopping automatically on convergence or when unchecked / a different layout is selected. Uses d3-force (Barnes-Hut approximation, O(n log n) repulsion), loaded from CDN. Circular and Random are implemented inline.
- **Namespace filter**: dropdown in the toolbar listing all namespaces; selecting one shows only nodes (and edges) within that namespace, hiding all others. Clicking a legend item toggles the same filter. Clicking again or selecting "All namespaces" resets.
- **Search box**: text input at the top; dims non-matching nodes as you type (matches against full qualified name); matching nodes remain fully visible
- **Node count**: displays `N nodes` when all nodes are visible; displays `M / N nodes` when at least one node is hidden by the active namespace filter and/or search, where M counts nodes matching both active filters simultaneously.

### Isolated nodes
Always shown — a class with no dependencies is still part of the graph.
