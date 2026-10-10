import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';

describe('jobs with no class yet', () => {
  setupRouteTest();
  afterEach(() => vi.restoreAllMocks());

  it('lists active jobs with no class, and which have a standard', async () => {
    renderRoute({ initialPath: '/backend/corpus' });

    expect(await screen.findByTestId('coverage-summary')).toHaveTextContent(
      '3 active jobs with no class: 1 with a standard, 2 with no JDs or standard.'
    );
    const list = screen.getByTestId('coverage-list');
    expect(within(list).getByText('Synthetic Research Tech 2').closest('li')).toHaveTextContent('no standard');
    expect(within(list).getByText('Social Work HC Supv 2').closest('li')).toHaveTextContent('Health Center only');
    expect(within(list).getAllByRole('button', { name: 'Bootstrap' })).toHaveLength(1);
  });

  it('narrows to jobs with no JDs or standard', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/corpus' });

    await screen.findByTestId('coverage-list');
    await user.selectOptions(screen.getByLabelText('Standard'), 'without');

    const list = screen.getByTestId('coverage-list');
    expect(within(list).queryByText('Acad Achievement Cnslr 3')).not.toBeInTheDocument();
    expect(within(list).getByText('Synthetic Research Tech 2')).toBeInTheDocument();
  });

  it('bootstraps a job not active at UC Davis only after confirming, and says so to the server', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const sent: unknown[] = [];
    testServer.use(
      http.post('/api/admin/bootstrap', async ({ request }) => {
        sent.push(await request.json());
        return HttpResponse.json({ envelopeSource: 'standard', slug: '000685-accounting-manager-1', title: 'Accounting Manager 1', ucJobCode: '000685' });
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await screen.findByTestId('coverage-list');
    await user.selectOptions(screen.getByLabelText('Which jobs'), 'inactive');
    expect(await screen.findByTestId('coverage-summary')).toHaveTextContent('1 not-active job with no class');

    await user.click(within(screen.getByTestId('coverage-list')).getByRole('button', { name: 'Bootstrap' }));

    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining("isn't active at UC Davis"));
    expect(await screen.findByText('Created Accounting Manager 1 (000685) from its standard.')).toBeInTheDocument();
    expect(sent).toEqual([{ allowNotActive: true, title: 'Accounting Manager 1' }]);
  });

  it('bootstraps an active job without asking twice', async () => {
    const user = userEvent.setup();
    const confirm = vi.spyOn(window, 'confirm');
    const sent: unknown[] = [];
    testServer.use(
      http.post('/api/admin/bootstrap', async ({ request }) => {
        sent.push(await request.json());
        return HttpResponse.json({ envelopeSource: 'standard', slug: 'x', title: 'Academic Achievement Counselor 3', ucJobCode: '004501' });
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.click(within(await screen.findByTestId('coverage-list')).getByRole('button', { name: 'Bootstrap' }));

    await screen.findByText(/Created Academic Achievement Counselor 3/);
    expect(confirm).not.toHaveBeenCalled();
    expect(sent).toEqual([{ allowNotActive: false, title: 'Academic Achievement Counselor 3' }]);
  });
});
