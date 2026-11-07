import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ApiError } from './api/client';
import { router } from './app/router';
import { SessionProvider } from './app/session';
import { StepUpDialog } from './app/step-up';
import { ToastProvider } from './components/toast';
import './styles.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5_000,
      refetchOnWindowFocus: true,
      // Don't hammer the API on client errors (401/403/404…); retry transient failures once.
      retry: (count, err) => !(err instanceof ApiError && err.status < 500) && count < 1,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <SessionProvider>
          <RouterProvider router={router} />
          <StepUpDialog />
        </SessionProvider>
      </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
);
