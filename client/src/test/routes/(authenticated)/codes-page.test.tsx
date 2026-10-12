import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';

describe('the jobs directory', () => {
  setupRouteTest();
  afterEach(() => vi.restoreAllMocks());

  it('lists every active job with its class and standard', async () => {
    renderRoute({ initialPath: '/backend/codes' });

    expect(await screen.findByTestId('jobs-summary')).toHaveTextContent('3 jobs · 1 with a class · 1 with a standard');
    // The job and its class share a title here: the row is the button around both.
    const lab = within(screen.getByTestId('jobs-list')).getAllByText('Lab Ast 1')[0].closest('button') as HTMLElement;
    expect(lab).toHaveTextContent('155 JDs');
    const hc = screen.getByText('Social Work HC Supv 2').closest('button') as HTMLElement;
    expect(hc).toHaveTextContent('Health Center only');
  });

  it('filters to jobs with no class and no standard', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/codes' });

    await screen.findByTestId('jobs-list');
    await user.selectOptions(screen.getByLabelText('Class'), 'none');
    await user.selectOptions(screen.getByLabelText('Standard'), 'none');

    const list = screen.getByTestId('jobs-list');
    expect(within(list).getAllByRole('button')).toHaveLength(1);
    expect(list).toHaveTextContent('Social Work HC Supv 2');
  });

  it('bootstraps a job not active at UC Davis from its dialog, after confirming', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const sent: unknown[] = [];
    testServer.use(
      http.post('/api/admin/bootstrap', async ({ request }) => {
        sent.push(await request.json());
        return HttpResponse.json({ envelopeSource: 'standard', slug: '000685-accounting-manager-1', title: 'Accounting Manager 1', ucJobCode: '000685' });
      })
    );
    renderRoute({ initialPath: '/backend/codes' });

    await screen.findByTestId('jobs-list');
    await user.selectOptions(screen.getByLabelText('Which jobs'), 'inactive');
    await user.click(await screen.findByText('Accounting Mgr 1'));

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Linked by hand');
    await user.click(within(dialog).getByRole('button', { name: 'Bootstrap from its standard' }));

    expect(await within(dialog).findByText('Created Accounting Manager 1 (000685) from its standard.')).toBeInTheDocument();
    expect(sent).toEqual([{ allowNotActive: true, title: 'Accounting Manager 1' }]);
  });

  it('adds a standard by hand: reads a Word document into fields, then saves what was reviewed', async () => {
    const user = userEvent.setup();
    const saved: unknown[] = [];
    testServer.use(
      http.post('/api/admin/standards/read', () =>
        HttpResponse.json({
          education: ['Bachelor’s degree or equivalent experience.'],
          keyResponsibilities: ['Provides counseling.', 'Keeps records.'],
          ksa: [],
          licenses: [],
          scope: 'Synthetic scope.',
          specialConditions: [],
          title: 'Clinical Social Worker Supervisor 2 HC',
        })
      ),
      http.post('/api/admin/standards/manual', async ({ request }) => {
        saved.push(await request.json());
        return HttpResponse.json({ ok: true });
      })
    );
    renderRoute({ initialPath: '/backend/codes' });

    await user.click(await screen.findByText('Social Work HC Supv 2'));
    const dialog = screen.getByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Add the standard' }));
    await user.upload(within(dialog).getByLabelText('Standard document'), new File(['x'], 'standard.docx'));

    const form = await within(dialog).findByTestId('standard-form');
    await within(form).findByDisplayValue('Clinical Social Worker Supervisor 2 HC');
    // The admin edits before saving.
    await user.clear(within(form).getByLabelText('Scope'));
    await user.type(within(form).getByLabelText('Scope'), 'Reviewed scope.');
    await user.click(within(form).getByRole('button', { name: 'Save the standard' }));

    await vi.waitFor(() => expect(saved).toHaveLength(1));
    expect(saved[0]).toMatchObject({
      standard: { keyResponsibilities: ['Provides counseling.', 'Keeps records.'], scope: 'Reviewed scope.', title: 'Clinical Social Worker Supervisor 2 HC' },
      ucJobCode: '006536',
    });
  });

  it('shows what each job without a class needs, and gathers them with "Waiting for a class"', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/codes' });

    await screen.findByTestId('jobs-list');
    expect(screen.getByText('Acad Achievement Cnslr 3').closest('button')).toHaveTextContent('Needs: Ready to bootstrap');
    expect(screen.getByText('Social Work HC Supv 2').closest('button')).toHaveTextContent('Needs: JDs, or a standard');

    await user.click(screen.getByRole('button', { name: 'Waiting for a class' }));

    expect(screen.getByLabelText('Class')).toHaveValue('none');
    expect(screen.getByTestId('jobs-needs')).toHaveTextContent('1 ready to bootstrap');
    expect(within(screen.getByTestId('jobs-list')).queryByText('Lab Ast 1')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download these 2 as CSV/ })).toBeInTheDocument();
  });

  it('ties a superseded job to its successor\'s standard', async () => {
    testServer.use(
      http.get('/api/admin/jobs', () =>
        HttpResponse.json({
          jobs: [
            { active: true, class: { corpusSize: 30, envelopeSource: 'claude', slug: '005183-fa-3-cx', title: 'Financial Analyst 3 CX' }, code: '007709', family: null, grade: null, site: null, standard: { fromSuccessor: true, how: 'title', title: 'Financial Analyst 3 CX' }, supersededBy: '005183', title: 'Financial Anl 3' },
          ],
          scope: 'active',
        })
      )
    );
    renderRoute({ initialPath: '/backend/codes' });

    const row = (await screen.findByText('Financial Anl 3')).closest('button') as HTMLElement;
    expect(row).toHaveTextContent("successor's");
    expect(row).not.toHaveTextContent('Needs:');
  });

  it('is in the back-end navigation', async () => {
    renderRoute({ initialPath: '/backend/standards' });

    expect(await screen.findByRole('link', { name: 'Codes' })).toHaveAttribute('href', '/backend/codes');
  });
});
