import { screen } from '@testing-library/react';
import type { userEvent } from '@testing-library/user-event';

/** The department is required before a build can be checked or assembled. */
export const fillDepartment = async (user: ReturnType<typeof userEvent.setup>) =>
  user.type(await screen.findByLabelText(/^Department/), 'Plant Sciences');
