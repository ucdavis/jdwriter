import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

describe('job descriptions and job standards', () => {
  setupRouteTest();

  it('lists every job description, filters the ones not yet in an envelope, and opens one', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/job-descriptions' });

    const list = await screen.findByTestId('jd-list');
    expect(within(list).getAllByRole('listitem')).toHaveLength(2);

    await user.click(screen.getByRole('button', { name: /Not yet in an envelope/ }));
    expect(within(screen.getByTestId('jd-list')).getAllByRole('listitem')).toHaveLength(1);

    await user.click(within(screen.getByTestId('jd-list')).getByRole('link', { name: 'View →' }));

    expect(await screen.findByRole('heading', { name: 'Greenhouse Technician' })).toBeInTheDocument();
    expect(screen.getByText('Waters and feeds plants daily.')).toBeInTheDocument();
    expect(screen.getByText('Pesticide applicator certificate.')).toBeInTheDocument();
    // Not yet in its envelope, so there is nothing to compare it with yet.
    expect(screen.queryByRole('link', { name: 'Compare with the envelope →' })).not.toBeInTheDocument();
  });

  it('sends the old Corpus & standards address to Job descriptions', async () => {
    useContractHandlers();
    renderRoute({ initialPath: '/backend/corpus' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Job descriptions' })).toBeInTheDocument();
  });

  it('lists every job standard, with what it is attached to, and shows one', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/standards' });

    const list = await screen.findByTestId('standards-list');
    expect(within(list).getAllByRole('button', { name: 'View' })).toHaveLength(3);
    expect(within(list).getByText('Not attached to a job code')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^Blank/ }));
    expect(within(screen.getByTestId('standards-list')).getByText('Academic Achievement Counselor 3')).toBeInTheDocument();
    expect(within(screen.getByTestId('standards-list')).queryByText('Laboratory Assistant 1')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^All/ }));
    const row = within(screen.getByTestId('standards-list')).getByText('Laboratory Assistant 1').closest('li') as HTMLElement;
    await user.click(within(row).getByRole('button', { name: 'View' }));
    expect(await within(row).findByText(/standard laboratory support tasks/)).toBeInTheDocument();
  });
});
