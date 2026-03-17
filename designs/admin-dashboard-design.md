# Nebula RAG Admin Dashboard - Industrial/Utilitarian Design

## Design Intent

**Transform the existing dashboard into a practical admin interface focused on data management and control.**

This is NOT a product dashboard. It is a database admin panel for AI systems.

---

## Aesthetic Direction

### Primary Style: **Industrial / Utilitarian**

- Think: **PostgreSQL pgAdmin meets VS Code**
- Characterized by: **precision, information density, functional clarity**
- Avoid: decorative flourishes, gratuitous animations, "assistant-like" UX

### Color System

#### Primary Palette
```css
--neutral-50: #F1F5F3   (Near-white, high contrast)
--neutral-100: #0F172A   (Cool gray for borders)
--neutral-200: #E2E8F0   (Darker gray for text)
--neutral-300: #AAB2B2    (Darker gray for sections)
--neutral-400: #858C92   (Dark gray for disabled)
--neutral-500: #6C717F4   (Dark gray for backgrounds)
--neutral-600: #4B5563    (Muted gray for secondary text)
--neutral-700: #374151    (Medium gray for primary text)
--neutral-900: #111827    (Black for headings)

--accent-primary: #00A3E6   (Electric blue - action, active states)
--accent-danger: #DC2626   (Alert red - delete, warnings)
--accent-success: #16A34A   (Forest green - success states)
```

**Usage Rules:**
- Backgrounds: `neutral-50` for main, `neutral-100` for panels, `neutral-300` for cards
- Borders: `neutral-200` for subtle separation
- Text hierarchy: `neutral-700` → `neutral-600` → `neutral-300` → `neutral-200`
- Accents: Use sparingly - only for active states, confirmations, alerts

### Typography

```css
--font-display: 'Inter', system-ui, sans-serif
--font-scale: 14px for body, 16px for headings, 13px for small text
--line-height: 1.5 for readability
--letter-spacing: -0.01em for compact data
```

**Hierarchy:**
- H1: 24px, `neutral-900`, weight 600
- H2: 18px, `neutral-700`, weight 600
- H3: 16px, `neutral-700`, weight 500 (section headers)
- Body: 14px, `neutral-600`, regular weight
- Secondary text: 13px, `neutral-500`, regular weight

### Spacing & Layout

```css
--space-xs: 4px
--space-sm: 8px
--space-md: 12px
--space-lg: 16px
--space-xl: 24px
--space-2xl: 32px

--border-radius: 2px for consistency
--border-width: 1px for tables, 2px for cards
```

**Grid System:**
- 4px gutters between sections
- 8px gaps between cards in grids
- Table rows: 32px height for optimal scanning
- Dense information display (tables preferred over cards)

---

## Component Architecture

### 1. Navigation Structure (Sidebar)

```
Sidebar (280px fixed)
├── Header
│   ├── Logo
│   ├── Title "Admin"
│   └── Status indicator
├── Navigation Items
│   ├── Projects
│   ├── RAG Documents
│   ├── Memories
│   └── Plans
└── Footer
    ├── Version
    └── Environment
```

**Navigation Behavior:**
- Active tab: Full-height border-left accent (2px solid `accent-primary`)
- Hover: `neutral-50` background
- Icon: 16px, primary color
- Typography: `neutral-700`, medium weight

### 2. Main Content Area

#### Overview Tab (Telemetry & Statistics)

**Layout:**
```
┌─────────────────────────────────┐
│ Telemetry Status                     │
│ ┌────────────────────────────────┐  │
│ │ System Health                    │  │
│ │ [● Online] Index Size: X.X GB │  │
│ └────────────────────────────────┘  │  │
│ [● Connected] Memory Count: X.X  │  │
│ [● Indexed] Generated: X.X    │  │
└─────────────────────────────────┘  │
├─────────────────────────────────┤  │
│ Quick Stats Grid                    │  │
│ ┌────────┬────────┬────────┐  │  │
│ │ Sources │ Documents │ Memories │  │  │
│ │ 1,234  │ 4,567 │ 2,890 │  │  │
│ │ indexed  │  stored    │  │  │
│ └────────┴────────┴────────┘  │  │
└─────────────────────────────────┘  │
│                                    │  │
```

**Telemetry Indicators:**
- Online: `accent-success` dot
- Connected: `accent-success` dot
- Indexed: `accent-primary` dot

**Quick Stats Cards:**
- Dense 4x2 grid
- Large number (24px, `neutral-900`)
- Label (14px, `neutral-600`)
- Subtitle border: 1px solid `neutral-200`

#### Projects Tab (Includes Overview Metrics)

**Layout:**
```
┌─────────────────────────────────┐
│ Table Header                          │
│ [Search ▾] [Filter ▾] [+ Add]    │
├─────────────────────────────────┤  │
│ Project Table (dense, 280px rows)   │  │
│                                    │  │
│ Columns:                            │  │
│ • Name (240px)                       │  │
│ • Description (flex)                 │  │
│ • Documents (100px)                   │  │
│ • Memories (100px)                  │  │
│ • Plans (100px)                     │  │
│ • Created (160px)                   │  │
│ • Actions (120px, right-aligned)      │  │
│                                    │  │
└─────────────────────────────────┘  │
│                                    │  │
```

**Layout:**
```
┌─────────────────────────────────────────┐
│ Table Header                          │
│ [Search ▾] [Filter ▾] [+ Add]    │
├─────────────────────────────────────────┤  │
│                                    │  │
│  Project Table (dense, 280px rows)   │  │
│                                    │  │
│  Columns:                            │  │
│ • Name (240px)                       │  │
│ • Description (flex)                 │  │
│ • Documents (100px)                   │  │
│ • Memories (100px)                  │  │
│ • Plans (100px)                     │  │
│ • Created (160px)                   │  │
│ • Actions (120px, right-aligned)      │  │
│                                    │  │
│ Row actions:                         │  │
│ • [Edit] [Delete] ⋮               │  │
│                                    │  │
└─────────────────────────────────────────┘  │
│                                    │  │
│                                    │  │
└─────────────────────────────────────────┘  │
│                                    │  │
```

**Row actions:**
- Edit button: Secondary style (outline, `neutral-50` background)
- Delete button: Danger style (filled, `accent-danger` background)
- Confirmation: Always modal dialog

#### Project Detail View

**Split Layout:**
```
┌────────────────┬────────────────────┐
│ Left Panel    │ Right Panel           │
│              │                      │  │
│ Header        │ List               │  │
│ [← Back]     │ • Project Name              │  │
│              │ • Description               │  │
│              │ • Tags                     │  │
│ Metadata      │ • Created                  │  │
│              │ • Updated                  │  │
│              │                          │  │
│              │ • Document Count            │  │
│              │ • Memory Count             │  │
│              │ • Plan Count               │  │
│              │                          │  │
│              │                          │  │
│ [Edit Meta]  │ • Last Activity             │  │
│              │                          │  │
│ [Delete] ⚠   │                          │  │
└────────────────┴────────────────────┘  │
```

**Delete Behavior:**
- Delete button: Danger style, prominent position
- Warning text: "This will delete all documents, memories, and plans"
- Requires confirmation modal

#### RAG Documents Tab

**Layout:**
```
┌─────────────────────────────────────────┐
│ Toolbar                             │
│ [Search ▾] [Filter ▾]          │
├─────────────────────────────────────────┤  │
│ Document Table (dense, 200px rows)  │  │
│                                    │  │
│ Columns:                            │  │
│ • Source (250px)                     │  │
│ • Size (80px)                       │  │
│ • Chunks (60px)                     │  │
│ • Indexed (120px)                   │  │
│ • Created (160px)                   │  │
│ • Actions (100px, right-aligned)      │  │
│                                    │  │
└─────────────────────────────────────────┘  │
```

**Row actions:**
- View button: Primary style, full width
- Delete button: Danger style

#### Memory Tab

**Layout:**
```
┌─────────────────────────────────────────┐
│ Toolbar                             │
│ [Search ▾] [Filter ▾]          │
├─────────────────────────────────────────┤  │
│ Memory Table (dense, 180px rows)   │  │
│                                    │  │
│ Columns:                            │  │
│ • Content (flex)                     │  │
│   - Text snippet (300px)            │  │
│   - Type badge (60px)               │  │
│ • Tags (200px)                       │  │
│ • Importance (60px)                   │  │
│ • Created (160px)                   │  │
│ • Actions (100px, right-aligned)      │  │
│                                    │  │
│                                    │  │
└─────────────────────────────────────────┘  │
```

**Type badges:**
- Episodic: `neutral-500` background
- Semantic: `neutral-200` background
- Procedural: `neutral-300` background

**Importance:**
- Low: Normal text weight
- Medium: Bold text weight
- High: Bold text weight + star indicator

#### Plans Tab

**Layout:**
```
┌─────────────────────────────────────────┐
│ Toolbar                             │
│ [+ New Plan]                        │
├─────────────────────────────────────────┤  │
│ Plan List (dense, 160px rows)    │  │
│                                    │  │
│                                    │  │
│ Columns:                            │  │
│ • Name (250px)                      │  │
│ • Tasks (flex, max 3 shown)     │  │
│ • Status (80px, badge)              │  │
│ • Created (140px)                   │  │
│ • Updated (140px)                   │  │
│ • Actions (120px, right-aligned)      │  │
│                                    │  │
└─────────────────────────────────────────┘  │
```

**Status badges:**
- Draft: `neutral-400` background
- Active: `accent-primary` background
- Completed: `accent-success` background
- Archived: `neutral-300` background

---

## Interaction Patterns

### Confirmations

**Delete Operations:**
1. User clicks delete button
2. Modal dialog appears
3. Warning text displayed
4. User confirms → deletion executes

**Modal Structure:**
```
┌─────────────────────────────┐
│ ⚠ Warning                     │
│                             │
│ Delete [Project Name]?           │
│                             │
│ This will permanently remove:       │
│ • All RAG documents                │
│ • All memories                      │
│ • All plans and tasks               │
│                             │
│                             │
│ [Cancel] [Delete ⚠]            │
└─────────────────────────────┘
```

### Inline Editing

**Memory Editing:**
- Click memory row → Expand to full editor
- Fields: Content textarea (auto-expand), Tags (comma-separated), Importance dropdown
- Actions: [Cancel] [Save] at top

**Project Editing:**
- Click edit button → Modal dialog appears
- Fields: Name (text), Description (textarea), Tags (comma-separated)
- Actions: [Cancel] [Save]

---

## Performance Considerations

### Pagination

**Implementation:**
```css
--page-size: 50 for all tables
--load-threshold: 80% of viewport height
```

**Visual:**
```
┌─────────────────────────────┐
│ Showing 1-50 of 500    │
│ [◀ Prev] [Page 1 of 10] [▶ Next] │
└─────────────────────────────┘
```

### Virtualized Lists

**Strategy:**
- Initial render: First 100 rows, then incrementally load more
- Row virtualization: For very large datasets (>1000 rows)
- Smooth scroll performance maintained

---

## Anti-Patterns Compliance

### ❌ NOT Implemented
- No create project flow
- No create document flow
- No create memory flow
- No AI generation features
- No dashboard fluff (health stats beyond operational metrics)

### ✅ Implemented
- Tables instead of cards
- Dense row layout
- One-click actions
- Modal confirmations
- High contrast dark mode
- Fast filtering/search

---

## Technical Notes

### CSS Variables

```css
:root {
  --font-display: 'Inter', system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;

  /* Neutral Colors */
  --neutral-50: #F1F5F3;
  --neutral-100: #0F172A;
  --neutral-200: #E2E8F0;
  --neutral-300: #AAB2B2;
  --neutral-400: #858C92;
  --neutral-500: #6C717F4;
  --neutral-600: #4B5563;
  --neutral-700: #374151;
  --neutral-900: #111827;

  /* Accent Colors */
  --accent-primary: #00A3E6;
  --accent-danger: #DC2626;
  --accent-success: #16A34A;

  /* Spacing */
  --space-xs: 4px;
  --space-sm: 8px;
  --space-md: 12px;
  --space-lg: 16px;
  --space-xl: 24px;
  --space-2xl: 32px;

  /* Layout */
  --sidebar-width: 280px;
  --border-radius: 2px;
  --table-row-height: 32px;

  /* Typography */
  --font-size-body: 14px;
  --font-size-h1: 24px;
  --font-size-h2: 18px;
  --font-size-h3: 16px;
  --font-size-small: 13px;
}
```

### Accessibility

**Contrast Ratios:**
- All text: WCAG AA compliant (4.5:1+)
- Interactive elements: Enhanced focus indicators
- Color-only information: Text labels provided

**Keyboard Navigation:**
- Tab switching: Ctrl+1/2/3/4
- Table navigation: Arrow keys, Enter to view, Escape to close
- Pagination: Standard controls (Home, End, Page Up/Down)

---

## Implementation Order

1. Update existing pages with new structure
2. Create shared components (tables, modals, confirmations)
3. Implement delete cascading logic
4. Add pagination to all tables
5. Update CSS variables and theme system
