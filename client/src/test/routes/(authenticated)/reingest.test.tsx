import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import type { UploadedClass } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const pendingClass = (over: Partial<UploadedClass>): UploadedClass => ({
  code: '004724',
  corpusJds: 2,
  existingSlug: null,
  hasManualEnvelope: false,
  newAuthored: 0,
  newClassified: 0,
  newFiles: 0,
  newImported: 2,
  replacesStarter: false,
  title: 'Farm Laborer',
  ...over,
});

describe('re-ingesting classes with new JDs', () => {
  setupRouteTest();

  it('re-ingests waiting classes one at a time from the Descriptions page', async () => {
    const user = userEvent.setup();
    const ingested: string[] = [];
    let remaining = [
      pendingClass({}),
      pendingClass({ code: '009605', corpusJds: 157, existingSlug: '009605-lab-ast-1', newImported: 3, title: 'Lab Ast 1' }),
    ];
    testServer.use(
      http.get('/api/admin/uploads/pending', () => HttpResponse.json({ classes: remaining })),
      http.post('/api/admin/uploads/ingest', async ({ request }) => {
        const { code } = (await request.json()) as { code: string };
        ingested.push(code);
        remaining = remaining.filter((c) => c.code !== code);
        return HttpResponse.json({ ok: true });
      })
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    await screen.findByText(/3 new JDs · 157 JDs in all/);
    expect(screen.getByText(/2 new JDs · new class/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Re-ingest 2' }));

    await waitFor(() => expect(ingested).toEqual(['004724', '009605']));
  });

  it('warns before replacing a hand-edited envelope', async () => {
    testServer.use(
      http.get('/api/admin/uploads/pending', () =>
        HttpResponse.json({
          classes: [
            pendingClass({ code: '009605', existingSlug: '009605-lab-ast-1', hasManualEnvelope: true, newAuthored: 2, newClassified: 1, newImported: 0, title: 'Lab Ast 1' }),
          ],
        })
      )
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    await screen.findByText(/2 written in the app · 1 from Classify/);
    expect(screen.getByText(/edited by hand — re-ingesting replaces those edits/)).toBeInTheDocument();
  });

  it('says when real JDs replace a class bootstrapped from its standard', async () => {
    testServer.use(
      http.get('/api/admin/uploads/pending', () =>
        HttpResponse.json({
          classes: [pendingClass({ code: '009605', existingSlug: '009605-laboratory-assistant-1', newImported: 4, replacesStarter: true, title: 'Laboratory Assistant 1' })],
        })
      )
    );
    renderRoute({ initialPath: '/backend/descriptions' });

    await screen.findByText(/4 new JDs · replaces its starter envelope/);
  });

  it('shows waiting classes atop the Envelopes page, and re-ingests one envelope from its row', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    const ingested: string[] = [];
    testServer.use(
      http.get('/api/admin/uploads/pending', () =>
        HttpResponse.json({
          classes: [pendingClass({ code: '009605', corpusJds: 157, existingSlug: '009605-lab-ast-1', newImported: 3, title: 'Lab Ast 1' })],
        })
      ),
      http.post('/api/admin/uploads/ingest', async ({ request }) => {
        ingested.push(((await request.json()) as { code: string }).code);
        return HttpResponse.json({ ok: true });
      })
    );
    renderRoute({ initialPath: '/backend' });

    expect(await screen.findByText('Waiting to be re-ingested')).toBeInTheDocument();
    const badge = await screen.findByText('3 new JDs to re-ingest');
    const row = badge.closest('li') as HTMLElement;
    await user.click(within(row).getByRole('button', { name: 'Re-ingest' }));

    await waitFor(() => expect(ingested).toEqual(['009605']));
    expect(await within(row).findByText('Re-ingested from all of its JDs.')).toBeInTheDocument();
  });

  it('keeps the Envelopes page clear when nothing is waiting', async () => {
    useContractHandlers();
    renderRoute({ initialPath: '/backend' });

    await screen.findByRole('heading', { level: 1, name: 'Envelopes' });
    await waitFor(() => expect(screen.queryByText('Waiting to be re-ingested')).not.toBeInTheDocument());
  });

  it('uploads standards workbooks on the Standards page and reports what merged', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.post('/api/admin/standards/upload', () =>
        HttpResponse.json({
          added: 2,
          files: [
            { error: null, fileName: 'a.xlsx', result: 'added', standards: 3 },
            { error: 'Not a Job Builder export — expected an .xlsx workbook.', fileName: 'b.txt', result: 'failed', standards: 0 },
          ],
          linkedCount: 19,
          total: 216,
          totalClasses: 65,
          uncodedSample: [],
          updated: 1,
        })
      )
    );
    renderRoute({ initialPath: '/backend/standards' });

    await user.upload(await screen.findByLabelText('Upload job standards workbooks'), [new File(['x'], 'a.xlsx')]);

    const summary = await screen.findByTestId('standards-upload-summary');
    expect(summary).toHaveTextContent('2 added');
    expect(summary).toHaveTextContent('216 standards in total');
    expect(screen.getByText(/b\.txt: Not a Job Builder export/)).toBeInTheDocument();
  });
});
