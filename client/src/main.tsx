import { StrictMode } from 'react';
import ReactDOM from 'react-dom/client';
import { RouterProvider, createRouter } from '@tanstack/react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import './main.css';

// Import the generated route tree
import { routeTree } from './routeTree.gen.ts';
import { startMockServiceWorker } from './mocks/start.ts';
import { basePath } from './lib/basePath.ts';

const queryClient = new QueryClient();

export type RouterContext = { queryClient: QueryClient };

// Create a new router instance
const router = createRouter({
  // The mount point the server wrote into index.html; '/' when there is none.
  basepath: basePath() || '/',
  context: { queryClient },
  defaultPreload: 'intent',
  routeTree,
  // Since we're using React Query, we don't want loader calls to ever be stale
  // This will ensure that the loader is always called when the route is preloaded or visited
  defaultPreloadStaleTime: 0,
  scrollRestoration: true,
});

// Register the router instance for type safety
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

// The mock worker is awaited BEFORE the first render so no request can escape before the
// interceptor is installed — otherwise the very first /api/user/me would race it. In a
// production build this resolves immediately and the branch is dropped by the bundler.
await startMockServiceWorker();

const rootElement = document.getElementById('root')!;
if (!rootElement.innerHTML) {
  const root = ReactDOM.createRoot(rootElement);
  root.render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </StrictMode>
  );
}
