using System.Text.Json;
using System.Text.RegularExpressions;

namespace code_map;

static class HtmlGenerator
{
    static readonly string[] Palette =
    [
        "#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f",
        "#edc948", "#b07aa1", "#ff9da7", "#9c755f", "#bab0ac",
        "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd",
        "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf"
    ];

    public static int Run()
    {
        if (!File.Exists("output.json"))
        {
            Console.Error.WriteLine("error: output.json not found. Run 'code-map scan <dir>' first.");
            return 1;
        }

        var nodes = ReadGraph(File.ReadAllText("output.json"));

        var inDeg = nodes.ToDictionary(n => n.From, _ => 0);
        var outDeg = nodes.ToDictionary(n => n.From, n => n.To.Length);
        foreach (var node in nodes)
            foreach (var to in node.To)
                inDeg[to]++;

        int maxDeg = nodes.Length == 0 ? 0 : nodes.Max(n => inDeg[n.From] + outDeg[n.From]);

        var nsColor = new Dictionary<string, int>();
        int colorIdx = 0;
        foreach (var node in nodes)
        {
            var ns = Namespace(node.From);
            if (!nsColor.ContainsKey(ns)) nsColor[ns] = colorIdx++ % Palette.Length;
        }

        var positions = ComputeLayout(nodes);

        var nodesList = nodes.Select(node =>
        {
            int deg = inDeg[node.From] + outDeg[node.From];
            double size = maxDeg == 0 ? 5.0 : 3.0 + (double)deg / maxDeg * 17.0;
            var (x, y) = positions[node.From];
            return new
            {
                id = node.From,
                label = ShortName(node.From),
                fullName = node.From,
                ns = Namespace(node.From),
                color = Palette[nsColor[Namespace(node.From)]],
                size = Math.Round(size, 1),
                x = Math.Round(x, 2),
                y = Math.Round(y, 2)
            };
        });

        var edgesList = nodes
            .SelectMany(node => node.To.Select(t => new { source = node.From, target = t }))
            .ToList();

        var legendItems = nsColor
            .OrderBy(kv => kv.Key)
            .Select(kv => new
            {
                ns = kv.Key,
                label = string.IsNullOrEmpty(kv.Key) ? "(global)" : kv.Key,
                color = Palette[kv.Value]
            });

        var opts = new JsonSerializerOptions { WriteIndented = false };
        string nodesJson = JsonSerializer.Serialize(nodesList, opts);
        string edgesJson = JsonSerializer.Serialize(edgesList, opts);
        string legendJson = JsonSerializer.Serialize(legendItems, opts);
        string legendDisplay = nsColor.Count > 1 ? "block" : "none";

        File.WriteAllText("output.html",
            Regex.Replace(HtmlTemplate, "__(NODES|EDGES|LEGEND|LEGEND_DISPLAY)__", match => match.Value switch
            {
                "__NODES__" => nodesJson,
                "__EDGES__" => edgesJson,
                "__LEGEND__" => legendJson,
                "__LEGEND_DISPLAY__" => legendDisplay,
                _ => throw new InvalidOperationException("Unknown template placeholder.")
            }));
        return 0;
    }

    internal static GraphNode[] ReadGraph(string json)
    {
        var input = JsonSerializer.Deserialize<GraphNode?[]>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Graph must be a JSON array, not null.");
        var nodes = new List<GraphNode>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in input)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.From) || node.To == null ||
                node.To.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Each graph node needs a nonempty 'from' and a 'to' array of nonempty strings.");
            if (!ids.Add(node.From))
                throw new InvalidDataException($"Duplicate graph node: {node.From}");
            if (node.To.Distinct(StringComparer.Ordinal).Count() != node.To.Length)
                throw new InvalidDataException($"Duplicate dependency in node: {node.From}");
            if (node.To.Contains(node.From, StringComparer.Ordinal))
                throw new InvalidDataException($"Self-reference in node: {node.From}");
            nodes.Add(node);
        }
        foreach (var node in nodes)
            foreach (var target in node.To)
                if (!ids.Contains(target))
                    throw new InvalidDataException($"Unknown dependency '{target}' in node '{node.From}'.");
        return nodes.ToArray();
    }

    // Namespace-clustered layout: each namespace occupies a grid cell, nodes
    // arranged in a circle within that cell. Zero browser-side layout work needed.
    static Dictionary<string, (double x, double y)> ComputeLayout(GraphNode[] nodes)
    {
        var positions = new Dictionary<string, (double, double)>();
        var groups = nodes.GroupBy(n => Namespace(n.From)).ToList();
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(groups.Count)));
        const double spacing = 1000.0;

        for (int gi = 0; gi < groups.Count; gi++)
        {
            var group = groups[gi].ToList();
            double cx = (gi % cols) * spacing;
            double cy = (gi / cols) * spacing;
            int n = group.Count;
            double radius = n <= 1 ? 0.0 : Math.Min(400.0, n * 20.0);

            for (int i = 0; i < n; i++)
            {
                double angle = 2.0 * Math.PI * i / Math.Max(1, n);
                positions[group[i].From] = (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
            }
        }
        return positions;
    }

    static string Namespace(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot < 0 ? "" : fullName[..dot];
    }

    static string ShortName(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot < 0 ? fullName : fullName[(dot + 1)..];
    }

    const string HtmlTemplate = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>C# Dependency Explorer</title>
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body { display: flex; flex-direction: column; height: 100vh; font-family: sans-serif; background: #1a1a2e; color: #eee; }
#toolbar { display: flex; align-items: center; gap: 12px; padding: 8px 12px; background: #16213e; border-bottom: 1px solid #0f3460; flex-shrink: 0; flex-wrap: wrap; }
#toolbar label { font-size: 13px; color: #aaa; }
#search { padding: 5px 10px; border-radius: 4px; border: 1px solid #0f3460; background: #1a1a2e; color: #eee; font-size: 13px; width: 280px; }
#search::placeholder { color: #555; }
#ns-filter, #layout-select { padding: 5px 10px; border-radius: 4px; border: 1px solid #0f3460; background: #1a1a2e; color: #eee; font-size: 13px; max-width: 320px; }
#node-count { font-size: 12px; color: #666; margin-left: auto; }
#sigma-container { flex: 1; background: #1a1a2e; }
#tooltip {
  position: fixed;
  background: rgba(0,0,0,0.85);
  color: #fff;
  padding: 5px 10px;
  border-radius: 4px;
  font-size: 12px;
  pointer-events: none;
  display: none;
  z-index: 100;
  white-space: nowrap;
}
#legend {
  position: fixed;
  top: 56px;
  right: 16px;
  background: rgba(22,33,62,0.92);
  border: 1px solid #0f3460;
  border-radius: 6px;
  padding: 10px 14px;
  font-size: 12px;
  max-height: calc(100vh - 80px);
  overflow-y: auto;
  display: __LEGEND_DISPLAY__;
  z-index: 50;
}
#legend h4 { margin-bottom: 8px; font-size: 11px; color: #aaa; text-transform: uppercase; letter-spacing: 0.06em; }
.legend-item { display: flex; align-items: center; gap: 8px; margin-bottom: 4px; cursor: pointer; }
.legend-item:hover span { color: #fff; }
.legend-swatch { width: 13px; height: 13px; border-radius: 3px; flex-shrink: 0; }
.legend-item span { color: #ddd; }
</style>
</head>
<body>
<div id="toolbar">
  <label for="layout-select">Layout</label>
  <select id="layout-select">
    <option value="clusters">Namespace Clusters</option>
    <option value="force">Force-directed</option>
    <option value="circular">Circular</option>
    <option value="random">Random</option>
  </select>
  <span id="animate-wrapper" style="display:none"><input type="checkbox" id="animate-layout"> <label for="animate-layout">Animate</label></span>
  <span id="separate-wrapper" title="No detected incoming references does not mean unused or dead code"><input type="checkbox" id="separate-unreferenced"> <label for="separate-unreferenced">Separate zero-incoming</label></span>
  <label for="ns-filter">Namespace</label>
  <select id="ns-filter"><option value="">All namespaces</option></select>
  <label for="search">Search</label>
  <input id="search" type="search" placeholder="Filter by fully qualified name…">
  <span id="node-count"></span>
</div>
<div id="sigma-container"></div>
<div id="tooltip"></div>
<div id="legend"><h4>Namespaces</h4></div>
<script src="https://cdn.jsdelivr.net/npm/graphology@0.26.0/dist/graphology.umd.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/sigma@2.4.0/build/sigma.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/d3@7.9.0/dist/d3.min.js"></script>
<script>
const nodesData = __NODES__;
const edgesData = __EDGES__;
const legendData = __LEGEND__;

// Build directed graph
const graph = new graphology.DirectedGraph();
nodesData.forEach(n => {
  graph.addNode(n.id, { x: n.x, y: n.y, size: n.size, color: n.color,
                        label: n.label, fullName: n.fullName, ns: n.ns });
});
edgesData.forEach(e => {
  graph.addEdge(e.source, e.target, { size: 1, color: '#4a5568' });
});

// Zero incoming edges describe this scan, not dead-code or runtime reachability.
const inDegree = Object.create(null);
graph.nodes().forEach(n => { inDegree[n] = 0; });
graph.forEachEdge((_, _a, _s, target) => { inDegree[target]++; });

// State
let activeNs = null;
let searchQuery = '';
let selectedNode = null;

// Legend
const legend = document.getElementById('legend');
legendData.forEach(item => {
  const div = document.createElement('div');
  div.className = 'legend-item';
  const swatch = document.createElement('div');
  swatch.className = 'legend-swatch';
  swatch.style.background = item.color;
  const lbl = document.createElement('span');
  lbl.textContent = item.label;
  div.appendChild(swatch);
  div.appendChild(lbl);
  legend.appendChild(div);
  div.addEventListener('click', () => {
    const sel = document.getElementById('ns-filter');
    const value = JSON.stringify(item.ns);
    sel.value = sel.value === value ? '' : value;
    sel.dispatchEvent(new Event('change'));
  });
});

// Namespace dropdown
const nsFilter = document.getElementById('ns-filter');
legendData.forEach(item => {
  const opt = document.createElement('option');
  opt.value = JSON.stringify(item.ns);
  opt.textContent = item.label;
  nsFilter.appendChild(opt);
});

function updateCount() {
  const total = graph.order;
  const visible = graph.nodes().filter(n => {
    const d = graph.getNodeAttributes(n);
    if (activeNs !== null && d.ns !== activeNs) return false;
    if (searchQuery && !d.fullName.toLowerCase().includes(searchQuery)) return false;
    return true;
  }).length;
  document.getElementById('node-count').textContent =
    visible === total ? `${total} nodes` : `${visible} / ${total} nodes`;
}
updateCount();

// Sigma renderer (WebGL)
const renderer = new Sigma(graph, document.getElementById('sigma-container'), {
  renderEdgeLabels: false,
  defaultEdgeType: 'arrow',
  labelColor: { color: '#ffffff' },
  labelSize: 12,
  nodeReducer: (node, data) => {
    const res = { ...data };
    if (activeNs !== null && data.ns !== activeNs) { res.hidden = true; return res; }
    if (searchQuery && !data.fullName.toLowerCase().includes(searchQuery)) {
      res.color = '#2a2a40'; res.label = '';
    }
    if (selectedNode !== null && node !== selectedNode) {
      const neighbors = graph.neighbors(selectedNode);
      if (!neighbors.includes(node)) { res.color = '#2a2a40'; res.label = ''; }
    }
    return res;
  },
  edgeReducer: (edge, data) => {
    const res = { ...data };
    const src = graph.source(edge);
    const tgt = graph.target(edge);
    if (activeNs !== null) {
      if (graph.getNodeAttribute(src, 'ns') !== activeNs ||
          graph.getNodeAttribute(tgt, 'ns') !== activeNs) {
        res.hidden = true; return res;
      }
    }
    if (selectedNode !== null && src !== selectedNode && tgt !== selectedNode) {
      res.hidden = true;
    }
    return res;
  }
});

// Tooltip
const tooltip = document.getElementById('tooltip');
renderer.on('enterNode', ({ node }) => {
  tooltip.textContent = graph.getNodeAttribute(node, 'fullName');
  tooltip.style.display = 'block';
});
renderer.on('leaveNode', () => { tooltip.style.display = 'none'; });
document.getElementById('sigma-container').addEventListener('mousemove', e => {
  tooltip.style.left = (e.clientX + 14) + 'px';
  tooltip.style.top  = (e.clientY - 10) + 'px';
});

// Click: highlight 1-hop neighbourhood
renderer.on('clickNode', ({ node }) => {
  selectedNode = selectedNode === node ? null : node;
  renderer.refresh();
});
renderer.on('clickStage', () => {
  if (selectedNode !== null) { selectedNode = null; renderer.refresh(); }
});

// Namespace filter
nsFilter.addEventListener('change', function () {
  activeNs = this.value === '' ? null : JSON.parse(this.value);
  renderer.refresh();
  updateCount();
});

// Search
document.getElementById('search').addEventListener('input', function () {
  searchQuery = this.value.toLowerCase().trim();
  renderer.refresh();
  updateCount();
});

// Layout
let forceRunning = false;
let forceAnimFrame = null;
let simulation = null;
let simNodes = null;
const animateWrapper = document.getElementById('animate-wrapper');
const animateCheckbox = document.getElementById('animate-layout');
const separateWrapper = document.getElementById('separate-wrapper');
const separateCheckbox = document.getElementById('separate-unreferenced');

function clusterLayout(nodeIds, rowOffset) {
  const groups = Object.create(null);
  nodeIds.forEach(id => {
    const ns = graph.getNodeAttribute(id, 'ns');
    if (!groups[ns]) groups[ns] = [];
    groups[ns].push(id);
  });
  const groupList = Object.values(groups);
  const cols = Math.max(1, Math.ceil(Math.sqrt(groupList.length)));
  const spacing = 1000;
  groupList.forEach((group, gi) => {
    const cx = (gi % cols) * spacing;
    const cy = (Math.floor(gi / cols) + rowOffset) * spacing;
    const n = group.length;
    const radius = n <= 1 ? 0 : Math.min(400, n * 20);
    group.forEach((id, i) => {
      const angle = 2 * Math.PI * i / Math.max(1, n);
      graph.setNodeAttribute(id, 'x', cx + radius * Math.cos(angle));
      graph.setNodeAttribute(id, 'y', cy + radius * Math.sin(angle));
    });
  });
  return Math.ceil(groupList.length / cols);
}

function applySeparatedClusters() {
  const referenced = graph.nodes().filter(n => inDegree[n] > 0);
  const zeroIncoming = graph.nodes().filter(n => inDegree[n] === 0);
  const referencedRows = clusterLayout(referenced, 0);
  clusterLayout(zeroIncoming, referencedRows + 1);
}

function buildSimulation() {
  const nodes = graph.nodes().map(n => ({
    id: n,
    x: graph.getNodeAttribute(n, 'x'),
    y: graph.getNodeAttribute(n, 'y')
  }));
  const links = graph.edges().map(e => ({
    source: graph.source(e),
    target: graph.target(e)
  }));
  simulation = d3.forceSimulation(nodes)
    .force('charge', d3.forceManyBody().strength(-300))
    .force('link', d3.forceLink(links).id(d => d.id).distance(150))
    .force('center', d3.forceCenter(0, 0))
    .stop();
  return nodes;
}

function syncPositions(nodes) {
  nodes.forEach(n => {
    graph.setNodeAttribute(n.id, 'x', n.x);
    graph.setNodeAttribute(n.id, 'y', n.y);
  });
}

function stopForceLayout() {
  forceRunning = false;
  if (forceAnimFrame) { cancelAnimationFrame(forceAnimFrame); forceAnimFrame = null; }
  simulation = null; simNodes = null;
}

function runForceAnimated() {
  forceRunning = true;
  simNodes = buildSimulation();
  (function step() {
    if (!forceRunning) return;
    const frameDeadline = performance.now() + 16;
    while (performance.now() < frameDeadline && simulation.alpha() > simulation.alphaMin()) {
      simulation.tick();
    }
    syncPositions(simNodes);
    renderer.refresh();
    if (simulation.alpha() > simulation.alphaMin()) {
      forceAnimFrame = requestAnimationFrame(step);
    } else {
      forceRunning = false;
    }
  })();
}

function applyLayout(name) {
  stopForceLayout();
  animateWrapper.style.display = 'none';
  separateWrapper.style.display = 'none';
  if (name === 'clusters') {
    separateWrapper.style.display = '';
    if (separateCheckbox.checked) {
      applySeparatedClusters();
    } else {
      nodesData.forEach(n => {
        graph.setNodeAttribute(n.id, 'x', n.x);
        graph.setNodeAttribute(n.id, 'y', n.y);
      });
    }
  } else if (name === 'circular') {
    const ns = graph.nodes(), cnt = ns.length, r = Math.max(300, cnt * 25);
    ns.forEach((n, i) => {
      const a = 2 * Math.PI * i / Math.max(1, cnt);
      graph.setNodeAttribute(n, 'x', r * Math.cos(a));
      graph.setNodeAttribute(n, 'y', r * Math.sin(a));
    });
  } else if (name === 'random') {
    const spread = Math.max(2000, graph.order * 30);
    graph.nodes().forEach(n => {
      graph.setNodeAttribute(n, 'x', (Math.random() - 0.5) * spread);
      graph.setNodeAttribute(n, 'y', (Math.random() - 0.5) * spread);
    });
  } else if (name === 'force') {
    animateWrapper.style.display = '';
    if (animateCheckbox.checked) {
      runForceAnimated(); return;
    } else {
      simNodes = buildSimulation();
      const deadline = performance.now() + 5000;
      let iters = 0;
      while (performance.now() < deadline && simulation.alpha() > simulation.alphaMin()) {
        simulation.tick(); iters++;
      }
      syncPositions(simNodes);
      if (simulation.alpha() > simulation.alphaMin())
        console.log(`[code-map] force layout timed out after ${iters} iterations (${graph.order} nodes)`);
      simulation = null; simNodes = null;
    }
  }
  renderer.refresh();
}

document.getElementById('layout-select').addEventListener('change', function () {
  applyLayout(this.value);
});

animateCheckbox.addEventListener('change', function () {
  if (document.getElementById('layout-select').value !== 'force') return;
  if (this.checked) {
    runForceAnimated();
  } else {
    stopForceLayout();
    renderer.refresh();
  }
});

separateCheckbox.addEventListener('change', function () {
  if (document.getElementById('layout-select').value !== 'clusters') return;
  if (this.checked) {
    applySeparatedClusters();
  } else {
    nodesData.forEach(n => {
      graph.setNodeAttribute(n.id, 'x', n.x);
      graph.setNodeAttribute(n.id, 'y', n.y);
    });
  }
  renderer.refresh();
});
</script>
</body>
</html>
""";
}
