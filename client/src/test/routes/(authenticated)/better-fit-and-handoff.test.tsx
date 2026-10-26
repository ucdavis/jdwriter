import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { fillDepartment } from '@/test/build.ts';
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
  await fillDepartment(user);
  await user.click(await screen.findByRole('button', { name: 'Check my duties' }));
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
    // Continuing is still the normal path, and the search already ran.
    expect(screen.getByRole('button', { name: 'Continue to requirements →' })).toBeEnabled();
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
        // Doesn't fit, and the check named no better class: the one case the search is offered.
        HttpResponse.json(check({ rationale: 'Budget work is outside this class.', verdict: 'out_of_envelope' }))
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

  it('tells the author to attach the JD to an AggieService ticket for DISSC, and what comes next', async () => {
    testServer.use(http.get('/api/jds/12', () => HttpResponse.json(savedJd())));
    renderRoute({ initialPath: '/jds/12' });

    const steps = await screen.findByRole('region', { name: 'Send this job description to DISSC' });
    expect(within(steps).getByRole('link', { name: '⤓ Download the Word document' })).toHaveAttribute('href', '/api/jds/12/docx');
    expect(within(steps).getByRole('link', { name: 'Start the Vacancy Request Form →' })).toHaveAttribute('href', '/vrf?jd=12');
    expect(within(steps).getByRole('link', { name: 'AggieService' })).toHaveAttribute('href', 'https://aggieservice.ucdavis.edu/s/');
    expect(within(steps).getByRole('link', { name: 'disscrequest@ucdavis.edu' })).toHaveAttribute('href', 'mailto:disscrequest@ucdavis.edu');
    expect(steps).toHaveTextContent('a vacancy request form, then classification by Compensation, then recruitment');
    expect(steps).toHaveTextContent('Your DISSC representative will reach out through AggieService');
    expect(steps).not.toHaveTextContent('Workforce Management');
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
