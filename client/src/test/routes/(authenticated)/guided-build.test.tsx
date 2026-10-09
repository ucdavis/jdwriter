import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { BuildRequest, EnvelopeCheckResponse } from '@/lib/contracts.ts';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
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

    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));
    await user.click(await screen.findByRole('button', { name: 'Assemble the JD →' }));

    expect(await screen.findByTestId('build-outcome')).toHaveTextContent('Great! Your JD passed.');
    expect(checks).toHaveLength(1);
  });

  it('checks again when a requirement was added, and lets the author decide when it does not fit', async () => {
    const user = userEvent.setup();
    const checks = countChecks('in_envelope', 'out_of_envelope');
    open();

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

    await user.click(await screen.findByRole('button', { name: 'Check my duties →' }));

    expect(await screen.findByText('Check completed!')).toBeInTheDocument();
    expect(screen.getByText('Fits class')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Look for a better fit' })).not.toBeInTheDocument();
  });

  it('says plainly when the build does not fit', async () => {
    const user = userEvent.setup();
    countChecks('out_of_envelope');
    open();

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

    expect(await screen.findByText(/^You added “.+”, and it already fits within “.+”, so it was not included\.$/)).toBeInTheDocument();
  });

  it('has a Home link in the top navigation', async () => {
    open();

    await screen.findByTestId('duties-intro');
    expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
  });
});
