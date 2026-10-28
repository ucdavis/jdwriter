import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import type { ProblemReport } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

// Synthetic: the shape of a report, none of a real one's content.
const report = (over: Partial<ProblemReport> = {}): ProblemReport => ({
  browser: 'Mozilla/5.0 (Macintosh) Chrome/141',
  createdAt: '2026-10-28T17:00:00Z',
  from: 'Sam Lee',
  fromEmail: 'slee@example.edu',
  id: 7,
  kind: 'Bug',
  message: 'The download button did nothing.',
  note: '',
  page: '/class/abc',
  resolved: false,
  resolvedAt: null,
  ...over,
});

describe('the reports inbox', () => {
  setupRouteTest();

  it('shows open reports with who sent them and from where, and resolves one with a note', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    const posted: unknown[] = [];
    testServer.use(
      http.get('/api/admin/reports/open-count', () => HttpResponse.json({ open: 1 })),
      http.get('/api/admin/reports', () => HttpResponse.json({ open: 1, reports: [report()] })),
      http.post('/api/admin/reports/7/status', async ({ request }) => {
        posted.push(await request.json());
        return HttpResponse.json({ id: 7, note: 'Fixed.', resolved: true, resolvedAt: '2026-10-28T18:00:00Z' });
      })
    );
    renderRoute({ initialPath: '/backend/reports' });

    // The open count is on the Back end tab, said in full to a screen reader.
    const nav = await screen.findByRole('navigation', { name: 'Back end' });
    expect(await within(nav).findByRole('link', { name: /^Reports\s*, 1 open report$/ })).toHaveAttribute('aria-current', 'page');

    const card = await screen.findByRole('article', { name: 'Report from Sam Lee' });
    expect(card).toHaveTextContent('Something’s wrong');
    expect(card).toHaveTextContent('The download button did nothing.');
    expect(card).toHaveTextContent('/class/abc');
    expect(within(card).getByRole('link', { name: 'slee@example.edu' })).toHaveAttribute(
      'href',
      'mailto:slee@example.edu?subject=Your%20JDWriter%20report'
    );

    await user.type(within(card).getByRole('textbox', { name: 'Note' }), 'Fixed.');
    await user.click(within(card).getByRole('button', { name: 'Mark resolved' }));
    await waitFor(() => expect(posted).toEqual([{ note: 'Fixed.', resolved: true }]));
  });

  it('says when nothing is open', async () => {
    useContractHandlers();
    renderRoute({ initialPath: '/backend/reports' });

    expect(await screen.findByTestId('reports-empty')).toHaveTextContent('Nothing open.');
  });
});
