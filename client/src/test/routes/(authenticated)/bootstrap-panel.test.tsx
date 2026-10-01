import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const candidate = (title: string, code: string) => ({
  code,
  family: 'Student Services',
  function: 'Advising',
  grade: 'Grade 20',
  title,
});

describe('bootstrap from a standard', () => {
  setupRouteTest();

  it('filters candidates and creates an envelope for the chosen one', async () => {
    const user = userEvent.setup();
    let remaining = [
      candidate('Financial Aid Advisor 1', '004502'),
      candidate('Career Counselor 2', '004610'),
    ];
    const created: string[] = [];
    testServer.use(
      http.get('/api/admin/bootstrap/candidates', () =>
        HttpResponse.json({ candidates: remaining })
      ),
      http.post('/api/admin/bootstrap', async ({ request }) => {
        const { title } = (await request.json()) as { title: string };
        created.push(title);
        remaining = remaining.filter((c) => c.title !== title);
        return HttpResponse.json({
          envelopeSource: 'standard',
          slug: '004610-career-counselor-2',
          title,
          ucJobCode: '004610',
        });
      })
    );
    renderRoute({ initialPath: '/backend' });

    await user.click(await screen.findByRole('button', { name: 'Find candidates' }));
    await screen.findByText('2 of 2');

    await user.type(screen.getByLabelText('Filter candidates by title'), 'career');
    expect(screen.getByText('1 of 2')).toBeInTheDocument();
    expect(screen.queryByText('Financial Aid Advisor 1')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Create envelope' }));

    await screen.findByText(/Created Career Counselor 2 \(004610\) from its standard/);
    expect(created).toEqual(['Career Counselor 2']);
    // The list is re-asked of the server, so the created class drops out of it.
    await waitFor(() => {
      expect(screen.queryByText('Career Counselor 2', { selector: 'div' })).not.toBeInTheDocument();
    });
  });

  it('shows the server refusal for a superseded class verbatim', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.get('/api/admin/bootstrap/candidates', () =>
        HttpResponse.json({ candidates: [candidate('Old Analyst 2', '001000')] })
      ),
      http.post('/api/admin/bootstrap', () =>
        HttpResponse.json(
          { message: 'Old Analyst 2 (001000) is superseded by 001001; bootstrap that instead.' },
          { status: 400 }
        )
      )
    );
    renderRoute({ initialPath: '/backend' });

    await user.click(await screen.findByRole('button', { name: 'Find candidates' }));
    await user.click(await screen.findByRole('button', { name: 'Create envelope' }));

    await screen.findByText(/superseded by 001001/);
  });
});
