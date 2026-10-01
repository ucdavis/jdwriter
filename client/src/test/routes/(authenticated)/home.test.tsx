import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import '@/test/mswUtils.ts';

describe('home', () => {
  setupRouteTest();

  it('lists ingested classes and separates titles awaiting ingestion', async () => {
    renderRoute({ initialPath: '/' });

    await waitFor(() => {
      expect(screen.getByText('Start with the role')).toBeInTheDocument();
    });

    await waitFor(() => {
      expect(screen.getAllByText(/ready$/).length).toBeGreaterThan(0);
    });
    expect(screen.getAllByText('not yet ingested').length).toBeGreaterThan(0);
  });

  it('disables classes that have no ingested profile', async () => {
    renderRoute({ initialPath: '/' });

    // Authoring against a class with no corpus would produce a JD with no standard to
    // tailor, so the row stays visible for discoverability but is not actionable.
    const seedRow = await waitFor(() =>
      screen.getByRole('button', { name: /Project Policy Analyst 4/ })
    );
    expect(seedRow).toBeDisabled();
  });
});
