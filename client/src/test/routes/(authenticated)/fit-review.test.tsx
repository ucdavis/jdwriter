import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { Misfit } from '@/lib/contracts.ts';
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

describe('reclassification: a low-coverage JD that genuinely belongs', () => {
  setupRouteTest();

  it('is marked reviewed with a note, and leaves the queue for the reviewed list', async () => {
    const user = userEvent.setup();
    let sent: unknown = null;
    let reviewed = false;
    testServer.use(
      http.get('/api/fit/misfits', () =>
        HttpResponse.json({
          misfits: [
            misfit({
              reviewed: reviewed ? { at: '2026-10-13T17:00:00Z', by: 'An Admin', note: 'Specialized, but this class.' } : null,
            }),
            misfit({ coveredPct: 70, sourceFile: 'import/bbbbbbbbbbbbbbbb' }),
          ],
          threshold: 90,
          totalJds: 10,
        })
      ),
      http.post('/api/fit/reviews', async ({ request }) => {
        sent = await request.json();
        reviewed = true;
        return HttpResponse.json({ at: '2026-10-13T17:00:00Z', by: 'An Admin', note: 'Specialized, but this class.' });
      })
    );
    renderRoute({ initialPath: '/backend/fit' });

    expect(await screen.findByRole('button', { name: /To review/ })).toHaveTextContent('2');
    await user.click(screen.getAllByRole('button', { name: 'Belongs in this class' })[0]);
    await user.type(screen.getByLabelText('Why it belongs (optional)'), 'Specialized, but this class.');
    await user.click(screen.getByRole('button', { name: 'Mark reviewed' }));

    await waitFor(() => expect(screen.getByRole('button', { name: /To review/ })).toHaveTextContent('1'));
    expect(sent).toEqual({ note: 'Specialized, but this class.', slug: '009605-lab-ast-1', sourceFile: 'import/aaaaaaaaaaaaaaaa' });

    await user.click(screen.getByRole('button', { name: /Reviewed — belongs/ }));
    const note = await screen.findByTestId('fit-review');
    expect(note).toHaveTextContent('Reviewed by An Admin');
    expect(note).toHaveTextContent('Specialized, but this class.');
  });

  it('can be undone, back into the queue', async () => {
    const user = userEvent.setup();
    let undone: string | null = null;
    testServer.use(
      http.get('/api/fit/misfits', () =>
        HttpResponse.json({
          misfits: [misfit({ reviewed: undone ? null : { at: '2026-10-13T17:00:00Z', by: null, note: null } })],
          threshold: 90,
          totalJds: 10,
        })
      ),
      http.delete('/api/fit/reviews', ({ request }) => {
        undone = new URL(request.url).searchParams.get('sourceFile');
        return new HttpResponse(null, { status: 204 });
      })
    );
    renderRoute({ initialPath: '/backend/fit' });

    await user.click(await screen.findByRole('button', { name: /Reviewed — belongs/ }));
    const row = (await screen.findByTestId('fit-review')).closest('div.card, [class*="p-4"]') as HTMLElement;
    await user.click(within(row).getByRole('button', { name: 'Undo review' }));

    await waitFor(() => expect(undone).toBe('import/aaaaaaaaaaaaaaaa'));
    await user.click(screen.getByRole('button', { name: /To review/ }));
    expect(await screen.findByRole('button', { name: 'Belongs in this class' })).toBeInTheDocument();
  });
});
