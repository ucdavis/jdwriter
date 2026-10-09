import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { QualificationRulesSummary } from '@/lib/contracts.ts';
import { screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';

// Synthetic envelope text: envelopes are never committed.
const summary = (applied: boolean): QualificationRulesSummary => ({
  applied,
  changed: 2,
  envelopes: 10,
  equivalentAdded: 3,
  examples: [
    {
      educationAfter: ["Bachelor's degree in agronomy or equivalent experience."],
      educationBefore: ["Bachelor's degree in agronomy; master's degree preferred."],
      preferredChanged: ["Master's degree or equivalent experience."],
      slug: '000001-widget-analyst-2',
      title: 'Widget Analyst 2',
    },
  ],
  movedToPreferred: 1,
});

describe('education rules', () => {
  setupRouteTest();
  afterEach(() => vi.restoreAllMocks());

  it('previews the changes, then applies them after confirmation', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const requests: boolean[] = [];
    testServer.use(
      http.post('/api/admin/envelopes/qualification-rules', async ({ request }) => {
        const { apply } = (await request.json()) as { apply: boolean };
        requests.push(apply);
        return HttpResponse.json(summary(apply));
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.click(await screen.findByRole('button', { name: 'Preview' }));

    expect(await screen.findByText(/Would change 2 of 10 envelopes/)).toBeInTheDocument();
    expect(screen.getByTestId('qualification-examples')).toHaveTextContent(
      "Master's degree or equivalent experience."
    );

    await user.click(screen.getByRole('button', { name: 'Apply to 2' }));

    expect(await screen.findByText(/Applied to 2 of 10 envelopes/)).toBeInTheDocument();
    expect(requests).toEqual([false, true]);
  });
});
