import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { testServer } from '@/test/mswUtils.ts';
import type { ClassifyStartRequest, SavedJd } from '@/lib/contracts.ts';
import { http, HttpResponse } from 'msw';

describe('classify', () => {
  setupRouteTest();

  it('classifies pasted text and shows the coverage split', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/classify' });

    const textarea = await waitFor(() => screen.getByLabelText('Job description text'));
    await user.type(textarea, 'Analyses study datasets and reports findings.');
    await user.click(screen.getByRole('button', { name: 'Classify this description' }));

    await waitFor(() => {
      expect(screen.getByText('Clear match')).toBeInTheDocument();
    });
    // One per match card. Alternatives are compared on the SAME axes as the top match —
    // coverage, level fit, in/out functions — rather than merely being listed, so this
    // label legitimately appears more than once.
    expect(
      screen.getAllByText('Description covered by this class').length
    ).toBeGreaterThan(1);
    expect(screen.getByText('Other classes considered')).toBeInTheDocument();
    // The person is told their submission was kept, and where.
    expect(screen.getByTestId('filed-under')).toHaveTextContent(/saved to the corpus for job code 006256/);
  });

  it('reads a dropped file into the box WITHOUT classifying it', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/classify' });

    await waitFor(() => screen.getByLabelText('Job description text'));

    // Extraction is free; classification costs a model call. Uploading must fill the box
    // and stop, so a mangled PDF can be corrected before it costs anything.
    const file = new File(['pd'], 'PD.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    await user.upload(screen.getByLabelText('Upload a job description file'), file);

    await waitFor(() => {
      expect(
        screen.getByText('PD_Evaluation_Analyst_final.xlsx')
      ).toBeInTheDocument();
    });
    expect(screen.queryByText('Clear match')).not.toBeInTheDocument();
  });

  it('names an un-ingested proposal a corpus gap rather than a disagreement', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/classify' });

    await waitFor(() => screen.getByLabelText('Job description text'));

    const file = new File(['pd'], 'PD.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    await user.upload(screen.getByLabelText('Upload a job description file'), file);
    await waitFor(() =>
      screen.getByText('PD_Evaluation_Analyst_final.xlsx')
    );

    await user.click(screen.getByRole('button', { name: 'Classify this description' }));

    // The classifier can only return classes it has ingested, so a proposal that was never
    // ingested could not have been returned. Calling that a disagreement would tell an
    // analyst their unit is wrong when the truth is our corpus is incomplete.
    await waitFor(() => {
      expect(screen.getByText('Corpus gap — not a disagreement')).toBeInTheDocument();
    });
    expect(screen.queryByText('Differs from the unit')).not.toBeInTheDocument();
    expect(
      screen.getByText(/could not have been returned by the classifier/)
    ).toBeInTheDocument();
  });

  it('starts a JD from a recommended class with the description merged in', async () => {
    const user = userEvent.setup();
    let sent: ClassifyStartRequest | null = null;
    let openedDraft = false;
    testServer.use(
      http.post('/api/classify/start-jd', async ({ request }) => {
        sent = (await request.json()) as ClassifyStartRequest;
        return HttpResponse.json({
          authoredJdId: 77,
          carriedDuties: 1,
          droppedFunctions: 0,
          keptFunctions: 2,
          matchedDuties: 0,
          outside: [],
          slug: sent.slug,
          title: 'Recommended Class',
        });
      }),
      http.get('/api/jds/77', () => {
        openedDraft = true;
        return HttpResponse.json(draft(sent!.slug));
      })
    );
    renderRoute({ initialPath: '/classify' });

    await user.type(await screen.findByLabelText('Job description text'), 'Analyses study datasets.');
    await user.click(screen.getByRole('button', { name: 'Classify this description' }));
    const starts = await screen.findAllByRole('button', { name: 'Start a JD from this class →' });
    expect(starts.length).toBeGreaterThan(1); // the recommendation and each alternative

    await user.click(starts[0]);

    // The description as the classifier read it goes back unchanged, so it is not read twice.
    await waitFor(() => expect(openedDraft).toBe(true));
    expect(sent!.distilled.workingTitle).toBe('Evaluation Analyst');
    expect(sent!.distilled.functions[0]).toEqual({
      duties: ['Analyzes program outcome data.'],
      name: 'ANALYSIS',
      pctTime: 90,
    });
    expect(sent!.slug).toBeTruthy();
  });
});

const draft = (slug: string): SavedJd => ({
  assembled: false,
  authorAdditions: [],
  authoredJdId: 77,
  bargainingUnit: null,
  canPublish: false,
  complianceEdits: [],
  corpusNote: 'Draft — not assembled yet, so not in the corpus.',
  createdAt: '2026-10-08T00:00:00Z',
  createdBy: 'Sample User',
  department: '',
  draftState: null,
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
  slug,
  status: 'draft',
  title: 'Recommended Class',
  ucJobCode: '000000',
  unallocatedPct: 0,
  updatedAt: '2026-10-08T00:00:00Z',
  workingTitle: 'Evaluation Analyst',
});
