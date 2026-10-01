import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { ClassSummary } from '@/lib/contracts.ts';
import { screen, waitFor } from '@testing-library/react';
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
});
