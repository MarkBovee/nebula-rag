# Nebula RAG Management Dashboard

A modern, real-time management dashboard for **Nebula RAG** built with React, TypeScript, Recharts, and Vite.

## Features

- **📊 Index Health**: Real-time statistics on documents, chunks, and index size
- **🔍 Search & Query**: Interactive search with result display and relevance scoring
- **📦 Source Breakdown**: Pie chart visualization of document distribution
- **📡 Real-time Activity**: Live feed of indexing, queries, and system events
- **🗂️ Source Management**: Table interface for managing indexed sources with delete/reindex actions
- **📈 Performance Timeline**: 24-hour charts tracking query latency, indexing rate, and CPU usage
- **🧠 Memory Insights**: Full analytics for memory growth, type/tag distribution, and recent memory sessions

## Nebula Theme

Features a Black Dashboard-inspired Nebula visual direction with:
- Deep slate/space backgrounds with warm orange and cool cyan accents
- Strong card hierarchy, dense operations layout, and responsive side navigation
- Atmospheric gradients and staggered reveal animation
- Data-first typography with high-contrast labels and metrics

## Development

### Prerequisites

- Node.js 18+
- npm or yarn

### Setup

```bash
# Install dependencies
npm install

# Start development server (runs on http://localhost:5173)
npm run dev

# Build for production (outputs to ../src/NebulaRAG.AddonHost/wwwroot/dashboard)
npm run build
```

### Local Testing Against Home Assistant API

One-command start (defaults to `http://homeassistant.local:8099` and `/nebula`):

```bash
npm run dev:ha
```

Override defaults when needed:

```bash
npm run dev:ha -- --origin http://homeassistant.local:8099 --basePath /nebula
```

Environment-variable override also works:

```bash
HA_ORIGIN=http://homeassistant.local:8099 HA_BASE_PATH=/nebula npm run dev:ha
```

Use Vite env variables so the local UI (`http://localhost:5173`) proxies to your Home Assistant-hosted Nebula API.

PowerShell example (Home Assistant with ingress base `/nebula`):

```powershell
$env:VITE_DEV_PROXY_ORIGIN = "http://homeassistant.local:8099"
$env:VITE_DEV_PROXY_BASE_PATH = "/nebula"
npm run dev
```

PowerShell example (no path base):

```powershell
$env:VITE_DEV_PROXY_ORIGIN = "http://homeassistant.local:8099"
Remove-Item Env:VITE_DEV_PROXY_BASE_PATH -ErrorAction SilentlyContinue
npm run dev
```

Optional direct API base override (bypasses origin/path inference in client):

```powershell
$env:VITE_API_BASE_URL = "http://homeassistant.local:8099/nebula"
npm run dev
```

### Development Workflow

1. Start the dev server: `npm run dev`
2. The proxy forwards API calls to `VITE_DEV_PROXY_ORIGIN` (default `http://localhost:8099`)
3. If Home Assistant uses a path base (for example `/nebula`), set `VITE_DEV_PROXY_BASE_PATH`
4. Make changes to components in `src/components/`
5. Build optimized output: `npm run build`
6. Built files are automatically output to the AddonHost `wwwroot/dashboard` folder

## Project Structure

```
dashboard/
├── src/
│   ├── components/          # React dashboard components
│   │   ├── IndexHealth.tsx
│   │   ├── SearchAnalytics.tsx
│   │   ├── SourceBreakdown.tsx
│   │   ├── ActivityFeed.tsx
│   │   ├── SourceManager.tsx
│   │   └── PerfTimeline.tsx
│   ├── styles/
│   │   └── theme.ts         # Nebula theme configuration
│   ├── api/
│   │   └── client.ts        # API client for backend calls
│   ├── types/
│   │   └── index.ts         # TypeScript type definitions
│   ├── App.tsx              # Main dashboard component
│   └── main.tsx             # React entry point
├── index.html               # HTML template
├── vite.config.ts           # Vite configuration
├── tsconfig.json            # TypeScript configuration
└── package.json             # Dependencies
```

## API Integration

The dashboard communicates with the Nebula RAG backend via REST API:

- `GET /api/health` - System health status
- `GET /api/stats` - Index statistics
- `GET /api/sources` - List indexed sources
- `GET /api/dashboard` - Aggregated dashboard payload (health, stats, sources)
- `POST /api/query` - Execute search query
- `POST /api/index` - Index a document source
- `POST /api/source/delete` - Delete a source
- `POST /api/purge` - Purge all indexed documents
- `POST /api/client-errors` - Browser runtime diagnostics

See the `src/api/client.ts` for implementation details.

## Styling & Theme

All colors, spacing, typography, and theme values are centralized in `src/styles/theme.ts`. 
Modify this file to adjust the Nebula aesthetic globally.

## Deployment

1. Run `npm run build` in the `dashboard/` directory
2. The optimized dashboard is built to `src/NebulaRAG.AddonHost/wwwroot/dashboard`
3. Deploy the entire NebulaRAG.AddonHost as usual
4. Access the dashboard at `http://<host>:8099/dashboard/`

## Browser Support

Modern browsers with ES2020 support:
- Chrome/Edge 90+
- Firefox 85+
- Safari 14+

## Performance Notes

- Recharts automatically optimizes renders for large datasets
- API calls are debounced and cached where possible
- The dashboard auto-refreshes every 30 seconds only while the tab is visible

## Visual Testing

Visual regression coverage is provided with Playwright for:
- Full dashboard overview page
- Each dashboard tab state (Overview, Search, Sources, Activity, Performance)
- Individual component cards used by the dashboard

Run visual tests:

```bash
npm run test:visual
```

Run visual tests in Playwright UI mode for fast tweak-and-rerun iteration:

```bash
npm run test:visual:ui
```

Run a focused headless loop against data/mobile assertions to catch regressions while iterating:

```bash
npm run test:visual:loop
```

Generate or refresh snapshot baselines:

```bash
npm run test:visual:update
```

Notes:
- Tests use mocked API responses to keep snapshots deterministic.
- Snapshots are stored under `dashboard/tests/visual/dashboard.visual.spec.ts-snapshots/`.

## Future Enhancements

- [ ] Real-time WebSocket updates instead of polling
- [ ] Export analytics to CSV/JSON
- [ ] Custom time range selection for performance charts
- [ ] Advanced filtering in source manager
- [ ] Query suggestions and auto-complete
- [ ] Multi-user audit logging
