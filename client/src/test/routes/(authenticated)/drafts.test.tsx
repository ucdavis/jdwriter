import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { BuildRequest, SavedJd } from '@/lib/contracts.ts';
import type { DraftState } from '@/features/build/useBuildState.ts';
import { cleanup, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const draftJd = (state: DraftState | null, over: Partial<SavedJd> = {}): SavedJd => ({
  assembled: false,
  authorAdditions: [],
  authoredJdId: 12,
  bargainingUnit: null,
  canPublish: false,
  complianceEdits: [],
  corpusNote: 'Draft — not assembled yet, so not in the corpus.',
  createdAt: '2026-10-05T00:00:00Z',
  createdBy: 'Sample User',
  department: '',
  draftState: state,
  flsaStatus: null,
  fromEnvelope: false,
  inCorpus: false,
  jd: {
    conditionsOfEmployment: [],
    education: [],
    jobSummary: '',
    keyResponsibilities: [],
    licensesCertifications: [],
    minKSA: [],
    physicalRequirements: [],
    prefKSA: [],
    workEnvironment: [],
    workExperience: [],
  },
  notes: '',
  salaryGrade: null,
  slug: '009605-lab-ast-1',
  status: 'draft',
  title: 'Lab Ast 1',
  ucJobCode: '009605',
  unallocatedPct: 0,
  updatedAt: '2026-10-05T00:00:00Z',
  workingTitle: 'Greenhouse Tech',
  ...over,
});

describe('drafts', () => {
  setupRouteTest();

  it('saves a draft with the whole build screen, without assembling', async () => {
    const user = userEvent.setup();
    const posted: BuildRequest[] = [];
    let assembled = false;
    testServer.use(
      http.post('/api/build/draft', async ({ request }) => {
        posted.push((await request.json()) as BuildRequest);
        return HttpResponse.json({ authoredJdId: 12 });
      }),
      http.post('/api/build/assemble', () => {
        assembled = true;
        return HttpResponse.json({});
      })
    );
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    await user.type(await screen.findByLabelText('Working title'), 'Greenhouse Tech');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    expect(await screen.findByTestId('draft-saved')).toHaveTextContent('Draft saved');
    const state = posted[0].draftState as DraftState;
    expect(state.version).toBe(1);
    expect(state.workingTitle).toBe('Greenhouse Tech');
    expect(state.resps.length).toBeGreaterThan(0);
    expect(assembled).toBe(false);
  });

  it('continues a draft exactly as it was left, and finishing it revises the same JD', async () => {
    const user = userEvent.setup();
    // Start from a real snapshot of the fresh build screen, then change it as an author would.
    let snapshot: DraftState | null = null;
    testServer.use(
      http.post('/api/build/draft', async ({ request }) => {
        snapshot = ((await request.json()) as BuildRequest).draftState as DraftState;
        return HttpResponse.json({ authoredJdId: 12 });
      })
    );
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });
    await user.click(await screen.findByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(snapshot).not.toBeNull());
    cleanup();

    const left: DraftState = {
      ...snapshot!,
      resps: snapshot!.resps.map((r, i) => (i === 0 ? { ...r, functionKept: false } : r)),
      workingTitle: 'Resumed Title',
    };
    const sentIds: Array<number | null | undefined> = [];
    testServer.use(
      http.get('/api/jds/:id', () => HttpResponse.json(draftJd(left))),
      http.post('/api/build/draft', async ({ request }) => {
        sentIds.push(((await request.json()) as BuildRequest).authoredJdId);
        return HttpResponse.json({ authoredJdId: 12 });
      })
    );
    renderRoute({ initialPath: '/class/009605-lab-ast-1?draft=12' });

    expect(await screen.findByLabelText('Working title')).toHaveValue('Resumed Title');
    expect(screen.getAllByRole('checkbox', { name: /^Include / })[0]).not.toBeChecked();

    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(sentIds).toEqual([12]));
  });

  it('marks a never-assembled draft and offers Continue editing', async () => {
    testServer.use(
      http.get('/api/jds/:id', () =>
        HttpResponse.json(draftJd({ version: 1 } as DraftState))
      )
    );
    renderRoute({ initialPath: '/jds/12' });

    expect(await screen.findByTestId('not-assembled')).toHaveTextContent(/saved before it was assembled/);
    expect(screen.getByRole('link', { name: 'Continue editing' })).toHaveAttribute(
      'href',
      '/class/009605-lab-ast-1?draft=12'
    );
  });
});
