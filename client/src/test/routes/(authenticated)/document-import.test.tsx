import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { UploadOutcome } from '@/lib/contracts.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';

const outcome = (over: Partial<UploadOutcome>): UploadOutcome => ({
  error: null,
  fileName: '',
  result: 'added',
  title: null,
  ucJobCode: null,
  ...over,
});

describe('add job descriptions', () => {
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
    renderRoute({ initialPath: '/backend/descriptions' });

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
    expect(within(rows[0]).getByText(/^LAB AST 1 \(009605\)/)).toBeInTheDocument();
    expect(within(rows[1]).getByText('Already added')).toBeInTheDocument();
    expect(within(rows[2]).getByText(/doesn’t match a UC job title/)).toBeInTheDocument();
  });

  it('says a document was parsed, and re-reads earlier imports on request', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    testServer.use(
      http.post('/api/admin/uploads/documents', () =>
        HttpResponse.json(outcome({ readBy: 'parsed', title: 'LAB AST 1', ucJobCode: '009605' }))
      ),
      http.post('/api/admin/uploads/documents/reparse', () =>
        HttpResponse.json({
          discarded: [{ fileName: 'posting.docx', reason: 'Incomplete, so not added: It lists no duties.' }],
          refiled: [],
          reparsed: 12,
          unrecognized: 0,
        })
      )
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    await user.upload(await screen.findByLabelText('Add job description documents'), new File(['a'], 'jd.docx'));
    expect(await screen.findByText('LAB AST 1 (009605) · parsed')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Re-read imported documents' }));
    expect(await screen.findByTestId('reparse-summary')).toHaveTextContent('Re-read 12 · discarded 1 incomplete');
    vi.restoreAllMocks();
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
    renderRoute({ initialPath: '/backend/descriptions' });

    expect(await screen.findByText(/4 new JDs · 12 JDs in all/)).toBeInTheDocument();
  });
});
