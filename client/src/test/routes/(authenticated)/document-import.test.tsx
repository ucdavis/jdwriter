import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { UploadOutcome } from '@/lib/contracts.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const outcome = (over: Partial<UploadOutcome>): UploadOutcome => ({
  error: null,
  fileName: '',
  result: 'added',
  title: null,
  ucJobCode: null,
  ...over,
});

describe('add JDs from other units', () => {
  setupRouteTest();

  it('reads a folder one document at a time, skipping files that are not documents', async () => {
    const user = userEvent.setup();
    // jsdom's File reaches MSW as a part named "blob", so verdicts are matched by order.
    const verdicts = [
      outcome({ title: 'LAB AST 1', ucJobCode: '009605' }),
      outcome({ result: 'duplicate' }),
      outcome({ error: 'Its title “Chief Plant Whisperer” doesn’t match a UC job title.', result: 'failed' }),
    ];
    let requests = 0;
    let inFlight = 0;
    let maxInFlight = 0;
    testServer.use(
      http.post('/api/admin/uploads/documents', async () => {
        inFlight += 1;
        maxInFlight = Math.max(maxInFlight, inFlight);
        const verdict = verdicts[requests];
        requests += 1;
        // Let any concurrent request start before this one answers.
        await Promise.resolve();
        inFlight -= 1;
        return HttpResponse.json(verdict);
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    const input = await screen.findByLabelText('Add a folder of job description documents');
    await user.upload(input, [
      new File(['a'], 'greenhouse.docx'),
      new File(['b'], 'greenhouse copy.docx'),
      new File(['c'], 'whisperer.pdf'),
      new File(['d'], '.DS_Store'),
      new File(['e'], '~$greenhouse.docx'),
      new File(['f'], 'notes.png'),
    ]);

    expect(await screen.findByTestId('document-summary')).toHaveTextContent(
      '1 added · 1 already added · 1 not usable · 3 other files skipped'
    );
    expect(requests).toBe(3);
    expect(maxInFlight).toBe(1);

    const rows = screen.getAllByRole('listitem').filter((li) => /greenhouse|whisperer/.test(li.textContent ?? ''));
    expect(within(rows[0]).getByText('LAB AST 1 (009605)')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Already added')).toBeInTheDocument();
    expect(within(rows[2]).getByText(/doesn’t match a UC job title/)).toBeInTheDocument();
  });

  it('shows imported JDs in the rebuild queue', async () => {
    testServer.use(
      http.get('/api/admin/uploads/pending', () =>
        HttpResponse.json({
          classes: [
            {
              code: '009605',
              corpusJds: 12,
              existingSlug: '009605-lab-ast-1',
              hasManualEnvelope: false,
              newAuthored: 0,
              newClassified: 0,
              newFiles: 0,
              newImported: 4,
              replacesStarter: false,
              title: 'Lab Ast 1',
            },
          ],
        })
      )
    );
    renderRoute({ initialPath: '/backend/corpus' });

    expect(await screen.findByText(/4 from other units · refreshes a class with 12 JDs/)).toBeInTheDocument();
  });
});
