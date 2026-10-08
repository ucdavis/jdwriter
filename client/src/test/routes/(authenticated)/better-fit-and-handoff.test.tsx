import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { BuildRequest, EnvelopeCheckResponse, SavedJd } from '@/lib/contracts.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const check = (over: Partial<EnvelopeCheckResponse>): EnvelopeCheckResponse => ({
  betterFitChecked: false,
  matchedSignals: [],
  rationale: 'Mild stretch.',
  suggestedClass: '',
  suggestedRationale: '',
  suggestedSlug: '',
  verdict: 'borderline',
  ...over,
});

const reviewAndContinue = async (user: ReturnType<typeof userEvent.setup>) => {
  renderRoute({ initialPath: '/class/009605-lab-ast-1' });
  await user.click(await screen.findByRole('button', { name: 'Review & continue →' }));
};

describe('a borderline fit', () => {
  setupRouteTest();

  it('also suggests a class that may fit better, without routing the manager away', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.post('/api/build/check', () =>
        HttpResponse.json(
          check({
            betterFitChecked: true,
            suggestedClass: 'Budget Analyst 2',
            suggestedRationale: 'Budget work dominates the additions.',
            suggestedSlug: '004767-budget-analyst-2',
          })
        )
      )
    );
    await reviewAndContinue(user);

    const panel = await screen.findByTestId('better-fit');
    expect(panel).toHaveTextContent('may fit Budget Analyst 2 better');
    expect(panel).toHaveTextContent('Budget work dominates the additions.');
    expect(within(panel).getByRole('link', { name: 'Look at Budget Analyst 2 →' })).toHaveAttribute(
      'href',
      '/class/004767-budget-analyst-2'
    );
    // Assembling here is still the normal path, and the search already ran.
    expect(screen.getByRole('button', { name: 'Assemble the JD →' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Look for a better fit' })).not.toBeInTheDocument();
  });

  it('says so when the current class is still the best fit', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.post('/api/build/check', () =>
        HttpResponse.json(check({ betterFitChecked: true, suggestedRationale: 'Typical of this class.' }))
      )
    );
    await reviewAndContinue(user);

    expect(await screen.findByTestId('better-fit')).toHaveTextContent('still the best match');
  });
});

describe('look for a better fit', () => {
  setupRouteTest();

  it('searches on demand for the build as it stands', async () => {
    const user = userEvent.setup();
    let sent: BuildRequest | null = null;
    testServer.use(
      http.post('/api/build/check', () =>
        HttpResponse.json(check({ rationale: 'Fits.', verdict: 'in_envelope' }))
      ),
      http.post('/api/build/better-fit', async ({ request }) => {
        sent = (await request.json()) as BuildRequest;
        return HttpResponse.json({
          rationale: 'The work is mostly greenhouse operations.',
          suggestedClass: 'Greenhouse Tech 2',
          suggestedSlug: '009000-greenhouse-tech-2',
        });
      })
    );
    await reviewAndContinue(user);

    await user.click(await screen.findByRole('button', { name: 'Look for a better fit' }));

    expect(await screen.findByTestId('better-fit')).toHaveTextContent('may fit Greenhouse Tech 2 better');
    expect(sent!.slug).toBe('009605-lab-ast-1');
    expect(sent!.keptResponsibilities.length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Look for a better fit' })).not.toBeInTheDocument();
  });
});

const savedJd = (over: Partial<SavedJd> = {}): SavedJd => ({
  assembled: true,
  authorAdditions: [],
  authoredJdId: 12,
  bargainingUnit: null,
  canPublish: true,
  complianceEdits: [],
  corpusNote: '',
  createdAt: '2026-10-08T00:00:00Z',
  createdBy: 'Sample User',
  department: 'Plant Sciences',
  draftState: null,
  flsaStatus: 'Non-Exempt',
  fromEnvelope: false,
  inCorpus: true,
  jd: {
    conditionsOfEmployment: [],
    education: ['High school diploma.'],
    jobSummary: 'Runs greenhouse experiments.',
    keyResponsibilities: [
      { duties: ['Waters and records plants.'], functionName: 'Greenhouse Operations', pctTime: 100 },
    ],
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
  status: 'ready',
  title: 'Lab Ast 1',
  ucJobCode: '009605',
  unallocatedPct: 0,
  updatedAt: '2026-10-08T00:00:00Z',
  workingTitle: 'Greenhouse Tech',
  ...over,
});

describe('a finished JD', () => {
  setupRouteTest();

  it('downloads as Word from the saved record, beside the PDF', async () => {
    testServer.use(http.get('/api/jds/12', () => HttpResponse.json(savedJd())));
    renderRoute({ initialPath: '/jds/12' });

    const word = await screen.findByRole('link', { name: '⤓ Download Word' });
    expect(word).toHaveAttribute('href', '/api/jds/12/docx');
    expect(word).toHaveAttribute('download');
    expect(screen.getByRole('button', { name: '⤓ Download PDF' })).toBeEnabled();
  });

  it('starts the workforce management justification with the JD attached', async () => {
    testServer.use(
      http.get('/api/jds/12', () => HttpResponse.json(savedJd())),
      http.get('/api/links', () =>
        HttpResponse.json({ wfmUrl: 'https://people.example.edu/wfm/requests/new' })
      )
    );
    renderRoute({ initialPath: '/jds/12' });

    const steps = await screen.findByRole('region', {
      name: 'Take this job description to the workforce management justification',
    });
    const start = await within(steps).findByRole('link', {
      name: 'Start the workforce management justification →',
    });

    // WFM is told where the JD comes from, and the absolute URL to fetch it from.
    const url = new URL(start.getAttribute('href')!);
    expect(url.origin + url.pathname).toBe('https://people.example.edu/wfm/requests/new');
    expect(url.searchParams.get('source')).toBe('jdwriter');
    expect(url.searchParams.get('jd')).toBe(`${window.location.origin}/api/jds/12/handoff`);

    expect(steps).toHaveTextContent('Job description (done)');
    expect(steps).toHaveTextContent('submit it all as one complete package');
  });

  it('offers the JD as Markdown and JSON until the workforce management tool is live', async () => {
    testServer.use(http.get('/api/jds/12', () => HttpResponse.json(savedJd())));
    renderRoute({ initialPath: '/jds/12' });

    const steps = await screen.findByRole('region', {
      name: 'Take this job description to the workforce management justification',
    });
    expect(await within(steps).findByText(/not available yet/)).toBeInTheDocument();
    expect(within(steps).queryByRole('link', { name: /Start the workforce/ })).not.toBeInTheDocument();
    expect(within(steps).getByRole('link', { name: 'Markdown' })).toHaveAttribute('href', '/api/jds/12/markdown');
    expect(within(steps).getByRole('link', { name: 'JSON' })).toHaveAttribute('href', '/api/jds/12/handoff');
  });

  it('offers neither the hand-off nor Word for a JD that is not ready', async () => {
    testServer.use(
      http.get('/api/jds/12', () =>
        HttpResponse.json(savedJd({ canPublish: false, status: 'draft', unallocatedPct: 10 }))
      )
    );
    renderRoute({ initialPath: '/jds/12' });

    await screen.findByText(/not publishable/);
    expect(screen.queryByRole('link', { name: '⤓ Download Word' })).not.toBeInTheDocument();
    expect(screen.queryByText('Next steps')).not.toBeInTheDocument();
  });
});
