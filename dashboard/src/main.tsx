import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import { apiClient } from '@/api/client';
import { installGlobalErrorMonitor } from '@/monitoring/errorMonitor';

installGlobalErrorMonitor(apiClient);

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
