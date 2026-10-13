import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import type { FitReview, Misfit } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const misfit = (over: Partial<Misfit>): Misfit => ({
  classTitle: 'Lab Ast 1',
  coveredPct: 62,
  idiosyncratic: [{ name: 'Greenhouse tours', pct: 10 }],
  reviewed: null,
  slug: '009605-lab-ast-1',
  sourceFile: 'import/aaaaaaaaaaaaaaaa',
  ucJobCode: '009605',
  ...over,
});

const review = (over: Partial<FitReview>): FitReview => ({
  at: '2026-10-13T17:00:00Z',
  by: 'An Admin',
  kind: 'belongs',
  note: null,
  rewrittenTo: null,
  ...over,
});

describe('reclassification: reviewed JDs', () => {
  setupRouteTest();

  it('offers Suggest and Rewrite until a JD is dealt with, then shows it as Reviewed, after the rest', async () => {
    testServer.use(
      http.get('/api/fit/misfits', () =>
        HttpResponse.json({
          misfits: [
            misfit({ coveredPct: 40, reviewed: review({ kind: 'rewritten', rewrittenTo: 'Lab Ast 2' }), sourceFile: 'import/rewritten00000000' }),
            misfit({ coveredPct: 50, reviewed: review({}), sourceFile: 'import/belongs0000000000' }),
            misfit({ coveredPct: 70, sourceFile: 'import/open000000000000' }),
          ],
          threshold: 90,
          totalJds: 10,
        })
      )
    );
    renderRoute({ initialPath: '/backend/fit' });

    expect(await screen.findByTestId('fit-progress')).toHaveTextContent('1 to review · 2 reviewed');
    // One JD still to do: its suggestion and rewrite, and nothing else.
    expect(screen.getAllByRole('button', { name: 'Suggest class' })).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: 'Rewrite to fit' })).toHaveLength(1);
    expect(screen.queryByRole('button', { name: /Belongs/ })).not.toBeInTheDocument();

    const reviewed = screen.getAllByTestId('fit-review');
    expect(reviewed).toHaveLength(2);
    expect(reviewed[0]).toHaveTextContent('Reviewed');
    expect(reviewed[0]).toHaveTextContent('Rewritten to fit Lab Ast 2 · An Admin');
    expect(reviewed[1]).toHaveTextContent('Belongs in this class');
    // The one still to do comes first.
    const links = screen.getAllByRole('link').map((a) => a.textContent ?? '');
    expect(links.findIndex((t) => t.includes('open'))).toBeLessThan(links.findIndex((t) => t.includes('rewritten')));
  });

  it('is marked on the JD itself with a checkbox, and unmarked the same way', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    let current: FitReview | null = null;
    const calls: string[] = [];
    testServer.use(
      http.get('/api/fit/reviews', () => HttpResponse.json(current)),
      http.post('/api/fit/reviews', async ({ request }) => {
        calls.push(`mark ${JSON.stringify(await request.json())}`);
        current = review({});
        return HttpResponse.json(current);
      }),
      http.delete('/api/fit/reviews', ({ request }) => {
        calls.push(`unmark ${new URL(request.url).searchParams.get('sourceFile')}`);
        current = null;
        return new HttpResponse(null, { status: 204 });
      })
    );
    renderRoute({ initialPath: '/backend/jd/009605-lab-ast-1/Sample%20Class/JD-001.HTML' });

    const box = await screen.findByRole('checkbox', { name: /Reviewed/ });
    expect(box).not.toBeChecked();
    await user.click(box);
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /Reviewed/ })).toBeChecked());
    expect(within(box.closest('label') as HTMLElement).getByText('— belongs in this class')).toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: /Reviewed/ }));
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /Reviewed/ })).not.toBeChecked());
    expect(calls).toEqual([
      'mark {"slug":"009605-lab-ast-1","sourceFile":"Sample Class/JD-001.HTML"}',
      'unmark Sample Class/JD-001.HTML',
    ]);
  });
});
