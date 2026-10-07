import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { FitRewriteResponse, Misfit } from '@/lib/contracts.ts';
import { screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const misfit: Misfit = {
  classTitle: 'Financial Anl 3 Cx',
  coveredPct: 62,
  idiosyncratic: [{ name: 'Event planning', pct: 10 }],
  slug: '005183-financial-anl-3-cx',
  sourceFile: 'JD-001.html',
  ucJobCode: '005183',
};

const rewritten = (over: Partial<FitRewriteResponse>): FitRewriteResponse => ({
  authoredJdId: 12,
  carriedDuties: 3,
  droppedFunctions: 1,
  keptFunctions: 2,
  outside: [{ pct: 10, reason: 'Event planning is not financial analysis.', text: 'Event planning' }],
  slug: misfit.slug,
  title: misfit.classTitle,
  ...over,
});

describe('reclassification review: rewrite to fit', () => {
  setupRouteTest();

  it('drafts a JD for the current class and links straight to it', async () => {
    const user = userEvent.setup();
    let sent: unknown = null;
    testServer.use(
      http.get('/api/fit/misfits', () =>
        HttpResponse.json({ misfits: [misfit], threshold: 90, totalJds: 10 })
      ),
      http.post('/api/fit/rewrite', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(rewritten({}));
      })
    );
    renderRoute({ initialPath: '/backend/fit' });

    await user.click(await screen.findByRole('button', { name: 'Rewrite to fit' }));

    await screen.findByText(/Draft written for/);
    expect(sent).toEqual({ slug: misfit.slug, sourceFile: misfit.sourceFile });
    expect(screen.getByText(/2 functions kept, 3 of the incumbent's duties carried over/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open draft →' })).toHaveAttribute(
      'href',
      '/class/005183-financial-anl-3-cx?draft=12'
    );
    expect(
      screen.getByText('Event planning (10%) — Event planning is not financial analysis.')
    ).toBeInTheDocument();
  });

  it('rewrites into the better class a suggestion found', async () => {
    const user = userEvent.setup();
    let sent: { targetSlug?: string } | null = null;
    testServer.use(
      http.get('/api/fit/misfits', () =>
        HttpResponse.json({ misfits: [misfit], threshold: 90, totalJds: 10 })
      ),
      http.post('/api/fit/suggest', () =>
        HttpResponse.json({
          currentSlug: misfit.slug,
          matches: [
            {
              confidence: 84,
              rationale: 'Budget work dominates.',
              slug: '004767-budget-anl-2',
              title: 'Budget Anl 2',
              ucJobCode: '004767',
            },
          ],
        })
      ),
      http.post('/api/fit/rewrite', async ({ request }) => {
        sent = (await request.json()) as { targetSlug?: string };
        return HttpResponse.json(
          rewritten({ outside: [], slug: '004767-budget-anl-2', title: 'Budget Anl 2' })
        );
      })
    );
    renderRoute({ initialPath: '/backend/fit' });

    await user.click(await screen.findByRole('button', { name: 'Suggest class' }));
    await user.click(await screen.findByRole('button', { name: 'Rewrite to fit Budget Anl 2' }));

    await screen.findByText(/Draft written for/);
    expect(sent).toEqual({
      slug: misfit.slug,
      sourceFile: misfit.sourceFile,
      targetSlug: '004767-budget-anl-2',
    });
    expect(screen.getByRole('link', { name: 'Open draft →' })).toHaveAttribute(
      'href',
      '/class/004767-budget-anl-2?draft=12'
    );
    expect(screen.queryByText(/Left out/)).not.toBeInTheDocument();
  });

  it('shows the server refusal when none of the work fits', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.get('/api/fit/misfits', () =>
        HttpResponse.json({ misfits: [misfit], threshold: 90, totalJds: 10 })
      ),
      http.post('/api/fit/rewrite', () =>
        HttpResponse.json(
          {
            message:
              "None of this JD's work fits the class, so there is nothing to rewrite into it.",
          },
          { status: 400 }
        )
      )
    );
    renderRoute({ initialPath: '/backend/fit' });

    await user.click(await screen.findByRole('button', { name: 'Rewrite to fit' }));

    await screen.findByText(/nothing to rewrite into it/);
  });
});
