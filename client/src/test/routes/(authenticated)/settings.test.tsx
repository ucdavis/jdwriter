import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { AdminEntry } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const entry = (loginId: string, over: Partial<AdminEntry> = {}): AdminEntry => ({
  displayName: null,
  fromConfiguration: false,
  grantedAt: '2026-10-01T00:00:00Z',
  grantedBy: 'Mock Admin',
  lastSeenAt: null,
  loginId,
  ...over,
});

const author = () =>
  http.get('/api/user/me', () =>
    HttpResponse.json({
      email: 'author@example.edu',
      iamId: null,
      id: 'author',
      name: 'Author Only',
      roles: ['Author'],
    })
  );

const section = (over: object) => ({
  added: 0, keptHere: 0, keptHereItems: [], name: '', onlyHere: 0, onlyHereItems: [], same: 0,
  skipped: 0, skippedItems: [], updated: 0, updatedItems: [], ...over,
});

const plan = (applied: boolean) => ({
  applied,
  counts: {},
  exportedAt: '2026-10-12T20:00:00Z',
  scrubbed: false,
  sections: [
    section({ added: 3, name: 'Classes', onlyHere: 1, onlyHereItems: ['Farm Laborer (004724)'], updated: 2, updatedItems: ['Lab Ast 1 (009605)', 'Lab Ast 2 (009606)'] }),
    section({ name: 'Job descriptions', same: 10 }),
  ],
});

describe('settings: admin whitelist', () => {
  setupRouteTest();

  it('adds an admin by login ID and lists them', async () => {
    const user = userEvent.setup();
    let admins: AdminEntry[] = [entry('rsmith', { fromConfiguration: true, grantedAt: null })];
    const posted: string[] = [];
    testServer.use(
      http.get('/api/admin/admins', () => HttpResponse.json({ admins })),
      http.post('/api/admin/admins', async ({ request }) => {
        const { loginId } = (await request.json()) as { loginId: string };
        posted.push(loginId);
        admins = [...admins, entry('jdoe')];
        return HttpResponse.json({ loginId: 'jdoe' });
      })
    );
    renderRoute({ initialPath: '/backend/settings' });

    await user.type(await screen.findByLabelText('UC Davis login ID'), 'jdoe');
    await user.click(screen.getByRole('button', { name: 'Add admin' }));

    const row = (await screen.findByText('jdoe')).closest('li')!;
    expect(posted).toEqual(['jdoe']);
    expect(within(row).getByText(/Has not signed in yet/)).toBeInTheDocument();
  });

  it('marks configured admins and offers no Remove for them', async () => {
    testServer.use(
      http.get('/api/admin/admins', () =>
        HttpResponse.json({
          admins: [entry('rsmith', { fromConfiguration: true }), entry('jdoe')],
        })
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    const configured = (await screen.findByText('rsmith')).closest('li')!;
    expect(within(configured).getByText('set in server configuration')).toBeInTheDocument();
    expect(within(configured).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();

    const granted = screen.getByText('jdoe').closest('li')!;
    expect(within(granted).getByRole('button', { name: 'Remove' })).toBeInTheDocument();
  });

  it('shows the server refusal verbatim', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.post('/api/admin/admins', () =>
        HttpResponse.json(
          { message: '“jdoe@gmail.com” is not a UC Davis login ID. Use the campus login, e.g. “rsmith”.' },
          { status: 400 }
        )
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    await user.type(await screen.findByLabelText('UC Davis login ID'), 'jdoe@gmail.com');
    await user.click(screen.getByRole('button', { name: 'Add admin' }));

    await screen.findByText(/is not a UC Davis login ID/);
  });

  it('previews a package import, then imports exactly that file on confirmation', async () => {
    const user = userEvent.setup();
    const sent: string[] = [];
    testServer.use(
      // Parts are counted, not parsed: Node's multipart reader rejects jsdom's File (see upload tests).
      http.post('/api/admin/package/preview', async ({ request }) => {
        sent.push(`preview:${((await request.text()).match(/name="file"/g) ?? []).length}`);
        return HttpResponse.json(plan(false));
      }),
      http.post('/api/admin/package/apply', async ({ request }) => {
        sent.push(`apply:${((await request.text()).match(/name="file"/g) ?? []).length}`);
        return HttpResponse.json(plan(true));
      })
    );
    renderRoute({ initialPath: '/backend/settings' });

    await user.upload(
      await screen.findByLabelText('Choose a package to import'),
      new File(['x'], 'pkg.jdwpkg.json.gz', { type: 'application/gzip' })
    );

    const planEl = await screen.findByTestId('package-plan');
    expect(planEl).toHaveTextContent('Nothing has changed yet');
    expect(within(planEl).getByText(/Classes: 2 updated from the file · 1 only here \(not removed\)/)).toBeInTheDocument();

    await user.click(within(planEl).getByRole('button', { name: 'Import 5 changes' }));

    await waitFor(() => expect(within(screen.getByTestId('package-plan')).getByText(/Done\./)).toBeInTheDocument());
    // The same file, sent again to import: the server holds nothing between preview and import.
    expect(sent).toEqual(['preview:1', 'apply:1']);
  });

  it('offers the one environment package export', async () => {
    renderRoute({ initialPath: '/backend/settings' });

    expect(await screen.findByRole('link', { name: 'Export package' })).toHaveAttribute(
      'href',
      '/api/admin/package/export'
    );
  });
});

describe('an author sees no back end', () => {
  setupRouteTest();

  it('has no back-end link in the navigation', async () => {
    testServer.use(author());
    renderRoute({ initialPath: '/' });

    await screen.findByText('Classify');
    expect(screen.queryByText('Back end')).not.toBeInTheDocument();
  });

  it('gets no link from the class page into the back-end standard view', async () => {
    // The mock gives this class a linked standard, so only the role decides.
    testServer.use(author());
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    const user = userEvent.setup();
    await user.click(await screen.findByLabelText('Show the job standard'));
    // The standard itself is for everyone; only the back-end view of it is not.
    expect(await screen.findByText(/Authoritative baseline/)).toBeInTheDocument();
    expect(screen.queryByText(/Open the standard in the back end/)).not.toBeInTheDocument();
  });

  it('while an admin does get that link', async () => {
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    const user = userEvent.setup();
    await user.click(await screen.findByLabelText('Show the job standard'));
    await screen.findByText(/Open the standard in the back end/);
  });

  it('settings page itself explains rather than failing', async () => {
    testServer.use(author());
    renderRoute({ initialPath: '/backend/settings' });

    await waitFor(() => expect(screen.getByText('Admin access required')).toBeInTheDocument());
  });
});
