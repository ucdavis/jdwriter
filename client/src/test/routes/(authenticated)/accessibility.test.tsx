import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import { fillDepartment } from '@/test/build.ts';
import { testServer } from '@/test/mswUtils.ts';
import type { ArchivedJd } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const SLUG = '009605-lab-ast-1';

// Synthetic: the shape of an archived JD, none of a real one's content.
const archived: ArchivedJd[] = [
  {
    department: 'Plant Sciences',
    origin: 'export',
    responsibilities: [{ duties: ['Waters the tomato trials daily.'], functionName: 'Plant care', pct: 100 }],
    sourceFile: 'a',
    summary: 'Runs the teaching greenhouse.',
    workingTitle: 'Greenhouse Assistant',
  },
  {
    department: 'Entomology',
    origin: 'imported',
    responsibilities: [{ duties: ['Rears insect colonies.'], functionName: 'Colonies', pct: 100 }],
    sourceFile: 'b',
    summary: 'Maintains insect colonies.',
    workingTitle: 'Insectary Assistant',
  },
];

describe('accessibility', () => {
  setupRouteTest();

  it('offers a skip link to the main content as the first stop', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/' });
    await screen.findByRole('main');

    await user.tab();
    const skip = screen.getByRole('link', { name: 'Skip to main content' });
    expect(skip).toHaveFocus();
    expect(skip).toHaveAttribute('href', '#main');
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main');
  });

  it('names each page in the browser tab', async () => {
    renderRoute({ initialPath: '/jds' });
    await waitFor(() => expect(document.title).toBe('My JDs · JDWriter'));
  });

  it('gives a class page its class’s name in the tab', async () => {
    renderRoute({ initialPath: `/class/${SLUG}` });
    await waitFor(() => expect(document.title).toMatch(/^.+ · Build a JD · JDWriter$/));
  });

  it('moves focus into a pop-up, keeps Tab inside it, and returns focus when Escape closes it', async () => {
    const user = userEvent.setup();
    testServer.use(http.get(`/api/classes/${SLUG}/archive`, () => HttpResponse.json({ jds: archived })));
    renderRoute({ initialPath: `/class/${SLUG}` });

    const opener = await screen.findByRole('button', { name: /start from an archived job description/ });
    await user.click(opener);
    const dialog = screen.getByRole('dialog');
    expect(screen.getByRole('searchbox', { name: 'Search archived job descriptions' })).toHaveFocus();

    for (let i = 0; i < 6; i++) {
      await user.tab();
      expect(dialog).toContainElement(document.activeElement as HTMLElement);
    }

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
  });

  it('announces how many archived JDs match a search, and focuses a preview’s title', async () => {
    const user = userEvent.setup();
    testServer.use(http.get(`/api/classes/${SLUG}/archive`, () => HttpResponse.json({ jds: archived })));
    renderRoute({ initialPath: `/class/${SLUG}` });

    await user.click(await screen.findByRole('button', { name: /start from an archived job description/ }));
    await user.type(screen.getByRole('searchbox', { name: 'Search archived job descriptions' }), 'insect');
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('status')).toHaveTextContent('1 job description match.');

    await user.click(within(dialog).getByRole('button', { name: /Insectary Assistant/ }));
    expect(within(dialog).getByRole('heading', { name: 'Insectary Assistant' })).toHaveFocus();
  });

  it('closes a job’s pop-up on Codes with Escape', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/codes?code=009605' });

    await screen.findByRole('dialog');
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('announces the duties check as it runs and when it lands', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: `/class/${SLUG}` });
    await fillDepartment(user);

    await user.click(await screen.findByRole('button', { name: 'Check my duties' }));
    await screen.findByRole('button', { name: /^Continue/ });
    expect(screen.getAllByRole('status').map((s) => s.textContent)).toContain('Check complete: your changes fit the class.');
  });

  it('announces an error as it appears', async () => {
    const user = userEvent.setup();
    testServer.use(http.post('/api/build/check', () => HttpResponse.json({ message: 'The AI service is busy.' }, { status: 502 })));
    renderRoute({ initialPath: `/class/${SLUG}` });
    await fillDepartment(user);

    await user.click(await screen.findByRole('button', { name: 'Check my duties' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The AI service is busy.');
  });
});
