using System.Text.Json;

namespace code_map;

static class HtmlGenerator
{
    record GraphNode(string From, string[] To);

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

        var nodes = JsonSerializer.Deserialize<GraphNode[]>(
            File.ReadAllText("output.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        // Drop the empty-name sentinel entry
        nodes = nodes.Where(n => !string.IsNullOrEmpty(n.From)).ToArray();

        // Compute degrees
        var inDeg = nodes.ToDictionary(n => n.From, _ => 0);
        var outDeg = nodes.ToDictionary(n => n.From, n => n.To.Length);
        foreach (var node in nodes)
            foreach (var to in node.To)
                if (inDeg.ContainsKey(to)) inDeg[to]++;

        int maxDeg = nodes.Length == 0 ? 0 :
            nodes.Max(n => inDeg.GetValueOrDefault(n.From) + outDeg.GetValueOrDefault(n.From));

        // Namespace → color index
        var nsColor = new Dictionary<string, int>();
        int idx = 0;
        foreach (var node in nodes)
        {
            var ns = Namespace(node.From);
            if (!nsColor.ContainsKey(ns))
                nsColor[ns] = idx++ % Palette.Length;
        }

        // Build Cytoscape elements JSON
        var elements = new List<object>();
        foreach (var node in nodes)
        {
            int deg = inDeg.GetValueOrDefault(node.From) + outDeg.GetValueOrDefault(node.From);
            double size = maxDeg == 0 ? 30.0 : 30.0 + (double)deg / maxDeg * 50.0;
            elements.Add(new
            {
                data = new
                {
                    id = node.From,
                    label = ShortName(node.From),
                    fullName = node.From,
                    ns = Namespace(node.From),
                    color = Palette[nsColor[Namespace(node.From)]],
                    size = Math.Round(size, 1)
                }
            });
            foreach (var to in node.To)
                elements.Add(new { data = new { source = node.From, target = to } });
        }

        // Build legend JSON
        var legendItems = nsColor
            .OrderBy(kv => kv.Key)
            .Select(kv => new
            {
                ns = kv.Key,
                label = string.IsNullOrEmpty(kv.Key) ? "(global)" : kv.Key,
                color = Palette[kv.Value]
            });

        var opts = new JsonSerializerOptions { WriteIndented = false };
        string elementsJson = JsonSerializer.Serialize(elements, opts);
        string legendJson = JsonSerializer.Serialize(legendItems, opts);
        string legendDisplay = nsColor.Count > 1 ? "block" : "none";

        File.WriteAllText("output.html",
            HtmlTemplate
                .Replace("__ELEMENTS__", elementsJson)
                .Replace("__LEGEND__", legendJson)
                .Replace("__LEGEND_DISPLAY__", legendDisplay));
        return 0;
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
<title>Class Dependency Map</title>
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body { display: flex; flex-direction: column; height: 100vh; font-family: sans-serif; background: #1a1a2e; color: #eee; }
#toolbar { display: flex; align-items: center; gap: 12px; padding: 8px 12px; background: #16213e; border-bottom: 1px solid #0f3460; flex-shrink: 0; flex-wrap: wrap; }
#toolbar label { font-size: 13px; color: #aaa; }
#search { padding: 5px 10px; border-radius: 4px; border: 1px solid #0f3460; background: #1a1a2e; color: #eee; font-size: 13px; width: 280px; }
#search::placeholder { color: #555; }
#ns-filter { padding: 5px 10px; border-radius: 4px; border: 1px solid #0f3460; background: #1a1a2e; color: #eee; font-size: 13px; max-width: 320px; }
#node-count { font-size: 12px; color: #666; margin-left: auto; }
#cy { flex: 1; }
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
  <label for="ns-filter">Namespace</label>
  <select id="ns-filter"><option value="">All namespaces</option></select>
  <label for="search">Search</label>
  <input id="search" type="search" placeholder="Filter by fully qualified name…">
  <span id="node-count"></span>
</div>
<div id="cy"></div>
<div id="tooltip"></div>
<div id="legend"><h4>Namespaces</h4></div>
<script src="https://cdnjs.cloudflare.com/ajax/libs/cytoscape/3.30.2/cytoscape.min.js"></script>
<script>
const elements = __ELEMENTS__;
const legendData = __LEGEND__;

// Populate legend
const legend = document.getElementById('legend');
legendData.forEach(item => {
  const div = document.createElement('div');
  div.className = 'legend-item';
  div.dataset.ns = item.ns;
  const swatch = document.createElement('div');
  swatch.className = 'legend-swatch';
  swatch.style.background = item.color;
  const label = document.createElement('span');
  label.textContent = item.label;
  div.appendChild(swatch);
  div.appendChild(label);
  legend.appendChild(div);
  div.addEventListener('click', () => {
    document.getElementById('ns-filter').value = item.ns;
    document.getElementById('ns-filter').dispatchEvent(new Event('change'));
  });
});

// Populate namespace dropdown
const nsFilter = document.getElementById('ns-filter');
legendData.forEach(item => {
  const opt = document.createElement('option');
  opt.value = item.ns;
  opt.textContent = item.label;
  nsFilter.appendChild(opt);
});

const layoutOpts = { name: 'cose', animate: false, padding: 50, randomize: true };

const cy = cytoscape({
  container: document.getElementById('cy'),
  elements,
  style: [
    {
      selector: 'node',
      style: {
        'label': 'data(label)',
        'width': 'data(size)',
        'height': 'data(size)',
        'background-color': 'data(color)',
        'color': '#fff',
        'text-valign': 'center',
        'text-halign': 'center',
        'font-size': '11px',
        'text-outline-width': 1,
        'text-outline-color': 'data(color)',
        'min-zoomed-font-size': 6
      }
    },
    {
      selector: 'edge',
      style: {
        'width': 1.5,
        'line-color': '#4a5568',
        'target-arrow-color': '#4a5568',
        'target-arrow-shape': 'triangle',
        'curve-style': 'bezier',
        'opacity': 0.7
      }
    },
    {
      selector: '.dimmed',
      style: { 'opacity': 0.08 }
    },
    {
      selector: '.highlighted',
      style: { 'opacity': 1 }
    },
    {
      selector: '.search-dim',
      style: { 'opacity': 0.06 }
    }
  ],
  layout: layoutOpts
});

function updateCount() {
  const visible = cy.nodes().filter(n => n.style('display') !== 'none').length;
  const total = cy.nodes().length;
  document.getElementById('node-count').textContent =
    visible === total ? `${total} nodes` : `${visible} / ${total} nodes`;
}
updateCount();

// Namespace filter
nsFilter.addEventListener('change', function () {
  const ns = this.value;
  cy.elements().removeClass('dimmed highlighted search-dim');

  if (!ns) {
    cy.elements().style('display', 'element');
    cy.layout(layoutOpts).run();
    updateCount();
    return;
  }

  cy.nodes().forEach(n => {
    n.style('display', n.data('ns') === ns ? 'element' : 'none');
  });
  cy.edges().forEach(e => {
    const src = cy.$id(e.data('source'));
    const tgt = cy.$id(e.data('target'));
    const show = src.style('display') !== 'none' && tgt.style('display') !== 'none';
    e.style('display', show ? 'element' : 'none');
  });

  // Re-layout only visible nodes
  const visible = cy.nodes().filter(n => n.style('display') !== 'none');
  visible.layout({ ...layoutOpts, animate: true }).run();
  updateCount();
});

// Tooltip
const tooltip = document.getElementById('tooltip');
cy.on('mouseover', 'node', evt => {
  tooltip.textContent = evt.target.data('fullName');
  tooltip.style.display = 'block';
});
cy.on('mousemove', 'node', evt => {
  tooltip.style.left = (evt.originalEvent.clientX + 14) + 'px';
  tooltip.style.top  = (evt.originalEvent.clientY - 10) + 'px';
});
cy.on('mouseout', 'node', () => { tooltip.style.display = 'none'; });

// Click: highlight 1-hop neighbourhood
let selected = null;
cy.on('tap', 'node', evt => {
  const node = evt.target;
  if (selected === node) {
    cy.elements().removeClass('dimmed highlighted');
    selected = null;
    return;
  }
  selected = node;
  cy.elements().removeClass('dimmed highlighted').addClass('dimmed');
  node.closedNeighborhood().removeClass('dimmed').addClass('highlighted');
});
cy.on('tap', evt => {
  if (evt.target === cy) {
    cy.elements().removeClass('dimmed highlighted');
    selected = null;
  }
});

// Search
document.getElementById('search').addEventListener('input', function () {
  const q = this.value.toLowerCase().trim();
  if (!q) {
    cy.nodes().removeClass('search-dim');
    return;
  }
  cy.nodes().forEach(node => {
    const matches = node.data('fullName').toLowerCase().includes(q);
    node.toggleClass('search-dim', !matches);
  });
});
</script>
</body>
</html>
""";
}
