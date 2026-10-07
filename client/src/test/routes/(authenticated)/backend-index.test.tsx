import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { ClassSummary } from '@/lib/contracts.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const summary = (over: Partial<ClassSummary>): ClassSummary => ({
  consolidatedFunctions: 3,
  corpusSize: 12,
  coverageN: 12,
  ctJobFamily: 'Agriculture',
  ctJobFunction: 'Field Operations',
  envelopePctTotal: 100,
  envelopeResponsibilities: 3,
  envelopeSource: 'manual',
  grade: 'Grade 3',
  hasEnvelope: true,
  ksas: 4,
  meanCoverage: 92,
  personnelProgram: 'PSS',
  responsibilities: 3,
  slug: '004724-farm-laborer',
  standardLinked: false,
  title: 'Farm Laborer',
  ucJobCode: '004724',
  wellCoveredPct: 0.75,
  ...over,
});

const titles = () =>
  screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent);

describe('analyst index', () => {
  setupRouteTest();

  it('renders curation stats from the summary endpoint', async () => {
    testServer.use(
      http.get('/api/classes/summary', () =>
        HttpResponse.json({ classes: [summary({})] })
      )
    );
    renderRoute({ initialPath: '/backend' });

    await waitFor(() => {
      expect(screen.getByText('Farm Laborer')).toBeInTheDocument();
    });
    expect(screen.getByText('partner-edited')).toBeInTheDocument();
    expect(screen.getByText('75%')).toBeInTheDocument();
    expect(screen.getByText(/75% of JDs are ≥90% covered \(mean 92%\)/)).toBeInTheDocument();
    expect(screen.queryByText(/% time sums to/)).not.toBeInTheDocument();
  });

  it('flags an envelope whose % time does not sum to 100', async () => {
    testServer.use(
      http.get('/api/classes/summary', () =>
        HttpResponse.json({ classes: [summary({ envelopePctTotal: 90 })] })
      )
    );
    renderRoute({ initialPath: '/backend' });

    await waitFor(() => {
      expect(screen.getByText('% time sums to 90')).toBeInTheDocument();
    });
  });

  it('tells a missing coverage report apart from zero coverage', async () => {
    testServer.use(
      http.get('/api/classes/summary', () =>
        HttpResponse.json({
          classes: [
            summary({ coverageN: null, meanCoverage: null, wellCoveredPct: null }),
            summary({ slug: 'zero', title: 'Zero Class', wellCoveredPct: 0 }),
          ],
        })
      )
    );
    renderRoute({ initialPath: '/backend' });

    await waitFor(() => {
      expect(screen.getByText('not computed')).toBeInTheDocument();
    });
    expect(screen.getByText('0%')).toBeInTheDocument();
  });

  it('shows an Author the access message without requesting the summary', async () => {
    let summaryRequested = false;
    testServer.use(
      http.get('/api/user/me', () =>
        HttpResponse.json({
          email: 'author@example.edu',
          iamId: null,
          id: 'author',
          name: 'Author Only',
          roles: ['Author'],
        })
      ),
      http.get('/api/classes/summary', () => {
        summaryRequested = true;
        return HttpResponse.json({ message: 'Forbidden' }, { status: 403 });
      })
    );
    renderRoute({ initialPath: '/backend' });

    await waitFor(() => {
      expect(screen.getByText('Admin access required')).toBeInTheDocument();
    });
    expect(summaryRequested).toBe(false);
  });

  describe('finding a class', () => {
    const classes = [
      summary({
        corpusSize: 12,
        slug: 'farm',
        title: 'Farm Laborer',
        wellCoveredPct: 0.75,
      }),
      summary({
        corpusSize: 30,
        ctJobFamily: 'Finance',
        slug: 'fin',
        title: 'Financial Analyst 3 CX',
        ucJobCode: '005183',
        wellCoveredPct: 0.4,
      }),
      summary({
        consolidatedFunctions: 0,
        corpusSize: 0,
        envelopeSource: 'standard',
        slug: 'ao',
        title: 'Administrative Officer 3 CX',
        wellCoveredPct: null,
      }),
      summary({
        envelopePctTotal: 95,
        slug: 'bad',
        title: 'Broken Total',
        wellCoveredPct: 0.9,
      }),
    ];

    it('searches by title, code or family', async () => {
      const user = userEvent.setup();
      testServer.use(
        http.get('/api/classes/summary', () => HttpResponse.json({ classes }))
      );
      renderRoute({ initialPath: '/backend' });

      await screen.findByText('Farm Laborer');
      await user.type(screen.getByLabelText('Search classes'), '005183');
      expect(titles()).toEqual(['Financial Analyst 3 CX']);

      await user.clear(screen.getByLabelText('Search classes'));
      await user.type(screen.getByLabelText('Search classes'), 'finance');
      expect(titles()).toEqual(['Financial Analyst 3 CX']);
    });

    it('filters to the classes that need attention, with a count on each filter', async () => {
      const user = userEvent.setup();
      testServer.use(
        http.get('/api/classes/summary', () => HttpResponse.json({ classes }))
      );
      renderRoute({ initialPath: '/backend' });

      await screen.findByText('Farm Laborer');
      const filters = screen.getByRole('group', { name: 'Show' });
      expect(
        within(filters).getByRole('button', { name: /Standard only\s*1/ })
      ).toBeInTheDocument();

      await user.click(
        within(filters).getByRole('button', { name: /Needs attention/ })
      );
      expect(titles()).toEqual(['Broken Total']);

      await user.click(
        within(filters).getByRole('button', { name: /Standard only/ })
      );
      expect(titles()).toEqual(['Administrative Officer 3 CX']);
    });

    it('sorts weakest match first, with uncomputed coverage last', async () => {
      const user = userEvent.setup();
      testServer.use(
        http.get('/api/classes/summary', () => HttpResponse.json({ classes }))
      );
      renderRoute({ initialPath: '/backend' });

      await screen.findByText('Farm Laborer');
      await user.selectOptions(screen.getByRole('combobox'), 'match');
      expect(titles()).toEqual([
        'Financial Analyst 3 CX',
        'Farm Laborer',
        'Broken Total',
        'Administrative Officer 3 CX',
      ]);
    });

    it('opens on the filter in the URL, and offers a way out of an empty result', async () => {
      const user = userEvent.setup();
      testServer.use(
        http.get('/api/classes/summary', () => HttpResponse.json({ classes }))
      );
      renderRoute({ initialPath: '/backend?q=nothing-like-this&show=jds' });

      await screen.findByText('No classes match.');
      await user.click(
        screen.getByRole('button', { name: 'Clear search and filter' })
      );
      expect(titles()).toHaveLength(4);
    });
  });

  it('links every back-end section from a tab bar, marking the current one', async () => {
    testServer.use(
      http.get('/api/classes/summary', () =>
        HttpResponse.json({ classes: [summary({})] })
      )
    );
    renderRoute({ initialPath: '/backend' });

    const nav = await screen.findByRole('navigation', { name: 'Back end' });
    expect(
      within(nav).getByRole('link', { name: 'Envelopes' })
    ).toHaveAttribute('aria-current', 'page');
    expect(
      within(nav).getByRole('link', { name: 'Corpus & standards' })
    ).toHaveAttribute('href', '/backend/corpus');
    expect(
      within(nav).getByRole('link', { name: 'Corpus & standards' })
    ).not.toHaveAttribute('aria-current');
    for (const name of ['Reclassification review', 'Analytics', 'Settings']) {
      expect(within(nav).getByRole('link', { name })).toBeInTheDocument();
    }
  });
});
