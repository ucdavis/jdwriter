import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { testServer } from '@/test/mswUtils.ts';
import { describe, expect, it } from 'vitest';

describe('app footer', () => {
  setupRouteTest();

  it('credits CRU, and sends problems to the JDWriter team rather than CRU’s help desk', async () => {
    renderRoute({ initialPath: '/' });

    const footer = await screen.findByRole('contentinfo');
    expect(
      within(footer).getByRole('img', {
        name: 'UC Davis College of Agricultural and Environmental Sciences',
      })
    ).toHaveAttribute('src', '/caes.svg');
    expect(footer).toHaveTextContent(/Created in association with CRU\s*\|\s*Report a problem/);
    expect(within(footer).getByRole('link', { name: 'CRU' })).toHaveAttribute(
      'href',
      'https://computing.caes.ucdavis.edu/'
    );
    expect(footer.querySelector('a[href*="caeshelp"]')).toBeNull();
    expect(within(footer).getByRole('button', { name: 'Report a problem' })).toBeInTheDocument();
  });

  it('sends a report with the page it came from, and thanks the sender', async () => {
    const user = userEvent.setup();
    const sent: unknown[] = [];
    testServer.use(
      http.post('/api/reports', async ({ request }) => {
        sent.push(await request.json());
        return HttpResponse.json({ sent: true });
      })
    );
    renderRoute({ initialPath: '/classify' });

    await user.click(await screen.findByRole('button', { name: 'Report a problem' }));
    const dialog = screen.getByRole('dialog', { name: 'Report a problem' });
    const send = within(dialog).getByRole('button', { name: 'Send report' });
    expect(send).toBeDisabled();
    expect(within(dialog).getByRole('textbox', { name: /What happened/ })).toHaveFocus();

    await user.click(within(dialog).getByRole('radio', { name: 'A request' }));
    await user.type(within(dialog).getByRole('textbox', { name: /What happened/ }), 'Please add a print button.');
    await user.click(send);

    expect(await within(dialog).findByRole('status')).toHaveTextContent('your report went to the JDWriter team');
    expect(sent).toEqual([{ kind: 'Request', message: 'Please add a print button.', page: '/classify' }]);
  });

  it('keeps the corner artwork out of the accessibility tree', async () => {
    renderRoute({ initialPath: '/' });

    const footer = await screen.findByRole('contentinfo');
    // Only the lockup is announced; the writing-tool art is decoration.
    expect(within(footer).getAllByRole('img')).toHaveLength(1);
  });
});
