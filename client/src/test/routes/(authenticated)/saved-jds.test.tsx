import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { BuildRequest, SavedJd, SavedJdSummary } from '@/lib/contracts.ts';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';

const summary = (over: Partial<SavedJdSummary>): SavedJdSummary => ({
  createdAt: '2026-10-01T00:00:00Z',
  createdBy: 'Sample User',
  department: 'Plant Sciences',
  id: 1,
  slug: '009605-lab-ast-1',
  status: 'ready',
  title: 'Lab Ast 1',
  ucJobCode: '009605',
  unallocatedPct: 0,
  updatedAt: '2026-10-01T00:00:00Z',
  workingTitle: 'Greenhouse Technician',
  ...over,
});

const saved = (over: Partial<SavedJd> = {}): SavedJd => ({
  authorAdditions: ['Coordinates the annual plant sale'],
  authoredJdId: 7,
  bargainingUnit: 'TX',
  canPublish: true,
  complianceEdits: [],
  corpusNote: 'Added to this class’s corpus. It counts the next time the class is rebuilt.',
  createdAt: '2026-10-01T00:00:00Z',
  createdBy: 'Sample User',
  department: 'Plant Sciences',
  flsaStatus: 'Non-Exempt',
  inCorpus: true,
  jd: {
    conditionsOfEmployment: [],
    education: ['High school diploma'],
    jobSummary: 'Supports greenhouse research.',
    keyResponsibilities: [{ duties: ['Waters plants'], functionName: 'Plant care', pctTime: 100 }],
    licensesCertifications: [],
    minKSA: [],
    physicalRequirements: [],
    prefKSA: [],
    workEnvironment: [],
    workExperience: [],
  },
  notes: 'Replaces a retiring incumbent.',
  salaryGrade: 'STEPS',
  slug: '009605-lab-ast-1',
  status: 'ready',
  title: 'Lab Ast 1',
  ucJobCode: '009605',
  unallocatedPct: 0,
  updatedAt: '2026-10-01T00:00:00Z',
  workingTitle: 'Greenhouse Technician',
  ...over,
});

describe('saved JDs', () => {
  setupRouteTest();

  it('lists my JDs with their status', async () => {
    testServer.use(
      http.get('/api/jds', () =>
        HttpResponse.json({
          jds: [summary({}), summary({ id: 2, status: 'draft', unallocatedPct: 20, workingTitle: 'Field Aide' })],
        })
      )
    );
    renderRoute({ initialPath: '/jds' });

    await screen.findByText('Greenhouse Technician');
    expect(screen.getByText('Ready')).toBeInTheDocument();
    expect(screen.getByText('Draft · 20% unallocated')).toBeInTheDocument();
  });

  it('shows an empty state that says how JDs get saved', async () => {
    testServer.use(http.get('/api/jds', () => HttpResponse.json({ jds: [] })));
    renderRoute({ initialPath: '/jds' });

    await screen.findByText(/it is saved as soon as you assemble it/);
  });

  it('lets an admin switch to everyone’s JDs, and not an author', async () => {
    const user = userEvent.setup();
    const asked: string[] = [];
    testServer.use(
      http.get('/api/jds', ({ request }) => {
        asked.push(new URL(request.url).search);
        return HttpResponse.json({ jds: [summary({ createdBy: 'Someone Else' })] });
      })
    );
    renderRoute({ initialPath: '/jds' });

    await user.click(await screen.findByRole('button', { name: 'Everyone’s' }));
    await screen.findByText(/by Someone Else/);
    expect(asked).toContain('?scope=all');
  });

  it('opens a saved JD with what the author added and their notes', async () => {
    testServer.use(http.get('/api/jds/:id', () => HttpResponse.json(saved())));
    renderRoute({ initialPath: '/jds/7' });

    await screen.findByText('Coordinates the annual plant sale');
    expect(screen.getByText('Replaces a retiring incumbent.')).toBeInTheDocument();
    expect(screen.getByText('Supports greenhouse research.')).toBeInTheDocument();
  });

  it('assembling saves the JD, and assembling again revises the same one', async () => {
    const user = userEvent.setup();
    const sent: Array<number | null | undefined> = [];
    const verdicts: Array<string | null | undefined> = [];
    testServer.use(
      http.post('/api/build/assemble', async ({ request }) => {
        const body = (await request.json()) as BuildRequest;
        sent.push(body.authoredJdId);
        verdicts.push(body.envelopeVerdict);
        return HttpResponse.json(saved({ authoredJdId: 42 }));
      })
    );
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    await user.click(await screen.findByRole('button', { name: /Review & continue/ }));
    await user.click(await screen.findByRole('button', { name: /Assemble the JD/ }));
    expect(await screen.findByTestId('saved-status')).toHaveTextContent('Saved as Ready');
    expect(screen.getByTestId('corpus-note')).toHaveTextContent(/Added to this class’s corpus/);
    expect(verdicts[0]).toBe('in_envelope');

    // Back to tailoring and assemble again: the second request names the saved record.
    await user.click(screen.getByRole('button', { name: /Back to tailoring/ }));
    await user.click(await screen.findByRole('button', { name: /Review & continue/ }));
    await user.click(await screen.findByRole('button', { name: /Assemble the JD/ }));
    await waitFor(() => expect(sent).toEqual([null, 42]));
  });

  it('sorts by date created, newest or oldest first', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.get('/api/jds', () =>
        HttpResponse.json({
          jds: [
            summary({ createdAt: '2026-09-01T00:00:00Z', id: 1, workingTitle: 'September JD' }),
            summary({ createdAt: '2026-10-01T00:00:00Z', id: 2, workingTitle: 'October JD' }),
          ],
        })
      )
    );
    renderRoute({ initialPath: '/jds' });

    await screen.findByText('October JD');
    const order = () =>
      screen.getAllByText(/(September|October) JD/).map((el) => el.textContent);
    expect(order()).toEqual(['October JD', 'September JD']);

    await user.selectOptions(screen.getByLabelText('Sort by date created'), 'oldest');
    expect(order()).toEqual(['September JD', 'October JD']);
  });

  it('deletes a JD after confirming, and not without', async () => {
    const user = userEvent.setup();
    const deleted: string[] = [];
    let jds = [summary({ id: 5, workingTitle: 'To Delete' })];
    testServer.use(
      http.get('/api/jds', () => HttpResponse.json({ jds })),
      http.delete('/api/jds/:id', ({ params }) => {
        deleted.push(String(params.id));
        jds = [];
        return HttpResponse.json({ ok: true });
      })
    );
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    renderRoute({ initialPath: '/jds' });

    const button = await screen.findByRole('button', { name: 'Delete To Delete' });
    await user.click(button);
    expect(deleted).toEqual([]);

    await user.click(button);
    await screen.findByText(/Nothing saved yet/);
    expect(deleted).toEqual(['5']);
    confirm.mockRestore();
  });

  it('shows my three most recently created JDs on the home page', async () => {
    testServer.use(
      http.get('/api/jds', () =>
        HttpResponse.json({
          jds: [1, 2, 3, 4].map((n) =>
            summary({ createdAt: `2026-09-0${n}T00:00:00Z`, id: n, workingTitle: `JD number ${n}` })
          ),
        })
      )
    );
    renderRoute({ initialPath: '/' });

    await screen.findByText('Your recent JDs');
    await screen.findByText('JD number 4');
    expect(screen.getByText('JD number 3')).toBeInTheDocument();
    expect(screen.getByText('JD number 2')).toBeInTheDocument();
    expect(screen.queryByText('JD number 1')).not.toBeInTheDocument();
  });
});
