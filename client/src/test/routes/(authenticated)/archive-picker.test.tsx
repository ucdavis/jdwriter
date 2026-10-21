import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { ArchivedJd, SavedJd } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const SLUG = '009605-lab-ast-1';

// Synthetic: the shape of a class's archived JDs, none of their content.
const archived: ArchivedJd[] = [
  {
    department: 'Plant Sciences',
    origin: 'export',
    responsibilities: [
      { duties: ['Waters the tomato trials daily.', 'Logs greenhouse temperatures.'], functionName: 'Plant care', pct: 70 },
      { duties: ['Orders potting supplies.'], functionName: 'Supplies', pct: 30 },
    ],
    sourceFile: 'Lab Ast 1 (00000001)_[0001]_Job Description.html',
    summary: 'Runs the teaching greenhouse.',
    workingTitle: 'Greenhouse Assistant',
  },
  {
    department: 'Entomology',
    origin: 'imported',
    responsibilities: [{ duties: ['Rears insect colonies.'], functionName: 'Colonies', pct: 100 }],
    sourceFile: 'import/abc',
    summary: 'Maintains insect colonies.',
    workingTitle: 'Insectary Assistant',
  },
];

const withArchive = () =>
  testServer.use(http.get(`/api/classes/${SLUG}/archive`, () => HttpResponse.json({ jds: archived })));

const draft = (): SavedJd => ({
  assembled: false,
  authorAdditions: [],
  authoredJdId: 91,
  bargainingUnit: null,
  canPublish: false,
  complianceEdits: [],
  corpusNote: '',
  createdAt: '2026-10-21T00:00:00Z',
  createdBy: 'Sample User',
  department: 'Plant Sciences',
  draftState: {
    certs: [],
    department: 'Plant Sciences',
    education: [],
    minKSA: [],
    notes: '',
    prefKSA: [],
    resps: [
      {
        draft: '',
        duties: [{ added: true, kept: true, text: 'Waters the tomato trials daily.' }],
        functionKept: true,
        functionName: 'Plant care',
        pctTime: 100,
      },
    ],
    summary: 'Runs the teaching greenhouse.',
    version: 1,
    workEnv: [],
    workExp: [],
    workingTitle: 'Greenhouse Assistant',
  },
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
  slug: SLUG,
  status: 'draft',
  title: 'Lab Ast 1',
  ucJobCode: '009605',
  unallocatedPct: 0,
  updatedAt: '2026-10-21T00:00:00Z',
  workingTitle: 'Greenhouse Assistant',
});

describe('starting from an archived job description', () => {
  setupRouteTest();

  it('offers nothing when the class has no archived JDs', async () => {
    renderRoute({ initialPath: `/class/${SLUG}` });

    await screen.findByTestId('duties-intro');
    expect(screen.queryByRole('button', { name: /start from an archived job description/ })).not.toBeInTheDocument();
  });

  it('lists the class’s JDs by working title, searches their wording, and previews one', async () => {
    const user = userEvent.setup();
    withArchive();
    renderRoute({ initialPath: `/class/${SLUG}` });

    await user.click(await screen.findByRole('button', { name: 'Or start from an archived job description (2)' }));
    const list = screen.getByTestId('archive-list');
    expect(within(list).getAllByRole('button').map((b) => b.textContent)).toEqual([
      expect.stringContaining('Greenhouse Assistant'),
      expect.stringContaining('Insectary Assistant'),
    ]);
    expect(list).toHaveTextContent('HRTMS');

    // Search reaches the duties' wording, not just the title.
    await user.type(screen.getByRole('searchbox', { name: 'Search archived job descriptions' }), 'potting');
    expect(within(list).getAllByRole('button')).toHaveLength(1);

    await user.click(within(list).getByRole('button', { name: /Greenhouse Assistant/ }));
    const preview = screen.getByTestId('archive-preview');
    expect(preview).toHaveTextContent('Runs the teaching greenhouse.');
    expect(preview).toHaveTextContent('70% Plant care');
    expect(preview).toHaveTextContent('Orders potting supplies.');
    expect(preview).not.toHaveTextContent('00000001');

    await user.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('works from the chosen JD: a draft with its duties and summary opens in the builder', async () => {
    const user = userEvent.setup();
    let sent: { sourceFile: string } | null = null;
    withArchive();
    testServer.use(
      http.post(`/api/classes/${SLUG}/archive/start`, async ({ request }) => {
        sent = (await request.json()) as { sourceFile: string };
        return HttpResponse.json({
          authoredJdId: 91,
          carriedDuties: 1,
          droppedFunctions: 0,
          keptFunctions: 1,
          matchedDuties: 0,
          outside: [],
          slug: SLUG,
          title: 'Lab Ast 1',
        });
      }),
      http.get('/api/jds/91', () => HttpResponse.json(draft()))
    );
    renderRoute({ initialPath: `/class/${SLUG}` });

    await user.click(await screen.findByRole('button', { name: /start from an archived job description/ }));
    await user.click(within(screen.getByTestId('archive-list')).getByRole('button', { name: /Greenhouse Assistant/ }));
    await user.click(screen.getByRole('button', { name: 'Work from this JD' }));

    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Job summary' })).toHaveValue('Runs the teaching greenhouse.'));
    expect(sent).toEqual({ sourceFile: 'Lab Ast 1 (00000001)_[0001]_Job Description.html' });
    expect(screen.getByDisplayValue('Waters the tomato trials daily.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows why a start failed, and stays open', async () => {
    const user = userEvent.setup();
    withArchive();
    testServer.use(
      http.post(`/api/classes/${SLUG}/archive/start`, () =>
        HttpResponse.json({ message: 'Starting from an archived JD is unavailable — no AI provider is configured.' }, { status: 503 })
      )
    );
    renderRoute({ initialPath: `/class/${SLUG}` });

    await user.click(await screen.findByRole('button', { name: /start from an archived job description/ }));
    await user.click(within(screen.getByTestId('archive-list')).getByRole('button', { name: /Insectary Assistant/ }));
    await user.click(screen.getByRole('button', { name: 'Work from this JD' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('no AI provider is configured');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});
