# Implementation notes

code-map is a syntax-and-name-based reconnaissance tool, not a compiler-semantic analyzer. The [README analysis contract](../README.md#analysis-contract) is the public behavior specification; this document describes the implementation choices.

## Commands and failure behavior

| Command | Input | Output |
|---|---|---|
| `scan <dir>` | Recursive `.cs` source directory | Dependency lines on stdout and `output.json` |
| `gen-html` | `output.json` in the working directory | `output.html` |

Run through `dotnet run --no-build -- ...`, or pass `--project` when outside the repository. Commands accept no extra flags or path overrides. Output paths are relative to the working directory and successful commands overwrite them.

`Program.cs` dispatches exact argument shapes. Expected input, JSON, and filesystem failures print `error: ...` to stderr and return 1. Usage failures also return 1. Input validation precedes output writes; filesystem write failures are reported, but writes are not transactional.

## Code organization

- `Program.cs`: argument dispatch and user-facing error boundary.
- `Scanner.cs`: source traversal, parsing, declaration identities, reference inference, shared `GraphNode` record, and JSON serialization.
- `HtmlGenerator.cs`: graph validation, degree/color/layout computation, and the embedded HTML/JavaScript template.
- `tests/code-map.Tests`: compact xUnit source-fixture and CLI-process tests.

No Workspace, SemanticModel, project loading, or frontend framework is involved.

## Scanner

### Input boundary

Traverse `.cs` files recursively, skipping directories named `bin`, `obj`, `.git`, and symlinked subdirectories. Sort paths ordinally and parse each file once. A syntax-error diagnostic fails the scan with its source location.

Tests and generated source outside excluded directories remain part of the input. Roslyn's default parse settings apply; project-defined symbols, implicit usings, source generators, and target frameworks are not loaded.

### Declaration pass

Collect `BaseTypeDeclarationSyntax` and `DelegateDeclarationSyntax`.

Identity is `Namespace.Outer+Inner`, with a backtick and generic parameter count appended to each generic type segment. This preserves nesting and distinguishes nongeneric and differently generic declarations. Generic arguments at use sites map to declaration arity rather than creating constructed nodes.

All declarations with the same identity share a dependency set, including partial types. There is no cross-project identity boundary; identical identities from unrelated projects will merge.

### Reference pass

For each declaration, inspect explicit type positions without descending into other type declarations. This prevents a nested type's body references from being attributed to its outer type.

Type positions include base types, variable/parameter/return/property types, object/array creation, casts, type tests/patterns, constraints, explicit interface specifiers, and explicit generic arguments. Generic arguments inside otherwise unknown external container types are still inspected. Ordinary value identifiers and static-member access are not enough evidence.

Names are normalized with generic arity. Lookup uses:

1. Explicit `global::` qualification or a visible named alias.
2. Enclosing type scopes, innermost first.
3. The declaring namespace and enclosing namespaces, checking applicable imports at each level.

Namespace imports and named aliases respect lexical scope; explicit global usings apply across scanned files. Import targets can be relative to their declaration's namespace. Generic type parameters are excluded from same-named type matches.

When a lookup level has multiple known matches, emit a warning and omit that reference. Unknown targets are not nodes. This is conservative name matching, not C# symbol binding: inherited nested types, external-name conflicts, constructed-alias arguments, static imports, and other semantic lookup rules are outside the contract.

Self-edges are excluded. Hash sets deduplicate targets; node identities and target lists are serialized in ordinal order.

## Graph interchange and validation

```json
[
  { "from": "Example.Consumer", "to": ["Example.Dependency"] },
  { "from": "Example.Dependency", "to": [] },
  { "from": "Example.Isolated", "to": [] }
]
```

The original two-property JSON shape is preserved. Property names are accepted case-insensitively. Nested/generic identity strings are more precise than the original short-name format.

`gen-html` requires a non-null array, non-null nodes, nonempty identities, and non-null dependency arrays of nonempty strings. Duplicate identities/targets, self-edges, and absent target nodes are errors rather than silently dropped data. Empty arrays are valid.

Default JSON escaping is retained when embedding data. Template placeholders are substituted in one pass so an identity resembling a placeholder cannot trigger another substitution.

## HTML visualization

### Libraries

Exact CDN versions:

```text
https://cdn.jsdelivr.net/npm/graphology@0.26.0/dist/graphology.umd.min.js
https://cdn.jsdelivr.net/npm/sigma@2.4.0/build/sigma.min.js
https://cdn.jsdelivr.net/npm/d3@7.9.0/dist/d3.min.js
```

The HTML is not self-contained: it embeds the graph but executes external scripts and requires internet access and WebGL. Review source-derived names before distributing exports.

### Layout and rendering

The default layout groups nodes by namespace, using `ceil(sqrt(group count))` columns and 1000-unit cell spacing. Each group's nodes lie on a circle of radius `min(400, count * 20)`, or zero for a singleton. Positions are computed in C#.

The namespace is the prefix before the final dot in an identity; nested `+` segments remain in the type label. Colors cycle through a fixed 20-color qualitative palette. Size scales from 3 to 20 with total detected degree; an entirely edgeless graph uses size 5.

Edges are directed arrows: A to B means an inferred type reference, not proven runtime dependence.

### Interaction

- Sigma provides pan/zoom; a custom tooltip displays the full identity.
- Selecting a node highlights its immediate detected incoming/outgoing neighbors. Another click or a background click clears selection.
- Namespace filtering hides nodes and edges outside the selected namespace. The global namespace is independently selectable. Legend clicks toggle the same filter.
- Search dims nonmatches rather than hiding them.
- The count reflects matching nodes under namespace/search criteria, not every still-rendered dimmed node.
- Namespace Clusters, Circular, Random, and d3-force layouts are selectable. Random intentionally changes on each application.
- **Separate zero-incoming** is shown for clusters. It moves zero-in-degree nodes into a separate row of namespace groups; it is not unused/dead-code detection.
- **Animate** is shown for force layout. The simulation stops on convergence, when unchecked, or on a layout change.

Nonanimated force layout runs synchronously until convergence or a five-second compute deadline, logging a timeout to the browser console. The browser can be unresponsive during this work. Animated layout advances with a 16 ms compute budget per frame, excluding synchronization/rendering; one simulation tick can itself exceed that budget. No large-graph responsiveness guarantee is claimed.

## Verification and examples

GitHub Actions restores/builds the solution and runs xUnit on .NET 10. Source fixtures exercise supported identities, scopes, reference positions, deterministic JSON, and deliberate nonmatches; process tests exercise CLI behavior and failures.

The [Medja example](example/Medja/README.md) is generated from a documented public revision and limited scan path. It is presentation data, not an expected-output oracle for scanner correctness.
