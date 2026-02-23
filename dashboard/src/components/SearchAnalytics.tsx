import React, { useState } from 'react';
import { nebulaTheme } from '@/styles/theme';
import { apiClient } from '@/api/client';

const styles = {
  card: {
    background: nebulaTheme.colors.surface,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.lg,
    padding: nebulaTheme.spacing.lg,
    boxShadow: nebulaTheme.shadow.lg,
  } as React.CSSProperties,
  title: {
    fontSize: nebulaTheme.typography.fontSize.xl,
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    marginBottom: nebulaTheme.spacing.lg,
    color: nebulaTheme.colors.neonBlue,
    textShadow: `0 0 10px ${nebulaTheme.colors.neonBlue}`,
  } as React.CSSProperties,
  searchBox: {
    display: 'flex',
    gap: nebulaTheme.spacing.md,
    marginBottom: nebulaTheme.spacing.lg,
  } as React.CSSProperties,
  input: {
    flex: 1,
    padding: nebulaTheme.spacing.md,
    background: 'rgba(0, 0, 0, 0.3)',
    border: `1px solid ${nebulaTheme.colors.neonBlue}`,
    borderRadius: nebulaTheme.borderRadius.md,
    color: nebulaTheme.colors.textPrimary,
    fontSize: nebulaTheme.typography.fontSize.base,
  } as React.CSSProperties,
  button: {
    padding: `${nebulaTheme.spacing.md} ${nebulaTheme.spacing.lg}`,
    background: 'rgba(0, 128, 255, 0.2)',
    border: `1px solid ${nebulaTheme.colors.neonBlue}`,
    color: nebulaTheme.colors.neonBlue,
    borderRadius: nebulaTheme.borderRadius.md,
    cursor: 'pointer',
    fontWeight: nebulaTheme.typography.fontWeight.semibold,
    transition: nebulaTheme.transition.base,
  } as React.CSSProperties,
  results: {
    marginTop: nebulaTheme.spacing.lg,
  } as React.CSSProperties,
  resultItem: {
    padding: nebulaTheme.spacing.md,
    background: 'rgba(50, 25, 100, 0.3)',
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.md,
    marginBottom: nebulaTheme.spacing.md,
  } as React.CSSProperties,
};

/// <summary>
/// SearchAnalytics component allows users to perform searches and see query results.
/// Displays matching documents with snippets and similarity scores.
/// </summary>
const SearchAnalytics: React.FC = () => {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim()) return;

    setLoading(true);
    try {
      const result = await apiClient.query(query, 8);
      setResults(result.results);
    } catch (error) {
      console.error('Search error:', error);
    }
    setLoading(false);
  };

  return (
    <div style={styles.card}>
      <h2 style={styles.title}>🔍 Search & Query</h2>
      
      <form onSubmit={handleSearch} style={styles.searchBox}>
        <input
          type="text"
          placeholder="Search your indexed documents..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          style={styles.input}
        />
        <button type="submit" style={styles.button} disabled={loading}>
          {loading ? '⟳ Searching...' : 'Search'}
        </button>
      </form>

      {results.length > 0 && (
        <div style={styles.results}>
          <p style={{ ...styles.title, fontSize: nebulaTheme.typography.fontSize.lg, marginBottom: nebulaTheme.spacing.lg }}>
            {results.length} results found
          </p>
          {results.map((result, idx) => (
            <div key={idx} style={styles.resultItem}>
              <div style={{ marginBottom: nebulaTheme.spacing.sm }}>
                <p style={{ color: nebulaTheme.colors.neonCyan, fontWeight: 'bold' }}>
                  📄 {result.sourcePath}
                </p>
                <p style={{ color: nebulaTheme.colors.textSecondary, fontSize: nebulaTheme.typography.fontSize.sm }}>
                  Score: {(result.score * 100).toFixed(1)}%
                </p>
              </div>
              <p style={{ fontSize: nebulaTheme.typography.fontSize.sm, color: nebulaTheme.colors.textSecondary }}>
                {result.snippet.substring(0, 200)}...
              </p>
            </div>
          ))}
        </div>
      )}

      {results.length === 0 && !loading && query && (
        <div style={styles.resultItem}>
          <p style={{ color: nebulaTheme.colors.textMuted }}>No results found for "{query}"</p>
        </div>
      )}
    </div>
  );
};

export default SearchAnalytics;
