import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import type { CtListingPlan, JobRow } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const plan = (applied: boolean): CtListingPlan => ({
  addedCodeCount: 200,
  addedCodes: ['004638 Business Development Professional 3'],
  affectedClasses: [{ slug: '000621-employment-manager-1', title: 'Employment Manager 1', why: '“Equal Employment Opportunity Manager 1” moved away to 000618' }],
  applied,
  changed: 0,
  familiesChanged: 64,
  familiesFilled: 34,
  gradesChanged: ['000680 Audit Mgr 2: Grade 27 → Grade 26'],
  gradesFilled: 72,
  newToListing: 1967,
  opOnly: 115,
  rows: 1967,
  standardMoves: [{ fromCode: '000621', fromTitle: 'Employment Mgr 1', standard: 'Equal Employment Opportunity Manager 1', toCode: '000618', toTitle: 'Equal Employment Opportunity Manager 1' }],
  standardSuggestions: 50,
  supervisionChanges: ['005637 Health Sciences Adm 4 — level M4: can now supervise'],
  unchanged: 0,
});

describe('the CT title code listing', () => {
  setupRouteTest();

  it('previews an import, then imports the same report, listing the classes to rebuild', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    const calls: string[] = [];
    testServer.use(
      http.post('/api/admin/codes/ct-listing/preview', () => {
        calls.push('preview');
        return HttpResponse.json(plan(false));
      }),
      http.post('/api/admin/codes/ct-listing/apply', () => {
        calls.push('apply');
        return HttpResponse.json(plan(true));
      })
    );
    renderRoute({ initialPath: '/backend/codes' });

    expect(await screen.findByTestId('ct-status')).toHaveTextContent('No listing imported yet.');
    await user.upload(screen.getByLabelText('Choose a CT title code listing'), new File(['%PDF'], 'listing.pdf', { type: 'application/pdf' }));

    const preview = await screen.findByTestId('ct-plan');
    expect(preview).toHaveTextContent('Nothing has changed yet');
    expect(preview).toHaveTextContent('200 codes added to the reference');
    expect(preview).toHaveTextContent('1 standards to move to the code the listing gives their title · 50 suggested in the match review');
    expect(within(screen.getByTestId('ct-affected')).getByRole('link', { name: 'Employment Manager 1' })).toHaveAttribute(
      'href',
      '/backend/edit/000621-employment-manager-1'
    );

    await user.click(screen.getByRole('button', { name: 'Import the listing' }));
    await waitFor(() => expect(screen.getByTestId('ct-plan')).toHaveTextContent('Done.'));
    expect(calls).toEqual(['preview', 'apply']);
  });

  it('shows what the listing says of a code in its pop-up', async () => {
    useContractHandlers();
    const job: JobRow = {
      active: true,
      class: null,
      code: '005637',
      ct: {
        family: 'General Administration', flsa: 'E', function: 'Health Sciences Admin Ops', grade: '31', lastUpdated: '2022-08-09',
        level: 'M4', opOnly: false, origin: 'MC', personnelProgram: 'MSP', status: 'A', supervisory: true, title: 'Health Sciences Administrator 4',
      },
      family: 'General Administration',
      grade: 'Grade 31',
      onPayroll: true,
      site: null,
      standard: null,
      supersededBy: null,
      title: 'Health Sciences Adm 4',
    };
    testServer.use(http.get('/api/admin/jobs', () => HttpResponse.json({ jobs: [job], scope: 'all' })));
    renderRoute({ initialPath: '/backend/codes?code=005637' });

    const facts = await within(await screen.findByRole('dialog')).findByTestId('ct-facts');
    expect(facts).toHaveTextContent('Health Sciences Administrator 4');
    expect(facts).toHaveTextContent('level M4 — supervises');
    expect(facts).toHaveTextContent('Medical Center');
  });
});
