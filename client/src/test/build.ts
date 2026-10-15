import { screen } from '@testing-library/react';
import type { userEvent } from '@testing-library/user-event';

/** The department is required before a build can be checked or assembled. */
export const fillDepartment = async (user: ReturnType<typeof userEvent.setup>) =>
  user.type(await screen.findByLabelText(/^Department/), 'Plant Sciences');

/**
 * Check the duties on the duties step, then continue to the requirements: the check shows its
 * result on the duties page, and moving on is a separate click once it is current.
 */
export const checkAndContinue = async (user: ReturnType<typeof userEvent.setup>) => {
  await user.click(await screen.findByRole('button', { name: 'Check my duties' }));
  await user.click(await screen.findByRole('button', { name: /^Continue (to requirements|anyway) →$/ }));
  await screen.findByTestId('requirements-intro');
};
