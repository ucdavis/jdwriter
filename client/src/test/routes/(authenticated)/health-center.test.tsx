import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import profilesFixture from '@/mocks/profiles.json' with { type: 'json' };
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const base = (profilesFixture as Array<Record<string, unknown>>).find((p) => p.slug === '009605-lab-ast-1');

/** The default test class, with Health Center and customization details as the server sends them. */
const asClass = (over: Record<string, unknown>) =>
  testServer.use(http.get('/api/classes/:slug', () => HttpResponse.json({ ...base, ...over })));

describe('Health Center classes', () => {
  setupRouteTest();

  it('points a regular class to its Health Center code', async () => {
    asClass({
      healthCenterOnly: false,
      healthCenterTwin: { healthCenter: true, slug: '004845-accounting-mgr-2-hc', title: 'Accounting Mgr 2 HC', ucJobCode: '004845' },
    });
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    const note = await screen.findByTestId('health-center-note');
    expect(note).toHaveTextContent('At the Health Center? Use Accounting Mgr 2 HC (004845) instead.');
    expect(within(note).getByRole('link', { name: 'Accounting Mgr 2 HC' })).toHaveAttribute('href', '/class/004845-accounting-mgr-2-hc');
    expect(within(note).queryByText('Health Center only')).not.toBeInTheDocument();
  });

  it('labels a Health Center class and points elsewhere to the regular code', async () => {
    asClass({
      healthCenterOnly: true,
      healthCenterTwin: { healthCenter: false, slug: null, title: 'Accounting Mgr 2', ucJobCode: '000686' },
      title: 'Accounting Manager 2 HC',
    });
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    const note = await screen.findByTestId('health-center-note');
    expect(within(note).getByText('Health Center only')).toBeInTheDocument();
    expect(note).toHaveTextContent('Not at the Health Center? Use Accounting Mgr 2 (000686) instead.');
    // No class in JDWriter yet: named, not linked.
    expect(within(note).queryByRole('link')).not.toBeInTheDocument();
  });

  it('marks Health Center classes in the class list', async () => {
    testServer.use(
      http.get('/api/classes', () =>
        HttpResponse.json({
          classes: [
            { healthCenterOnly: true, ready: false, slug: 'seed-006536', title: 'Social Work HC Supv 2', ucJobCode: '006536' },
            { healthCenterOnly: false, ready: true, slug: '009605-lab-ast-1', title: 'Lab Ast 1', ucJobCode: '009605' },
          ],
        })
      )
    );
    renderRoute({ initialPath: '/' });

    const hc = (await screen.findByText('Social Work HC Supv 2')).closest('button') as HTMLElement;
    expect(within(hc).getByText('Health Center only')).toBeInTheDocument();
    const lab = screen.getByText('Lab Ast 1').closest('button') as HTMLElement;
    expect(within(lab).queryByText('Health Center only')).not.toBeInTheDocument();
  });
});

describe('customization target', () => {
  setupRouteTest();

  it('allows senior and supervisory classes up to 30%', async () => {
    const user = userEvent.setup();
    asClass({ customizationTarget: 30, title: 'Financial Analyst 4' });
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    const add = (await screen.findAllByPlaceholderText('Add a duty to this function…'))[0];
    await user.type(add, 'Leads the annual budget model redesign{Enter}');

    expect(await screen.findByTestId('customization')).toHaveTextContent('target ≤30%');
  });

  it('keeps most classes at 10%', async () => {
    const user = userEvent.setup();
    asClass({ customizationTarget: 10 });
    renderRoute({ initialPath: '/class/009605-lab-ast-1' });

    const add = (await screen.findAllByPlaceholderText('Add a duty to this function…'))[0];
    await user.type(add, 'Leads the annual budget model redesign{Enter}');

    expect(await screen.findByTestId('customization')).toHaveTextContent('target ≤10%');
  });
});
