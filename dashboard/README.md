# Nebula RAG Management Dashboard

A modern, real-time management dashboard for **Nebula RAG** built with React, TypeScript, Recharts, and Vite.

## Features

- **📊 Index Health**: Real-time statistics on documents, chunks, and index size
- **🔍 Search & Query**: Interactive search with result display and relevance scoring
- **📦 Source Breakdown**: Pie chart visualization of document distribution
- **📡 Real-time Activity**: Live feed of indexing, queries, and system events
- **🗂️ Source Management**: Table interface for managing indexed sources with delete/reindex actions
- **📈 Performance Timeline**: 24-hour charts tracking query latency, indexing rate, and CPU usage

## Nebula Theme

Features a dark, synthwave-inspired aesthetic with:
- Deep purple and teal backgrounds
- Neon cyan, magenta, and pink accent colors
- Smooth transitions and glowing effects
- Modern glassmorphism UI elements

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

### Development Workflow

1. Start the dev server: `npm run dev`
2. The proxy is configured to forward API calls to `http://localhost:8099`
3. Make changes to components in `src/components/`
4. Build optimized output: `npm run build`
5. Built files are automatically output to the AddonHost `wwwroot/dashboard` folder

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
- `POST /api/query` - Execute search query
- `POST /api/index` - Index a document source
- `POST /api/source/delete` - Delete a source
- `POST /api/purge` - Purge all indexed documents

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
- The dashboard auto-refreshes every 10 seconds (configurable in `App.tsx`)

## Future Enhancements

- [ ] Real-time WebSocket updates instead of polling
- [ ] Export analytics to CSV/JSON
- [ ] Custom time range selection for performance charts
- [ ] Advanced filtering in source manager
- [ ] Query suggestions and auto-complete
- [ ] Multi-user audit logging
