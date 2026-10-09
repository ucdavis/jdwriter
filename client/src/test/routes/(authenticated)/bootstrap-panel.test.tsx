import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';

const candidate = (title: string, code: string) => ({
  code,
  family: 'Student Services',
  function: 'Advising',
  grade: 'Grade 20',
  title,
});

describe('bootstrap from a standard', () => {
  setupRouteTest();

  it('filters candidates and creates an envelope for the chosen one', async () => {
    const user = userEvent.setup();
    let remaining = [
      candidate('Financial Aid Advisor 1', '004502'),
      candidate('Career Counselor 2', '004610'),
    ];
    const created: string[] = [];
    testServer.use(
      http.get('/api/admin/bootstrap/candidates', () =>
        HttpResponse.json({ candidates: remaining })
      ),
      http.post('/api/admin/bootstrap', async ({ request }) => {
        const { title } = (await request.json()) as { title: string };
        created.push(title);
        remaining = remaining.filter((c) => c.title !== title);
        return HttpResponse.json({
          envelopeSource: 'standard',
          slug: '004610-career-counselor-2',
          title,
          ucJobCode: '004610',
        });
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.click(await screen.findByRole('button', { name: 'Find candidates' }));
    await screen.findByText('2 of 2');

    await user.type(screen.getByLabelText('Filter candidates by title'), 'career');
    expect(screen.getByText('1 of 2')).toBeInTheDocument();
    expect(screen.queryByText('Financial Aid Advisor 1')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Create envelope' }));

    await screen.findByText(/Created Career Counselor 2 \(004610\) from its standard/);
    expect(created).toEqual(['Career Counselor 2']);
    // The list is re-asked of the server, so the created class drops out of it.
    await waitFor(() => {
      expect(screen.queryByText('Career Counselor 2', { selector: 'div' })).not.toBeInTheDocument();
    });
  });

  it('shows the server refusal for a superseded class verbatim', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.get('/api/admin/bootstrap/candidates', () =>
        HttpResponse.json({ candidates: [candidate('Old Analyst 2', '001000')] })
      ),
      http.post('/api/admin/bootstrap', () =>
        HttpResponse.json(
          { message: 'Old Analyst 2 (001000) is superseded by 001001; bootstrap that instead.' },
          { status: 400 }
        )
      )
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.click(await screen.findByRole('button', { name: 'Find candidates' }));
    await user.click(await screen.findByRole('button', { name: 'Create envelope' }));

    await screen.findByText(/superseded by 001001/);
  });

  describe('create all', () => {
    afterEach(() => {
      vi.restoreAllMocks();
    });

    it('retires superseded classes first, then creates every candidate in turn', async () => {
      const user = userEvent.setup();
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      const calls: string[] = [];
      let retired = false;
      let remaining = [candidate('Widget Analyst 2', '111111')];
      testServer.use(
        http.post('/api/admin/supersessions/retire', () => {
          calls.push('retire');
          retired = true;
          // Retiring the dead class surfaces its successor's standard.
          remaining = [
            ...remaining,
            candidate('Financial Analyst 3 CX', '005183'),
          ];
          return HttpResponse.json({
            profiles: [
              {
                action: 'remove',
                authoredJds: 0,
                code: '007709',
                corpusJds: 0,
                needsRebuild: false,
                slug: '007709-financial-analyst-3',
                successorCode: '005183',
                successorSlug: null,
                successorTitle: 'Financial Analyst 3 CX',
                title: 'Financial Analyst 3',
              },
            ],
            refiledJds: 0,
          });
        }),
        http.get('/api/admin/bootstrap/candidates', () => {
          calls.push(retired ? 'list-after-retire' : 'list');
          return HttpResponse.json({ candidates: remaining });
        }),
        http.post('/api/admin/bootstrap', async ({ request }) => {
          const { title } = (await request.json()) as { title: string };
          calls.push(`create ${title}`);
          remaining = remaining.filter((c) => c.title !== title);
          return HttpResponse.json({
            envelopeSource: 'standard',
            slug: 'x',
            title,
            ucJobCode: '000000',
          });
        })
      );
      renderRoute({ initialPath: '/backend/corpus' });

      await user.click(
        await screen.findByRole('button', { name: 'Find candidates' })
      );
      await user.click(
        await screen.findByRole('button', { name: 'Create all 1' })
      );

      await screen.findByText(/Created 2 classes from their standards/);
      expect(
        screen.getByText(/Retired 1 superseded class first/)
      ).toBeInTheDocument();
      expect(calls.slice(0, 5)).toEqual([
        'list',
        'retire',
        'list-after-retire',
        'create Widget Analyst 2',
        'create Financial Analyst 3 CX',
      ]);
      await screen.findByText(/No candidates/);
    });

    it('marks a class that fails and carries on with the rest', async () => {
      const user = userEvent.setup();
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      const remaining = [
        candidate('Broken Analyst 1', '222222'),
        candidate('Fine Analyst 1', '333333'),
      ];
      let created = false;
      testServer.use(
        http.post('/api/admin/supersessions/retire', () =>
          HttpResponse.json({ profiles: [], refiledJds: 0 })
        ),
        http.get('/api/admin/bootstrap/candidates', () =>
          HttpResponse.json({
            candidates: remaining.filter(
              (c) => c.title !== 'Fine Analyst 1' || !created
            ),
          })
        ),
        http.post('/api/admin/bootstrap', async ({ request }) => {
          const { title } = (await request.json()) as { title: string };
          if (title === 'Broken Analyst 1') {
            return HttpResponse.json(
              { message: 'No standard found' },
              { status: 400 }
            );
          }
          created = true;
          return HttpResponse.json({
            envelopeSource: 'standard',
            slug: 'x',
            title,
            ucJobCode: '333333',
          });
        })
      );
      renderRoute({ initialPath: '/backend/corpus' });

      await user.click(
        await screen.findByRole('button', { name: 'Find candidates' })
      );
      await user.click(
        await screen.findByRole('button', { name: 'Create all 2' })
      );

      await screen.findByText(/Created 1 class from their standards; 1 failed/);
      expect(
        screen.getByText(/Broken Analyst 1: No standard found/)
      ).toBeInTheDocument();
      expect(screen.getByText('failed')).toBeInTheDocument();
    });

    it('lists only classes on UC Davis payroll and says how many standards were left out', async () => {
      const user = userEvent.setup();
      testServer.use(
        http.get('/api/admin/bootstrap/candidates', () =>
          HttpResponse.json({
            candidates: [candidate('Widget Analyst 2', '111111')],
            noCodeMatch: 31,
            notOnPayroll: 59,
          })
        )
      );
      renderRoute({ initialPath: '/backend/corpus' });

      await user.click(
        await screen.findByRole('button', { name: 'Find candidates' })
      );

      expect(await screen.findByTestId('bootstrap-left-out')).toHaveTextContent(
        /90 standards are left out/
      );
      expect(screen.getByRole('button', { name: 'Create all 1' })).toBeInTheDocument();
      expect(screen.queryByText(/not on UC Davis payroll/)).not.toBeInTheDocument();
    });

    it('does nothing when the confirmation is declined', async () => {
      const user = userEvent.setup();
      vi.spyOn(window, 'confirm').mockReturnValue(false);
      let retireCalls = 0;
      testServer.use(
        http.post('/api/admin/supersessions/retire', () => {
          retireCalls++;
          return HttpResponse.json({ profiles: [], refiledJds: 0 });
        }),
        http.get('/api/admin/bootstrap/candidates', () =>
          HttpResponse.json({
            candidates: [candidate('Widget Analyst 2', '111111')],
          })
        )
      );
      renderRoute({ initialPath: '/backend/corpus' });

      await user.click(
        await screen.findByRole('button', { name: 'Find candidates' })
      );
      await user.click(
        await screen.findByRole('button', { name: 'Create all 1' })
      );

      expect(retireCalls).toBe(0);
      expect(
        screen.getByRole('button', { name: 'Create envelope' })
      ).toBeEnabled();
    });
  });
});

describe('superseded classes', () => {
  setupRouteTest();

  it('previews what retiring does and retires on request', async () => {
    const user = userEvent.setup();
    const profile = {
      action: 'merge',
      authoredJds: 2,
      code: '007709',
      corpusJds: 7,
      needsRebuild: true,
      slug: '007709-financial-anl-3',
      successorCode: '005183',
      successorSlug: '005183-financial-anl-3-cx',
      successorTitle: 'Financial Anl 3 Cx',
      title: 'Financial Anl 3',
    };
    testServer.use(
      http.get('/api/admin/supersessions/retire', () =>
        HttpResponse.json({ profiles: [profile], refiledJds: 7 })
      ),
      http.post('/api/admin/supersessions/retire', () =>
        HttpResponse.json({ profiles: [profile], refiledJds: 7 })
      )
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.click(await screen.findByRole('button', { name: 'Check' }));
    await screen.findByText('Financial Anl 3 (007709)');
    expect(
      screen.getByText(
        /Merge into Financial Anl 3 Cx \(005183\) · 7 corpus JDs · 2 saved JDs/
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Retire 1' }));

    await screen.findByText(/Retired 1 class and refiled 7 JDs/);
    expect(
      screen.getByText(
        /Rebuild from the corpus to learn from the refiled JDs: Financial Anl 3 Cx/
      )
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Retire 1' })
    ).not.toBeInTheDocument();
  });
});
