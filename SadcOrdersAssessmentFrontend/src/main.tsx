import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { SWRConfig } from 'swr';
import { App } from './App';
import { ApiError } from './api/client';
import { AuthProvider } from './auth/AuthProvider';
import { initDevAuth } from './auth/devAuth';
import './styles.css';

initDevAuth();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SWRConfig
      value={{
        // Don't retry definite client errors (400/403/404); do retry network errors and 5xx.
        shouldRetryOnError: (error) => !(error instanceof ApiError) || error.isTransient,
        errorRetryCount: 3,
      }}
    >
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </SWRConfig>
  </StrictMode>,
);
