import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { UploadedClass } from '@/lib/contracts.ts';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const pendingClass = (over: Partial<UploadedClass>): UploadedClass => ({
  code: '004724',
  corpusJds: 0,
  existingSlug: null,
  newFiles: 2,
  title: 'Farm Laborer',
  ...over,
});

describe('upload JDs', () => {
  setupRouteTest();

  it('uploads files and reports each one', async () => {
    const user = userEvent.setup();
    let contentType: string | null = null;
    let parts = 0;
    testServer.use(
      http.post('/api/admin/uploads', async ({ request }) => {
        contentType = request.headers.get('Content-Type');
        // File names are not asserted: jsdom's File is not recognized by Node's multipart
        // encoder, which names every part "blob". Real browsers keep the name.
        parts = ((await request.text()).match(/name="files"/g) ?? []).length;
        return HttpResponse.json({
          files: [
            { error: null, fileName: 'a.html', result: 'added', title: 'FARM LABORER', ucJobCode: '004724' },
            { error: null, fileName: 'b.html', result: 'duplicate', title: null, ucJobCode: null },
            { error: 'Not an HRTMS export — expected an .html file.', fileName: 'c.txt', result: 'failed', title: null, ucJobCode: null },
          ],
        });
      })
    );
    renderRoute({ initialPath: '/backend' });

    const input = await screen.findByLabelText('Upload HRTMS export files');
    await user.upload(input, [
      new File(['<html/>'], 'a.html', { type: 'text/html' }),
      new File(['<html/>'], 'b.html', { type: 'text/html' }),
    ]);

    expect(await screen.findByTestId('upload-summary')).toHaveTextContent(
      '1 added · 1 already uploaded · 1 not usable'
    );
    expect(screen.getByText(/c\.txt: Not an HRTMS export/)).toBeInTheDocument();
    // A real multipart body arrived: the Content-Type was left for the browser to set.
    expect(contentType).toMatch(/^multipart\/form-data; boundary=/);
    expect(parts).toBe(2);
  });

  it('ingests pending classes one at a time, saying which are new and which refresh', async () => {
    const user = userEvent.setup();
    const ingested: string[] = [];
    let remaining = [
      pendingClass({}),
      pendingClass({ code: '009605', corpusJds: 154, existingSlug: '009605-lab-ast-1', newFiles: 3, title: 'Lab Ast 1' }),
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
    renderRoute({ initialPath: '/backend' });

    // Wait for the upload list itself — the folder panel also says "new class".
    await screen.findByText(/3 new · refreshes a class with 154 JDs/);
    expect(screen.getByText(/2 new · new class/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Ingest 2' }));

    await waitFor(() => expect(ingested).toEqual(['004724', '009605']));
  });

  it('hides the folder panel on a server with no export folder', async () => {
    testServer.use(
      http.get('/api/admin/ingest/pending', () => HttpResponse.json({ configured: false, pending: [] }))
    );
    renderRoute({ initialPath: '/backend' });

    await screen.findByText('Upload JDs');
    await waitFor(() =>
      expect(screen.queryByText('New JDs from the corpus folder')).not.toBeInTheDocument()
    );
  });
});
