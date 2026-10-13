import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import { act, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

/**
 * Leaving a page to look at a suggestion and coming back finds the request, the search and the
 * suggestions as they were. Clear starts over. (A reload starts over too: nothing is stored in
 * the browser — the state lives in memory, for the life of the app.)
 */
describe('searches and recommendations persist across pages', () => {
  setupRouteTest();

  it('keeps the AI request, its suggestions and the class search when you open a class and come back', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    const { router } = renderRoute({ initialPath: '/' });

    await user.type(await screen.findByLabelText('Describe the role'), 'Runs lab samples and logs results');
    await user.click(screen.getByRole('button', { name: 'Find matching class' }));
    const suggestion = (await screen.findAllByText(/% match$/))[0].closest('button') as HTMLElement;
    await user.type(screen.getByLabelText('Search job title or code'), 'lab');

    await user.click(suggestion);
    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/class\//));

    await act(() => router.history.back());

    expect(await screen.findByLabelText('Describe the role')).toHaveValue('Runs lab samples and logs results');
    expect(await screen.findAllByText(/% match$/)).not.toHaveLength(0);
    expect(screen.getByLabelText('Search job title or code')).toHaveValue('lab');
  });

  it('starts over when cleared', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/' });

    await user.type(await screen.findByLabelText('Describe the role'), 'Runs lab samples');
    await user.click(screen.getByRole('button', { name: 'Find matching class' }));
    await screen.findAllByText(/% match$/);

    await user.click(screen.getByRole('button', { name: 'Clear' }));

    expect(screen.getByLabelText('Describe the role')).toHaveValue('');
    expect(screen.queryByText(/% match$/)).not.toBeInTheDocument();
  });

  it('keeps a classified description and its recommendations when you leave and come back', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    const { router } = renderRoute({ initialPath: '/classify' });

    const text = 'Supports an active research laboratory by preparing materials and running routine assays.';
    await user.type(await screen.findByLabelText('Job description text'), text);
    await user.click(screen.getByRole('button', { name: 'Classify this description' }));
    await screen.findByText('Other classes considered');

    await act(() => router.navigate({ to: '/' }));
    await screen.findByText('Start with the role');
    await act(() => router.history.back());

    expect(await screen.findByLabelText('Job description text')).toHaveValue(text);
    expect(await screen.findByText('Other classes considered')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear' }));
    expect(screen.getByLabelText('Job description text')).toHaveValue('');
    expect(screen.queryByText('Other classes considered')).not.toBeInTheDocument();
  });
});
