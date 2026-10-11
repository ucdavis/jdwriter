import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';

describe('blank standards', () => {
  setupRouteTest();
  afterEach(() => vi.restoreAllMocks());

  it('finds classes built from blank standards and removes them, keeping any with saved JDs', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    let removed = false;
    testServer.use(
      http.get('/api/admin/standards/blank-classes', () =>
        HttpResponse.json({
          classes: [
            { savedJds: 0, slug: '009214-mos-crd-3', title: 'Medical Office Services Coordinator 3', ucJobCode: '009214' },
            { savedJds: 2, slug: '006314-pem-ast', title: 'Public Events Manager Assistant', ucJobCode: '006314' },
          ],
        })
      ),
      http.post('/api/admin/standards/blank-classes/remove', () => {
        removed = true;
        return HttpResponse.json({
          kept: [{ savedJds: 2, slug: '006314-pem-ast', title: 'Public Events Manager Assistant', ucJobCode: '006314' }],
          removed: [{ savedJds: 0, slug: '009214-mos-crd-3', title: 'Medical Office Services Coordinator 3', ucJobCode: '009214' }],
        });
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.click(await screen.findByRole('button', { name: 'Find them' }));
    const list = await screen.findByTestId('blank-classes');
    expect(within(list).getByText(/Public Events Manager Assistant/).closest('li')).toHaveTextContent('kept — 2 saved JDs');

    await user.click(screen.getByRole('button', { name: 'Remove 1' }));

    expect(await screen.findByTestId('blank-cleanup-result')).toHaveTextContent(
      'Removed 1 class; kept 1 with saved JDs (Public Events Manager Assistant).'
    );
    expect(removed).toBe(true);
  });

  it('marks a blank standard in the jobs list and offers no bootstrap from it', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.get('/api/admin/jobs', () =>
        HttpResponse.json({
          jobs: [
            { active: true, class: null, code: '009214', family: null, grade: null, site: null, standard: { blank: true, how: 'title', title: 'Medical Office Services Coordinator 3' }, supersededBy: null, title: 'Med Ofc Svc Crd 3' },
          ],
          scope: 'active',
        })
      )
    );
    renderRoute({ initialPath: '/backend/jobs' });

    const row = (await screen.findByText('Med Ofc Svc Crd 3')).closest('button') as HTMLElement;
    expect(row).toHaveTextContent('blank — no responsibilities');
    await user.click(row);

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByTestId('blank-standard')).toHaveTextContent("can't be used to build an envelope");
    expect(within(dialog).queryByRole('button', { name: 'Bootstrap from its standard' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Replace the standard' })).toBeInTheDocument();
  });
});
