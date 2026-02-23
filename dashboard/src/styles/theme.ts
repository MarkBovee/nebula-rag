/// <summary>
/// Nebula RAG theme configuration with dark purple/synthwave aesthetic.
/// Uses neon accents for important metrics and interactive elements.
/// </summary>

export const nebulaTheme = {
  // Primary colors - deep space purples and teals
  colors: {
    background: '#0a0e27',
    surface: 'rgba(26, 0, 51, 0.6)',
    surfaceLight: 'rgba(45, 10, 80, 0.8)',
    surfaceBorder: 'rgba(100, 50, 200, 0.3)',
    
    // Text colors
    textPrimary: '#e0e0ff',
    textSecondary: '#a0a0dd',
    textMuted: '#707099',
    
    // Neon accents for metrics
    neonCyan: '#00d9ff',
    neonMagenta: '#ff00ff',
    neonPink: '#ff0080',
    neonPurple: '#c700ff',
    neonBlue: '#0080ff',
    
    // Status colors
    success: '#00ff88',
    warning: '#ffaa00',
    error: '#ff3333',
    info: '#00d9ff',
    
    // Chart colors - vibrant synthwave palette
    chart: [
      '#00d9ff', // Cyan
      '#ff00ff', // Magenta
      '#ffaa00', // Amber
      '#00ff88', // Lime
      '#ff0080', // Pink
      '#8800ff', // Purple
      '#00d4ff', // Light Cyan
      '#ff6600', // Orange
    ]
  },

  // Typography
  typography: {
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', sans-serif",
    fontSize: {
      xs: '0.75rem',
      sm: '0.875rem',
      base: '1rem',
      lg: '1.125rem',
      xl: '1.25rem',
      '2xl': '1.5rem',
      '3xl': '1.875rem',
    },
    fontWeight: {
      normal: 400,
      medium: 500,
      semibold: 600,
      bold: 700,
    },
  },

  // Spacing system
  spacing: {
    xs: '0.25rem',
    sm: '0.5rem',
    md: '1rem',
    lg: '1.5rem',
    xl: '2rem',
    '2xl': '3rem',
    '3xl': '4rem',
  },

  // Border radius for modern look
  borderRadius: {
    sm: '0.25rem',
    base: '0.5rem',
    md: '0.75rem',
    lg: '1rem',
    xl: '1.5rem',
  },

  // Shadows with neon glow
  shadow: {
    sm: '0 2px 4px rgba(0, 0, 0, 0.3)',
    base: '0 4px 12px rgba(0, 0, 0, 0.4)',
    lg: '0 8px 24px rgba(0, 0, 0, 0.5)',
    glow: {
      cyan: '0 0 20px rgba(0, 217, 255, 0.3)',
      magenta: '0 0 20px rgba(255, 0, 255, 0.3)',
      purple: '0 0 20px rgba(199, 0, 255, 0.3)',
    },
  },

  // Transitions
  transition: {
    fast: '150ms cubic-bezier(0.4, 0, 0.2, 1)',
    base: '250ms cubic-bezier(0.4, 0, 0.2, 1)',
    slow: '350ms cubic-bezier(0.4, 0, 0.2, 1)',
  },
};

/// <summary>
/// Recharts color scheme matching the nebula theme.
/// Uses the vibrant synthwave palette for data visualizations.
/// </summary>
export const chartTheme = {
  colors: nebulaTheme.colors.chart,
  backgroundColor: nebulaTheme.colors.background,
  textColor: nebulaTheme.colors.textPrimary,
  grid: {
    stroke: 'rgba(100, 50, 200, 0.2)',
  },
  axis: {
    stroke: 'rgba(100, 50, 200, 0.3)',
    tick: {
      fill: nebulaTheme.colors.textMuted,
    },
  },
};

/// <summary>
/// CSS utility for applying the nebula background gradient.
/// </summary>
export const getBackgroundGradient = () => `
  linear-gradient(135deg, ${nebulaTheme.colors.background} 0%, 
    #1a0033 50%, #0f001a 100%)
`;
