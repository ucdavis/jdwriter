import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const bundle = {
  envelopes: [{ envelope: { summary: 'S' }, title: 'Financial Analyst 2 CX', ucJobCode: '004767' }],
  exportedAt: '2026-10-07T00:00:00Z',
  format: 'jdwriter.standard-envelopes',
  version: 1,
};

/** jsdom's File has no text(); every browser's does. */
const jsonFile = (content: string, name: string) =>
  Object.assign(new File([content], name, { type: 'application/json' }), {
    text: () => Promise.resolve(content),
  });

describe('move envelopes between environments', () => {
  setupRouteTest();

  it('offers the export as a download', async () => {
    renderRoute({ initialPath: '/backend/corpus' });

    const link = await screen.findByRole('link', { name: 'Download envelopes' });
    expect(link).toHaveAttribute('href', '/api/admin/envelopes/export');
    expect(link).toHaveAttribute('download');
  });

  it('sends the file as downloaded and reports what was created and why the rest were skipped', async () => {
    const user = userEvent.setup();
    let sent: unknown = null;
    testServer.use(
      http.post('/api/admin/envelopes/import', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({
          created: [{ slug: '004767-financial-analyst-2-cx', title: 'Financial Analyst 2 CX', ucJobCode: '004767' }],
          skipped: [
            { message: 'A profile already exists for Widget Analyst 2.', reason: 'exists', title: 'Widget Analyst 2' },
            { message: 'Old Analyst 1 (001000) is superseded by 001001.', reason: 'superseded', title: 'Old Analyst 1' },
          ],
        });
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.upload(
      await screen.findByLabelText('Import an envelope export'),
      jsonFile(JSON.stringify(bundle), 'jdwriter-standard-envelopes-2026-10-07.json')
    );

    await screen.findByText('1 class created');
    expect(sent).toEqual(bundle);
    expect(screen.getByText('2 skipped')).toBeInTheDocument();
    expect(screen.getByText('Widget Analyst 2 — already has a class')).toHaveAttribute(
      'title',
      'A profile already exists for Widget Analyst 2.'
    );
    expect(screen.getByText('Old Analyst 1 — superseded here')).toBeInTheDocument();
  });

  it('refuses a file that is not JSON before sending anything', async () => {
    const user = userEvent.setup();
    let requests = 0;
    testServer.use(
      http.post('/api/admin/envelopes/import', () => {
        requests++;
        return HttpResponse.json({ created: [], skipped: [] });
      })
    );
    renderRoute({ initialPath: '/backend/corpus' });

    await user.upload(
      await screen.findByLabelText('Import an envelope export'),
      jsonFile('not json', 'notes.json')
    );

    await screen.findByText('notes.json is not an envelope export (it is not JSON).');
    expect(requests).toBe(0);
  });
});
