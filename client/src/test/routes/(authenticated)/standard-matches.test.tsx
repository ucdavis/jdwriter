import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { StandardMatchReview } from '@/lib/contracts.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const review = (over: Partial<StandardMatchReview> = {}): StandardMatchReview => ({
  confirmed: [],
  unmatched: [
    {
      standardTitle: 'Systems Administration Supervisor 1',
      suggestions: [
        { active: true, score: 0.74, title: 'ADMIN SUPV 1', ucJobCode: '007374' },
        { active: false, score: 0.6, title: 'SYS ADM SUPV 1', ucJobCode: '007999' },
      ],
    },
  ],
  withoutSuggestion: 12,
  ...over,
});

/** Serves the review and records every decision posted. */
const serve = (data: StandardMatchReview) => {
  const decisions: unknown[] = [];
  testServer.use(
    http.get('/api/admin/standards/matches', () => HttpResponse.json(data)),
    http.post('/api/admin/standards/matches', async ({ request }) => {
      decisions.push(await request.json());
      return HttpResponse.json({ ok: true });
    })
  );
  return decisions;
};

describe('matching standards to job codes', () => {
  setupRouteTest();

  it('shows each unmatched standard with its suggested codes', async () => {
    serve(review());
    renderRoute({ initialPath: '/backend/standards' });

    expect(await screen.findByTestId('match-summary')).toHaveTextContent(
      '1 standard to review · 0 matched by hand · 12 with no close job code'
    );
    const list = screen.getByTestId('match-list');
    expect(within(list).getByText(/ADMIN SUPV 1/).closest('li')).toHaveTextContent('active');
    expect(within(list).getByText(/ADMIN SUPV 1/).closest('li')).toHaveTextContent('74% alike');
  });

  it('confirms the right code', async () => {
    const user = userEvent.setup();
    const decisions = serve(review());
    renderRoute({ initialPath: '/backend/standards' });

    const row = (await screen.findByText(/SYS ADM SUPV 1/)).closest('li') as HTMLElement;
    await user.click(within(row).getByRole('button', { name: 'This is the one' }));

    expect(decisions).toEqual([{ match: true, standardTitle: 'Systems Administration Supervisor 1', ucJobCode: '007999' }]);
  });

  it('rejects every suggestion with "None of these"', async () => {
    const user = userEvent.setup();
    const decisions = serve(review());
    renderRoute({ initialPath: '/backend/standards' });

    await user.click(await screen.findByRole('button', { name: 'None of these' }));

    await screen.findByRole('button', { name: 'None of these' });
    expect(decisions).toEqual([
      { match: false, standardTitle: 'Systems Administration Supervisor 1', ucJobCode: '007374' },
      { match: false, standardTitle: 'Systems Administration Supervisor 1', ucJobCode: '007999' },
    ]);
  });

  it('takes a code typed by hand', async () => {
    const user = userEvent.setup();
    const decisions = serve(review());
    renderRoute({ initialPath: '/backend/standards' });

    await user.type(await screen.findByLabelText('Job code for Systems Administration Supervisor 1'), '007000');
    await user.click(screen.getByRole('button', { name: 'Use this code' }));

    expect(decisions).toEqual([{ match: true, standardTitle: 'Systems Administration Supervisor 1', ucJobCode: '007000' }]);
  });

  it('lists matches made by hand, each with an undo', async () => {
    const user = userEvent.setup();
    serve(review({ confirmed: [{ decidedAt: '2026-10-10T00:00:00Z', standardTitle: 'Admissions Reqruitment Supervisor 2', title: 'ADMISSIONS RECRMT SUPV 2', ucJobCode: '004512' }], unmatched: [] }));
    const undone: unknown[] = [];
    testServer.use(
      http.post('/api/admin/standards/matches/undo', async ({ request }) => {
        undone.push(await request.json());
        return HttpResponse.json({ ok: true });
      })
    );
    renderRoute({ initialPath: '/backend/standards' });

    await user.click(await screen.findByText('Matched by hand (1)'));
    const confirmed = screen.getByTestId('confirmed-list');
    expect(confirmed).toHaveTextContent('Admissions Reqruitment Supervisor 2 → 004512 ADMISSIONS RECRMT SUPV 2');
    await user.click(within(confirmed).getByRole('button', { name: 'Undo' }));

    await screen.findByTestId('confirmed-list');
    expect(undone).toEqual([{ standardTitle: 'Admissions Reqruitment Supervisor 2' }]);
  });
});
