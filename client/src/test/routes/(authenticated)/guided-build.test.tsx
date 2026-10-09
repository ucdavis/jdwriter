import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { fillDepartment } from '@/test/build.ts';
import type { BuildRequest, EnvelopeCheckResponse } from '@/lib/contracts.ts';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import profilesFixture from '@/mocks/profiles.json' with { type: 'json' };
import { describe, expect, it } from 'vitest';

const verdict = (v: EnvelopeCheckResponse['verdict']): EnvelopeCheckResponse => ({
  betterFitChecked: false,
  matchedSignals: v === 'in_envelope' ? [] : ['Sets the unit budget'],
  rationale: v === 'in_envelope' ? 'These fit the class.' : 'Budget authority belongs to a higher class.',
  suggestedClass: '',
  suggestedRationale: '',
  suggestedSlug: '',
  verdict: v,
});

/** Counts envelope checks, answering each from the given verdicts in turn. */
const countChecks = (...verdicts: Array<EnvelopeCheckResponse['verdict']>) => {
  const seen: BuildRequest[] = [];
  testServer.use(
    http.post('/api/build/check', async ({ request }) => {
      seen.push((await request.json()) as BuildRequest);
      return HttpResponse.json(verdict(verdicts[Math.min(seen.length - 1, verdicts.length - 1)]));
    })
  );
  return seen;
};

const open = () => renderRoute({ initialPath: '/class/009605-lab-ast-1' });

/** The default test class, retitled or marked represented. */
const asClass = (over: Record<string, unknown>) => {
  const base = (profilesFixture as Array<Record<string, unknown>>).find((p) => p.slug === '009605-lab-ast-1');
  testServer.use(http.get('/api/classes/:slug', () => HttpResponse.json({ ...base, ...over })));
};

/** The build request the duties check sent. */
const sentOnCheck = async (user: ReturnType<typeof userEvent.setup>) => {
  const checks = countChecks('in_envelope');
  await fillDepartment(user);
  await user.click(screen.getByRole('button', { name: 'Check my duties →' }));
  await screen.findByTestId('requirements-intro');
  return checks[0];
};

describe('the guided build', () => {
  setupRouteTest();

  it('opens on the duties step with the envelope at a glance, and the requirements on the next step', async () => {
    open();

    await screen.findByTestId('duties-intro');
    expect(screen.getByTestId('envelope-summary')).not.toBeEmptyDOMElement();
    expect(screen.getByTestId('envelope-facts')).toHaveTextContent('UC job code');
    expect(screen.getByRole('listitem', { current: 'step' })).toHaveTextContent('Duties');
    // Qualifications wait for step 2, so the duties are not pushed below the fold.
    expect(screen.queryByText('Preferred qualifications')).not.toBeInTheDocument();
  });

  it('shows the standard and the out-of-envelope signals only when asked', async () => {
    const user = userEvent.setup();
    open();

    await screen.findByTestId('duties-intro');
    expect(screen.queryByTestId('outside-envelope')).not.toBeInTheDocument();

    await user.click(screen.getByLabelText(/Show what.s outside this envelope/));
    expect(screen.getByTestId('outside-envelope')).toHaveTextContent('Not part of this class');

    await user.click(screen.getByLabelText('Show the job standard'));
    expect(await screen.findByText(/Authoritative baseline/)).toBeInTheDocument();
  });

  it('checks the duties, then moves on to the requirements without the envelope overview', async () => {
    const user = userEvent.setup();
    countChecks('in_envelope');
    open();

    await fillDepartment(user);
    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));

    expect(await screen.findByText('Check completed!')).toBeInTheDocument();
    expect(screen.getByTestId('requirements-intro')).toBeInTheDocument();
    expect(screen.getByText('Preferred qualifications')).toBeInTheDocument();
    expect(screen.queryByTestId('envelope-summary')).not.toBeInTheDocument();
    expect(screen.getByRole('listitem', { current: 'step' })).toHaveTextContent('Requirements');
  });

  it('does not check twice when nothing changed since the duties check', async () => {
    const user = userEvent.setup();
    const checks = countChecks('in_envelope');
    open();

    await fillDepartment(user);
    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));
    await user.click(await screen.findByRole('button', { name: 'Assemble the JD →' }));

    expect(await screen.findByTestId('build-outcome')).toHaveTextContent('Great! Your JD passed.');
    expect(checks).toHaveLength(1);
  });

  it('checks again when a requirement was added, and lets the author decide when it does not fit', async () => {
    const user = userEvent.setup();
    const checks = countChecks('in_envelope', 'out_of_envelope');
    open();

    await fillDepartment(user);
    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));
    await user.type(await screen.findByPlaceholderText('Add to preferred qualifications…'), 'Sets the unit budget{Enter}');
    await user.click(screen.getByRole('button', { name: 'Assemble the JD →' }));

    expect(await screen.findByText('Final check — a few things to look at')).toBeInTheDocument();
    expect(checks).toHaveLength(2);
    expect(checks[1].addedItems).toContain('Sets the unit budget');

    // The same build again is the author's decision to go ahead — not a third check.
    await user.click(screen.getByRole('button', { name: 'Assemble anyway →' }));
    expect(await screen.findByTestId('build-outcome')).toBeInTheDocument();
    expect(checks).toHaveLength(2);
  });

  it('ends with the outcome, downloads and next steps, and goes back to make changes', async () => {
    const user = userEvent.setup();
    countChecks('in_envelope');
    open();

    await fillDepartment(user);
    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));
    await user.click(await screen.findByRole('button', { name: 'Assemble the JD →' }));

    expect(await screen.findByTestId('build-outcome')).toHaveTextContent('Great! Your JD passed.');
    expect(screen.getByRole('button', { name: '⤓ Download PDF' })).toBeEnabled();
    expect(screen.getByRole('region', { name: /Workforce Management/ })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { current: 'step' })).toHaveTextContent('Your JD');

    await user.click(screen.getByRole('button', { name: 'Make changes' }));
    await waitFor(() => expect(screen.getByTestId('duties-intro')).toBeInTheDocument());
  });

  it('says plainly whether the build fits, and offers no better-fit search when it does', async () => {
    const user = userEvent.setup();
    countChecks('borderline');
    open();

    await fillDepartment(user);
    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));

    expect(await screen.findByText('Check completed!')).toBeInTheDocument();
    expect(screen.getByText('Fits class')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Look for a better fit' })).not.toBeInTheDocument();
  });

  it('says plainly when the build does not fit', async () => {
    const user = userEvent.setup();
    countChecks('out_of_envelope');
    open();

    await fillDepartment(user);
    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));

    expect(await screen.findByText('Doesn’t fit class')).toBeInTheDocument();
    expect(screen.getByText('Check completed — take a look')).toBeInTheDocument();
  });

  it('explains why an added item that the class already covers was not included', async () => {
    const user = userEvent.setup();
    open();

    const add = (await screen.findAllByPlaceholderText('Add a duty to this function…'))[0];
    const existing = screen.getAllByRole('checkbox', { checked: true }).find((c) => c.closest('li'));
    const text = existing?.closest('label')?.textContent?.trim() ?? '';
    await user.type(add, `${text}{Enter}`);

    // Shown under the box it was typed in, not at the top of the page.
    const refusal = await screen.findByTestId('add-refusal');
    expect(refusal).toHaveTextContent(/^You added “.+”, and it already fits within “.+”, so it was not included\.$/);
    expect(add.parentElement?.parentElement).toContainElement(refusal);

    // Typing again clears it.
    await user.type(add, 'x');
    expect(screen.queryByTestId('add-refusal')).not.toBeInTheDocument();
  });

  it('goes back a step from the progress bar, with no separate back buttons', async () => {
    const user = userEvent.setup();
    countChecks('in_envelope');
    open();

    await fillDepartment(user);
    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));
    await screen.findByTestId('requirements-intro');
    expect(screen.queryByRole('button', { name: /Back to duties/ })).not.toBeInTheDocument();
    // Later steps are not links: they are reached by passing the check.
    expect(screen.queryByRole('button', { name: 'Your JD' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Duties' }));
    expect(await screen.findByTestId('duties-intro')).toBeInTheDocument();
  });

  it('says how many job descriptions the envelope was built from', async () => {
    open();

    await screen.findByTestId('duties-intro');
    expect(screen.getByText(/^Built from \d+ job descriptions?$/)).toBeInTheDocument();
    expect(screen.queryByText('AI-synthesized')).not.toBeInTheDocument();
  });

  it('requires the department before the duties can be checked', async () => {
    const user = userEvent.setup();
    open();

    const check = await screen.findByRole('button', { name: 'Check my duties →' });
    expect(check).toBeDisabled();
    expect(screen.getByTestId('build-gate-reason')).toHaveTextContent('Enter the department before continuing.');
    expect(screen.getByLabelText(/^Department/)).toHaveAttribute('aria-required', 'true');

    await fillDepartment(user);
    expect(check).toBeEnabled();
    expect(screen.queryByTestId('build-gate-reason')).not.toBeInTheDocument();
  });

  describe('supervision', () => {
    it('starts a regular role at No for both, with no count asked', async () => {
      const user = userEvent.setup();
      open();

      expect(await screen.findByLabelText('Supervises')).toHaveValue('no');
      expect(screen.getByLabelText('Leads')).toHaveValue('no');
      expect(screen.queryByLabelText(/How many people/)).not.toBeInTheDocument();

      const sent = await sentOnCheck(user);
      expect([sent.supervises, sent.supervisesCount, sent.leads]).toEqual([false, null, false]);
    });

    it('starts a supervisor role at Yes, and requires how many it supervises', async () => {
      const user = userEvent.setup();
      asClass({ isRepresented: false, title: 'Lab Supervisor 2' });
      open();

      expect(await screen.findByLabelText('Supervises')).toHaveValue('yes');
      expect(screen.getByLabelText('Leads')).toHaveValue('yes');
      await fillDepartment(user);
      expect(screen.getByRole('button', { name: 'Check my duties →' })).toBeDisabled();
      expect(screen.getByTestId('build-gate-reason')).toHaveTextContent('Enter how many people this position supervises.');

      await user.type(screen.getByLabelText(/How many people/), '4');
      const checks = countChecks('in_envelope');
      await user.click(screen.getByRole('button', { name: 'Check my duties →' }));
      await screen.findByTestId('requirements-intro');
      expect([checks[0].supervises, checks[0].supervisesCount]).toEqual([true, 4]);
    });

    it('never lets a union-represented class supervise, but lets it lead', async () => {
      const user = userEvent.setup();
      asClass({ bargainingUnit: 'SV', isRepresented: true, title: 'Student Services Supervisor 2 SV' });
      open();

      const supervises = await screen.findByLabelText('Supervises');
      expect(supervises).toHaveValue('no');
      expect(supervises).toBeDisabled();
      expect(screen.getByTestId('represented-note')).toHaveTextContent(/union-represented class \(SV\), so it can.t supervise\. It may lead\./);
      expect(screen.getByLabelText('Leads')).toBeEnabled();

      const sent = await sentOnCheck(user);
      expect([sent.supervises, sent.leads]).toEqual([false, true]);
    });

    it('shows the bargaining unit from the title suffix', async () => {
      asClass({ bargainingUnit: 'SV', isRepresented: true, title: 'Student Services Advisor 3 SV' });
      open();

      expect(await screen.findByTestId('envelope-facts')).toHaveTextContent('Bargaining unitSV');
    });
  });

  it('has a Home link in the top navigation', async () => {
    open();

    await screen.findByTestId('duties-intro');
    expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
  });
});
