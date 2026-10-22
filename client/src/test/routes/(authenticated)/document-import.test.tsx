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
          discarded: [{ fileName: 'empty.docx', reason: 'Incomplete, so not added: It has no key responsibilities.' }],
          refiled: [],
          reparsed: 12,
          restored: [{ fileName: 'old-style.docx', reason: 'filed under 004973' }],
          unrecognized: 0,
        })
      )
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    await user.upload(await screen.findByLabelText('Add job description documents'), new File(['a'], 'jd.docx'));
    expect(await screen.findByText('LAB AST 1 (009605) · parsed')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Re-read imported documents' }));
    expect(await screen.findByTestId('reparse-summary')).toHaveTextContent(
      'Re-read 12 · discarded 1 incomplete · 1 restored that were refused for listing no duties'
    );
    vi.restoreAllMocks();
  });

  it('says plainly when the campus security filter blocks a file before it reaches JDWriter', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.post(
        '/api/admin/uploads/documents',
        () =>
          new HttpResponse('<!DOCTYPE html><title>Attention Required! | Cloudflare</title><h1>Sorry, you have been blocked</h1>', {
            headers: { 'Content-Type': 'text/html' },
            status: 403,
          })
      )
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    await user.upload(await screen.findByLabelText('Add job description documents'), new File(['a'], 'jd.docx'));

    expect(await screen.findByText(/security filter blocked this before it reached JDWriter/)).toBeInTheDocument();
    expect(screen.queryByText(/HTTP 403/)).not.toBeInTheDocument();
  });

  it('offers to paste a blocked file’s text instead, and adds the pasted JD', async () => {
    const user = userEvent.setup();
    const bodies: string[] = [];
    testServer.use(
      http.post('/api/admin/uploads/documents', async ({ request }) => {
        const body = await request.text();
        bodies.push(body);
        // jsdom's FormData drops file names and contents on the way to MSW; the type survives.
        return body.includes('Content-Type: text/plain')
          ? HttpResponse.json(outcome({ readBy: 'ai', title: 'ACCESS AND ENGMT MGR 1', ucJobCode: '005604' }))
          : new HttpResponse('<!DOCTYPE html><title>Attention Required! | Cloudflare</title>', {
              headers: { 'Content-Type': 'text/html' },
              status: 403,
            });
      })
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    await user.upload(await screen.findByLabelText('Add job description documents'), new File(['a'], 'jd.docx'));
    expect(await screen.findByText(/blocked a file before it reached JDWriter/)).toBeInTheDocument();
    const box = screen.getByRole('textbox', { name: 'Pasted job description' });
    expect(box).toBeVisible();

    await user.type(box, 'ACCESS AND ENGMT MGR 1. Leads the access program.');
    await user.click(screen.getByRole('button', { name: 'Add pasted job description' }));
    expect(await screen.findByText('ACCESS AND ENGMT MGR 1 (005604) · read by AI (unrecognized layout)')).toBeInTheDocument();
    expect(bodies).toHaveLength(2);
    expect(bodies[1]).toContain('Content-Type: text/plain');
    expect(box).toHaveValue('');
  });

  it('puts the file in blob storage first when the server stages uploads, and sends only its name', async () => {
    const user = userEvent.setup();
    const blobUrl = 'https://stjdwritertest.blob.core.windows.net/uploads/0123456789abcdef/0123456789abcdef0123456789abcdef';
    const put: { blobType: string | null; body: string; type: string | null }[] = [];
    let sent = '';
    testServer.use(
      http.post('/api/uploads/stage', async ({ request }) => {
        expect(await request.json()).toEqual({ count: 1 });
        return HttpResponse.json({ slots: [{ blob: '0123456789abcdef/0123456789abcdef0123456789abcdef', url: `${blobUrl}?sig=x` }] });
      }),
      http.put(blobUrl, async ({ request }) => {
        put.push({
          blobType: request.headers.get('x-ms-blob-type'),
          body: await request.text(),
          type: request.headers.get('content-type'),
        });
        return new HttpResponse(null, { status: 201 });
      }),
      http.post('/api/admin/uploads/documents', async ({ request }) => {
        sent = await request.text();
        return HttpResponse.json(outcome({ readBy: 'parsed', title: 'LAB AST 1', ucJobCode: '009605' }));
      })
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    const file = new File(['the document itself'], 'Lab Ast 1 (40000001).docx', {
      type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    });
    await user.upload(await screen.findByLabelText('Add job description documents'), file);

    expect(await screen.findByText('LAB AST 1 (009605) · parsed')).toBeInTheDocument();
    expect(put).toEqual([
      {
        blobType: 'BlockBlob',
        body: 'the document itself',
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      },
    ]);
    // The upload request names the blob; the file's bytes went to storage, not through the front door.
    expect(sent).toContain('name="staged"');
    expect(sent).toContain('0123456789abcdef/0123456789abcdef0123456789abcdef');
    expect(sent).not.toContain('the document itself');
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
