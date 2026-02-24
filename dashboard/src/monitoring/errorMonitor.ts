import type { NebularRagClient } from '@/api/client';
import type { ClientErrorReport } from '@/types';

let isInstalled = false;

/// <summary>
/// Installs global browser error listeners and forwards events to the backend.
/// </summary>
/// <param name="client">API client used to send telemetry.</param>
export const installGlobalErrorMonitor = (client: NebularRagClient): void => {
  if (isInstalled) {
    return;
  }

  const report = (payload: ClientErrorReport) => {
    void client.reportClientError(payload);
  };

  window.addEventListener('error', (event: ErrorEvent) => {
    report({
      message: event.message || 'Unknown runtime error',
      stack: event.error?.stack,
      source: 'window.error',
      url: window.location.href,
      userAgent: navigator.userAgent,
      severity: 'error',
      timestamp: new Date().toISOString()
    });
  });

  window.addEventListener('unhandledrejection', (event: PromiseRejectionEvent) => {
    const reason = event.reason;
    const message = typeof reason === 'string'
      ? reason
      : reason?.message ?? 'Unhandled promise rejection';
    const stack = reason?.stack;

    report({
      message,
      stack,
      source: 'unhandledrejection',
      url: window.location.href,
      userAgent: navigator.userAgent,
      severity: 'error',
      timestamp: new Date().toISOString()
    });
  });

  isInstalled = true;
};
