/**
 * Starts the MSW browser worker so `npm run dev` gives a fully clickable app before the
 * backend exists.
 *
 * Double-gated on purpose. `import.meta.env.DEV` is false in any production build, so
 * bundlers drop this branch entirely; and the flag must ALSO be set explicitly, so
 * pointing the dev server at a real backend does not silently get intercepted mocks.
 * A mocked app that looks real is worse than no app — it would make a broken endpoint
 * look like a working one.
 */
export const startMockServiceWorker = async (): Promise<void> => {
  if (!import.meta.env.DEV || import.meta.env.VITE_USE_MSW !== 'true') {
    return;
  }

  const { worker } = await import('./browser.ts');
  await worker.start({
    // The real backend serves routes this client does not mock; let them through
    // rather than failing them, so a partially-built backend can be used as it lands.
    onUnhandledRequest: 'bypass',
    quiet: false,
  });

  // eslint-disable-next-line no-console
  console.info(
    '[msw] API requests are mocked. Unset VITE_USE_MSW to talk to the real backend.'
  );
};
