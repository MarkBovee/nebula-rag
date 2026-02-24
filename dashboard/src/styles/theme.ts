export const nebulaTheme = {
  // Black Dashboard-inspired palette with Nebula accents.
  colors: {
    background: '#101622',
    backgroundAlt: '#0c1018',
    surface: 'rgba(24, 31, 45, 0.92)',
    surfaceLight: 'rgba(31, 40, 58, 0.94)',
    surfaceBorder: 'rgba(88, 101, 126, 0.32)',
    textPrimary: '#f3f5fa',
    textSecondary: '#b6bed0',
    textMuted: '#7f8ca5',
    accentPrimary: '#fd5d32',
    accentSecondary: '#22b8cf',
    accentTertiary: '#f7b731',
    success: '#2ecc71',
    warning: '#f39c12',
    error: '#e74c3c',
    info: '#22b8cf',

    // Compatibility aliases used by current components.
    neonCyan: '#22b8cf',
    neonMagenta: '#fd5d32',
    neonPink: '#f7b731',
    neonPurple: '#8e99ae',
    neonBlue: '#4dabf7',

    chart: [
      '#fd5d32',
      '#22b8cf',
      '#f7b731',
      '#2ecc71',
      '#4dabf7',
      '#d69e2e',
      '#b2becd',
      '#6c5ce7',
    ]
  },

  typography: {
    fontFamily: "'Sora', 'Rajdhani', 'Segoe UI', sans-serif",
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

  spacing: {
    xs: '0.25rem',
    sm: '0.5rem',
    md: '1rem',
    lg: '1.5rem',
    xl: '2rem',
    '2xl': '3rem',
    '3xl': '4rem',
  },

  borderRadius: {
    sm: '0.25rem',
    base: '0.5rem',
    md: '0.75rem',
    lg: '1rem',
    xl: '1.5rem',
  },

  shadow: {
    sm: '0 4px 12px rgba(2, 7, 18, 0.35)',
    base: '0 10px 24px rgba(1, 4, 12, 0.4)',
    lg: '0 18px 38px rgba(1, 4, 12, 0.5)',
    glow: {
      cyan: '0 0 18px rgba(34, 184, 207, 0.25)',
      magenta: '0 0 18px rgba(253, 93, 50, 0.25)',
      purple: '0 0 18px rgba(119, 140, 170, 0.22)',
    },
  },

  transition: {
    fast: '150ms cubic-bezier(0.4, 0, 0.2, 1)',
    base: '250ms cubic-bezier(0.4, 0, 0.2, 1)',
    slow: '350ms cubic-bezier(0.4, 0, 0.2, 1)',
  },
};

export const chartTheme = {
  colors: nebulaTheme.colors.chart,
  backgroundColor: nebulaTheme.colors.background,
  textColor: nebulaTheme.colors.textPrimary,
  grid: {
    stroke: 'rgba(116, 133, 161, 0.18)',
  },
  axis: {
    stroke: 'rgba(116, 133, 161, 0.28)',
    tick: {
      fill: nebulaTheme.colors.textMuted,
    },
  },
};

export const getBackgroundGradient = () => `
  radial-gradient(1200px 600px at 12% -10%, rgba(253, 93, 50, 0.18), transparent 60%),
  radial-gradient(900px 480px at 84% 4%, rgba(34, 184, 207, 0.18), transparent 62%),
  linear-gradient(160deg, ${nebulaTheme.colors.backgroundAlt} 0%, ${nebulaTheme.colors.background} 100%)
`;
