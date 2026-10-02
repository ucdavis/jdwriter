import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

describe('admin analytics', () => {
  setupRouteTest();

  it('shows usage tiles, the weekly chart with a table view, and the class sections', async () => {
    renderRoute({ initialPath: '/backend/analytics' });

    await screen.findByText('JDs created per week');
    expect(screen.getByText('Active, 7 days').parentElement).toHaveTextContent('2');
    expect(screen.getByText('JDs saved').parentElement).toHaveTextContent('5');

    const chart = screen.getByRole('group', { name: 'JDs created per week, last 12 weeks' });
    expect(within(chart).getAllByRole('button')).toHaveLength(12);

    expect(screen.getByText('Most-authored classes')).toBeInTheDocument();
    expect(screen.getByText('Envelope match across classes')).toBeInTheDocument();
    expect(screen.getByText(/classes have no JDs written yet/)).toBeInTheDocument();
    expect(screen.getAllByText('Show as table').length).toBeGreaterThan(0);
  });

  it('gives every column a tooltip on keyboard focus, not just hover', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/analytics' });

    const chart = await screen.findByRole('group', { name: 'JDs created per week, last 12 weeks' });
    await user.tab();
    while (!chart.contains(document.activeElement)) {
      await user.tab();
    }

    expect(await screen.findByRole('tooltip')).toHaveTextContent(/Week of .*: \d+ JDs/);
  });
});
