# Medja core example

These JSON, HTML, and screenshot files are **generated dependency metadata from public source**, not a copy of the Medja implementation.

| Property | Value |
|---|---|
| Source repository | https://github.com/danielbisar/Medja |
| Source revision | [`d7255e64bb3db427cdc2c2e2fcad076e241dc199`](https://github.com/danielbisar/Medja/tree/d7255e64bb3db427cdc2c2e2fcad076e241dc199) |
| Scanned directory | `src/Medja` |
| Scope | Core library only; sibling projects, tests, examples, and prototypes are not included |
| Export | 128 distinct type identities, 325 inferred references |

The exact source revision and scan command of the original example committed in `a9cde33` were not recorded and could not be established confidently. These files replace that historical export with a reproducible scan of the public revision above.

The narrower path is deliberate: a whole-`src` scan at this revision encounters a C# syntax error in `src/prototypes/ReadFileContinously/ReadFileContinously/FileCreationWatcher.cs`. No upstream files were patched to generate this example.

## Reproduce JSON and HTML

Start in the code-map repository root. Clone the source beside, not inside, this repository:

```sh
git clone https://github.com/danielbisar/Medja.git ../Medja-code-map-example
git -C ../Medja-code-map-example checkout d7255e64bb3db427cdc2c2e2fcad076e241dc199
dotnet restore code-map.slnx
dotnet build code-map.slnx --no-restore
dotnet run --no-build -- scan ../Medja-code-map-example/src/Medja
dotnet run --no-build -- gen-html
```

The generated files are in the working directory. To refresh the committed example on a shell with `cp`:

```sh
cp output.json output.html doc/example/Medja/
```

Open `output.html` locally; GitHub's file view displays source rather than running the visualization. Internet access and WebGL are required. Names and edges are heuristic results, not compiler-validated dependencies.

## Screenshots

- `namespace_clusters.png`: Namespace Clusters layout with `Medja.Controls.Control` selected, highlighting its detected 1-hop neighborhood.
- `force_directed.png`: a zoomed detail of the same graph in the nonanimated Force-directed layout, with selection cleared.

Screenshots were captured from the generated HTML at a 1440 x 960 browser viewport. Layout appearance can vary by browser; d3-force positions are not a semantic result.

## Attribution and license boundary

Medja is a separate project. Its [license at the pinned revision](https://github.com/danielbisar/Medja/blob/d7255e64bb3db427cdc2c2e2fcad076e241dc199/LICENSE.md) contains an MIT grant and these copyright notices:

- Copyright (c) 2017 by Daniel Klafft
- Copyright (c) 2018-2021 SprintWORX GmbH, author: Daniel Bişar
- Copyright (c) 2022 Daniel Bişar

The MIT notice is preserved in [LICENSE-Medja.txt](LICENSE-Medja.txt). The upstream license file also discusses Medja's dependencies; those dependencies are not bundled in this graph export.

The root code-map MIT license covers code-map's original implementation. It does not assert exclusive ownership of Medja, replace upstream notices, or grant additional rights in Medja or its dependencies. The visualization uses CDN-loaded Graphology/Sigma.js (MIT) and d3 (ISC), as described in the root README.
