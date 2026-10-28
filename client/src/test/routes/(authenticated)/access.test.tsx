import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import type { ProblemReport } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const me = (roles: string[], accessRequestedAt: string | null = null) => ({
  accessRequestedAt,
  email: 'newperson@example.edu',
  iamId: null,
  id: 'oid-new',
  name: 'New Person',
  roles,
});

describe('the access allow-list', () => {
  setupRouteTest();

  it('sends someone not on the list to a page to ask, and lets them in once approved', async () => {
    const user = userEvent.setup();
    let approved = false;
    let asked: string | null = null;
    const sent: unknown[] = [];
    testServer.use(
      http.get('/api/user/me', () => HttpResponse.json(me(approved ? ['Author'] : [], asked))),
      http.post('/api/user/access-request', async ({ request }) => {
        sent.push(await request.json());
        asked = '2026-10-28T17:00:00Z';
        return HttpResponse.json({ hasAccess: false, requestedAt: asked });
      })
    );
    renderRoute({ initialPath: '/classify' });

    expect(await screen.findByRole('heading', { name: 'Request access to JDWriter' })).toBeInTheDocument();
    // None of the app: no navigation, no footer, no page behind it.
    expect(screen.queryByRole('link', { name: 'Classify' })).not.toBeInTheDocument();
    expect(screen.queryByRole('contentinfo')).not.toBeInTheDocument();
    expect(screen.getByText('New Person')).toBeInTheDocument();

    await user.type(screen.getByRole('textbox', { name: /Who you are/ }), 'Plant Sciences, writing two JDs.');
    await user.click(screen.getByRole('button', { name: 'Request access' }));
    expect(await screen.findByTestId('access-asked')).toHaveTextContent('You asked on');
    expect(sent).toEqual([{ message: 'Plant Sciences, writing two JDs.' }]);

    await user.click(screen.getByRole('button', { name: 'Check again' }));
    expect(await screen.findByText('Not yet approved.')).toBeInTheDocument();

    approved = true;
    await user.click(screen.getByRole('button', { name: 'Check again' }));
    expect(await screen.findByRole('link', { name: 'Classify' })).toBeInTheDocument();
  });

  it('lets admins approve who has signed in, add by login and remove', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    const calls: string[] = [];
    testServer.use(
      http.get('/api/admin/access', () =>
        HttpResponse.json({
          approved: [{ displayName: 'Ana Ruiz', email: null, grantedAt: '2026-10-28T00:00:00Z', grantedBy: 'Mock Admin', lastSeenAt: null, loginId: 'aruiz' }],
          waiting: [
            { displayName: 'New Person', email: null, lastSeenAt: '2026-10-28T00:00:00Z', loginId: 'newp', requestedAt: '2026-10-28T00:00:00Z' },
            { displayName: 'Other Person', email: null, lastSeenAt: '2026-10-27T00:00:00Z', loginId: 'otherp', requestedAt: null },
          ],
        })
      ),
      http.post('/api/admin/access', async ({ request }) => {
        calls.push(`grant ${((await request.json()) as { loginId: string }).loginId}`);
        return HttpResponse.json({ loginId: 'x' });
      }),
      http.post('/api/admin/access/approve-waiting', () => {
        calls.push('approve all');
        return HttpResponse.json({ approved: 2 });
      }),
      http.delete('/api/admin/access/:login', ({ params }) => {
        calls.push(`revoke ${String(params.login)}`);
        return HttpResponse.json({ ok: true });
      })
    );
    renderRoute({ initialPath: '/backend/settings' });

    const waiting = await screen.findByTestId('access-waiting');
    expect(within(waiting).getByText(/asked/)).toBeInTheDocument();
    await user.click(within(waiting).getAllByRole('button', { name: 'Approve' })[0]);
    await user.click(within(waiting).getByRole('button', { name: 'Approve all 2' }));
    await user.type(screen.getByRole('textbox', { name: 'UC Davis login ID to approve' }), 'jdoe');
    await user.click(screen.getByRole('button', { name: 'Give access' }));
    await user.click(within(screen.getByTestId('access-approved')).getByRole('button', { name: 'Remove access for aruiz' }));

    await waitFor(() => expect(calls).toEqual(['grant newp', 'approve all', 'grant jdoe', 'revoke aruiz']));
  });

  it('approves an access request from the reports inbox', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    let approvedId: string | null = null;
    const request: ProblemReport = {
      browser: 'Chrome', createdAt: '2026-10-28T17:00:00Z', from: 'New Person', fromEmail: null, id: 9,
      kind: 'Access', message: 'Plant Sciences, writing two JDs.', note: '', page: '/', resolved: false, resolvedAt: null,
    };
    testServer.use(
      http.get('/api/admin/reports', () => HttpResponse.json({ open: 1, reports: [request] })),
      http.post('/api/admin/reports/:id/approve-access', ({ params }) => {
        approvedId = String(params.id);
        return HttpResponse.json({ loginId: 'newp' });
      })
    );
    renderRoute({ initialPath: '/backend/reports' });

    const card = await screen.findByRole('article', { name: 'Report from New Person' });
    expect(card).toHaveTextContent('Access request');
    expect(within(card).getByRole('button', { name: 'Decline' })).toBeInTheDocument();
    await user.click(within(card).getByRole('button', { name: 'Approve access' }));
    await waitFor(() => expect(approvedId).toBe('9'));
  });
});
