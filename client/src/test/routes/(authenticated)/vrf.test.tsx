import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { SavedJd } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';

// Synthetic: the shape of a finished JD, none of a real one's content.
const finished = (over: Partial<SavedJd> = {}): SavedJd => ({
  assembled: true,
  authorAdditions: [],
  authoredJdId: 12,
  bargainingUnit: 'TX',
  canPublish: true,
  complianceEdits: [],
  corpusNote: '',
  createdAt: '2026-10-26T00:00:00Z',
  createdBy: 'Sample User',
  department: 'Plant Sciences',
  draftState: null,
  flsaStatus: 'Non-Exempt',
  fromEnvelope: true,
  inCorpus: false,
  jd: {
    conditionsOfEmployment: [],
    education: [],
    jobSummary: 'Runs the teaching greenhouse.',
    keyResponsibilities: [{ duties: ['Waters plants.'], functionName: 'Plant care', pctTime: 100 }],
    licensesCertifications: [],
    minKSA: [],
    physicalRequirements: [],
    prefKSA: [],
    workEnvironment: [],
    workExperience: [],
  },
  notes: '',
  salaryGrade: 'STEPS',
  slug: '009605-lab-ast-1',
  status: 'ready',
  supervises: true,
  supervisesCount: 2,
  title: 'Lab Ast 1',
  ucJobCode: '009605',
  unallocatedPct: 0,
  updatedAt: '2026-10-26T00:00:00Z',
  workingTitle: 'Greenhouse Assistant',
  ...over,
});

const field = (name: RegExp | string) => screen.getByRole('textbox', { name });

describe('the vacancy request form', () => {
  setupRouteTest();
  afterEach(() => vi.restoreAllMocks());

  it('is a tab for everyone, and starts from a finished JD with the position filled in', async () => {
    testServer.use(http.get('/api/jds/12', () => HttpResponse.json(finished())));
    renderRoute({ initialPath: '/vrf?jd=12' });

    expect(await screen.findByRole('link', { name: 'Vacancy request' })).toHaveAttribute('href', '/vrf');
    expect(await screen.findByTestId('vrf-filled')).toHaveTextContent('Filled in from your job description');
    expect(field(/^Payroll title/)).toHaveValue('Lab Ast 1');
    expect(field(/^Job code/)).toHaveValue('009605');
    expect(field(/^Working title/)).toHaveValue('Greenhouse Assistant');
    expect(field(/^Bargaining unit/)).toHaveValue('TX');
    expect(field(/^Salary grade/)).toHaveValue('STEPS');
    expect(field(/^Department name/)).toHaveValue('Plant Sciences');
    expect(field(/^Division/)).toHaveValue('College of Agricultural and Environmental Sciences');
    await waitFor(() => expect(document.title).toBe('Vacancy request · JDWriter'));
  });

  it('fills in from a dropped JD document: its title and code, and the class’s unit and grade', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.post('/api/classify/extract', () =>
        HttpResponse.json({ filename: 'jd.docx', kind: 'docx', proposed: { code: '009605', ingested: true, title: 'LAB AST 1' }, text: 'x' })
      )
    );
    renderRoute({ initialPath: '/vrf' });

    await user.upload(await screen.findByLabelText('Fill in from a JD document'), new File(['x'], 'jd.docx'));

    expect(await screen.findByTestId('vrf-filled')).toHaveTextContent('Filled in from “jd.docx”');
    expect(field(/^Payroll title/)).toHaveValue('LAB AST 1');
    expect(field(/^Job code/)).toHaveValue('009605');
    expect(field(/^Bargaining unit/)).toHaveValue('TX-Technical Professionals');
    expect(field(/^Salary grade/)).toHaveValue('STEPS');
  });

  it('fills the salary range from a Career Tracks grade, once, and leaves a represented title to HR', async () => {
    const user = userEvent.setup();
    const asked: string[] = [];
    testServer.use(
      http.get('/api/jds/12', () => HttpResponse.json(finished({ bargainingUnit: '99', salaryGrade: 'Grade 20' }))),
      http.get('/api/vrf/salary-range', ({ request }) => {
        asked.push(new URL(request.url).searchParams.get('grade') ?? '');
        return HttpResponse.json({
          range: { effectiveDate: '2026-07-01', grade: 20, maximum: 123_000, midpoint: 100_000, minimum: 77_000, note: 'Geo Diff 112% for Non-Represented Staff', scale: 'Career Tracks' },
        });
      })
    );
    renderRoute({ initialPath: '/vrf?jd=12' });
    await screen.findByTestId('vrf-filled');
    await user.click(screen.getByRole('button', { name: /^3\. Budget/ }));

    await waitFor(() => expect(field(/^Minimum \(annual\)/)).toHaveValue('$77,000'));
    expect(field(/^Midpoint \(annual\)/)).toHaveValue('$100,000');
    expect(field(/^Maximum \(annual\)/)).toHaveValue('$123,000');
    expect(screen.getByTestId('salary-note')).toHaveTextContent('From the Career Tracks ranges for Grade 20, effective July 1, 2026 (Geo Diff 112%). HR confirms the final range.');
    expect(asked).toContain('Grade 20');

    // Cleared on purpose, it stays cleared.
    for (const name of [/^Minimum \(annual\)/, /^Midpoint \(annual\)/, /^Maximum \(annual\)/]) {
      await user.clear(field(name));
    }
    await user.click(screen.getByRole('button', { name: /^2\. The request/ }));
    await user.click(screen.getByRole('button', { name: /^3\. Budget/ }));
    expect(field(/^Minimum \(annual\)/)).toHaveValue('');
  });

  it('says a represented title is on its union’s step scale, and asks for no range', async () => {
    const user = userEvent.setup();
    let asked = false;
    testServer.use(
      http.get('/api/jds/12', () => HttpResponse.json(finished())),
      http.get('/api/vrf/salary-range', () => {
        asked = true;
        return HttpResponse.json({ range: null });
      })
    );
    renderRoute({ initialPath: '/vrf?jd=12' });
    await screen.findByTestId('vrf-filled');
    await user.click(screen.getByRole('button', { name: /^3\. Budget/ }));

    expect(screen.getByTestId('salary-note')).toHaveTextContent('Represented titles are paid on their union’s step scale — HR fills this in.');
    expect(field(/^Minimum \(annual\)/)).toHaveValue('');
    expect(asked).toBe(false);
  });

  it('asks only what applies: the vacated date for a backfill, the funding question for a contract', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/vrf' });
    await user.click(await screen.findByRole('button', { name: /^2\. The request/ }));
    const action = screen.getByRole('combobox', { name: /^Action requested/ });

    expect(screen.queryByLabelText(/Date vacated/)).not.toBeInTheDocument();
    await user.selectOptions(action, 'Backfill Position');
    // A date picker, not a box to type a date into.
    expect(screen.getByLabelText(/Date vacated/)).toHaveAttribute('type', 'date');

    await user.selectOptions(action, 'New Contract Appt');
    await user.click(screen.getByRole('button', { name: /^3\. Budget/ }));
    expect(screen.getByRole('combobox', { name: /funding for this contract secured/ })).toBeInTheDocument();

    // The budget source is a dropdown like the rest; Other opens a box to type it.
    const source = screen.getByRole('combobox', { name: /^Budget source/ });
    expect(screen.queryByRole('textbox', { name: /Which source/ })).not.toBeInTheDocument();
    await user.selectOptions(source, 'Other');
    expect(field(/Which source/)).toBeInTheDocument();
  });

  it('offers the magic wand once a few words are written, drafts the paragraph, and can undo it', async () => {
    const user = userEvent.setup();
    let sent: Record<string, unknown> | null = null;
    testServer.use(
      http.post('/api/vrf/narrative', async ({ request }) => {
        sent = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ paragraph: 'The greenhouse trials depend on this role being filled.' });
      })
    );
    renderRoute({ initialPath: '/vrf' });
    await user.click(await screen.findByRole('button', { name: /^4\. Why it’s needed/ }));
    const box = field(/^Critical Role/);

    await user.type(box, 'trials');
    expect(screen.queryByRole('button', { name: 'Draft the Critical Role paragraph from my notes' })).not.toBeInTheDocument();
    await user.type(box, ' depend on it');
    await user.click(screen.getByRole('button', { name: 'Draft the Critical Role paragraph from my notes' }));

    await waitFor(() => expect(box).toHaveValue('The greenhouse trials depend on this role being filled.'));
    expect(sent).toMatchObject({ field: 'criticalRole', notes: 'trials depend on it' });
    expect(Object.keys((sent as unknown as { context: object }).context)).not.toContain('positionNumber');
    expect(screen.getByText(/Drafted by AI from your notes/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Undo' }));
    expect(box).toHaveValue('trials depend on it');
  });

  it('lists what is still required, and downloads the Word file with what is on screen', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    testServer.use(
      http.get('/api/jds/12', () => HttpResponse.json(finished())),
      http.post('/api/vrf/docx', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(new Blob(['docx']), {
          headers: { 'Content-Disposition': 'attachment; filename="VRF - Greenhouse Assistant.docx"' },
        });
      })
    );
    const createUrl = vi.fn(() => 'blob:vrf');
    Object.assign(URL, { createObjectURL: createUrl, revokeObjectURL: vi.fn() });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    renderRoute({ initialPath: '/vrf?jd=12' });
    await screen.findByTestId('vrf-filled');

    await user.click(screen.getByRole('button', { name: /^6\. Review & download/ }));
    const missing = screen.getByTestId('vrf-missing');
    expect(within(missing).getByRole('button', { name: 'Action requested' })).toBeInTheDocument();
    expect(within(missing).queryByRole('button', { name: 'Payroll title' })).not.toBeInTheDocument();
    expect(screen.getByTestId('vrf-attachments')).toHaveTextContent('the job description and an org chart');

    await user.click(screen.getByRole('button', { name: '⤓ Download the Vacancy Request Form' }));
    await waitFor(() => expect(click).toHaveBeenCalled());
    expect(body).toMatchObject({ hiringManagerName: 'Mock Admin', jobCode: '009605', workingTitle: 'Greenhouse Assistant' });
    expect(createUrl).toHaveBeenCalled();

    await user.click(within(missing).getByRole('button', { name: 'Action requested' }));
    expect(screen.getByRole('combobox', { name: /^Action requested/ })).toBeInTheDocument();
  });
});
