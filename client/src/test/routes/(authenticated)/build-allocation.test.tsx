import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import '@/test/mswUtils.ts';
import { fillDepartment } from '@/test/build.ts';

/**
 * The percent-of-time allocation gate on the guided build.
 *
 * Dropping a standard responsibility carries the remaining percentages through verbatim,
 * so a tailored JD can total less than 100. The product decision is that the shortfall is
 * neither published silently nor rescaled away — the author is shown it and decides where
 * the freed time goes.
 */
const openBuild = async (user?: ReturnType<typeof userEvent.setup>) => {
  renderRoute({ initialPath: '/class/009605-lab-ast-1' });
  await screen.findByTestId('duties-intro');
  // With the department entered, only the time allocation can hold the build back.
  if (user) {
    await fillDepartment(user);
  }
};

describe('build allocation', () => {
  setupRouteTest();

  it('shows a running total that is always visible while tailoring', async () => {
    await openBuild();

    const total = await waitFor(() => screen.getByTestId('allocation-total'));
    expect(total).toHaveTextContent('100%');
    expect(screen.getByText('balanced')).toBeInTheDocument();
  });

  it('names the shortfall and blocks continuing when a responsibility is dropped', async () => {
    const user = userEvent.setup();
    await openBuild(user);

    await waitFor(() => screen.getByTestId('allocation-total'));

    // Drop the first whole responsibility. Its share of time is now unassigned.
    const firstFunction = screen.getAllByRole('checkbox', { name: /^Include / })[0];
    await user.click(firstFunction);

    await waitFor(() => {
      expect(screen.getByText(/% unallocated$/)).toBeInTheDocument();
    });

    // The reason sits next to the disabled control, not only in a toast.
    const cont = screen.getByRole('button', { name: /Check my duties/ });
    expect(cont).toBeDisabled();
    expect(screen.getByTestId('build-gate-reason')).toHaveTextContent(
      /unallocated — assign it before continuing/
    );
  });

  it('lets the author redistribute the freed time and then continue', async () => {
    const user = userEvent.setup();
    await openBuild(user);

    await waitFor(() => screen.getByTestId('allocation-total'));
    await user.click(screen.getAllByRole('checkbox', { name: /^Include / })[0]);

    const distribute = await waitFor(() =>
      screen.getByRole('button', { name: /Distribute \d+% evenly/ })
    );
    await user.click(distribute);

    // Back to exactly 100, and the gate lifts.
    await waitFor(() => {
      expect(screen.getByTestId('allocation-total')).toHaveTextContent('100%');
    });
    expect(screen.getByText('balanced')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Check my duties/ })).toBeEnabled();
  });

  it('rebalances the kept time to 100% in proportion to each function\'s share', async () => {
    const user = userEvent.setup();
    await openBuild(user);
    await waitFor(() => screen.getByTestId('allocation-total'));

    const pcts = () =>
      screen.getAllByRole('spinbutton', { name: /^Percent of time for / }).map((i) => Number((i as HTMLInputElement).value));
    const before = pcts();
    await user.click(screen.getAllByRole('checkbox', { name: /^Include / })[0]);

    await user.click(await screen.findByRole('button', { name: 'Rebalance to 100%' }));

    await waitFor(() => expect(screen.getByTestId('allocation-total')).toHaveTextContent('100%'));
    expect(screen.getByRole('button', { name: /Check my duties/ })).toBeEnabled();
    // The kept functions grew in proportion to their shares, not by the same amount each.
    const kept = before.slice(1);
    const after = pcts().slice(1);
    const scale = 100 / kept.reduce((a, b) => a + b, 0);
    after.forEach((p, i) => expect(Math.abs(p - kept[i] * scale)).toBeLessThan(1));
  });

  it('treats over-allocation the same way', async () => {
    const user = userEvent.setup();
    await openBuild(user);

    await waitFor(() => screen.getByTestId('allocation-total'));

    const pctInputs = screen.getAllByRole('spinbutton', { name: /Percent of time/ });
    await user.clear(pctInputs[0]);
    await user.type(pctInputs[0], '90');

    await waitFor(() => {
      expect(screen.getByText(/% over-allocated$/)).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /Check my duties/ })).toBeDisabled();
    expect(screen.getByTestId('build-gate-reason')).toHaveTextContent(
      /over-allocated — reduce it before continuing/
    );
  });

  it('never silently rescales the kept percentages', async () => {
    const user = userEvent.setup();
    await openBuild(user);

    await waitFor(() => screen.getByTestId('allocation-total'));

    const pctInputs = screen.getAllByRole('spinbutton', { name: /Percent of time/ });
    const keptValueBefore = (pctInputs[1] as HTMLInputElement).value;

    // Dropping one responsibility must leave the others exactly as they were. Rescaling
    // would inflate a kept share with nobody told the number moved.
    await user.click(screen.getAllByRole('checkbox', { name: /^Include / })[0]);

    await waitFor(() => screen.getByText(/% unallocated$/));
    const stillThere = screen.getAllByRole('spinbutton', { name: /Percent of time/ });
    expect((stillThere[1] as HTMLInputElement).value).toBe(keptValueBefore);
  });
});
