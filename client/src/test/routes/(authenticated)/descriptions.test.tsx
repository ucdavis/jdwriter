import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import { testServer } from '@/test/mswUtils.ts';
import { screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const standardRow = (over: object) => ({
  blank: false, classSlug: null, classTitle: null, code: '009610', flsa: '', grade: '', how: 'title',
  jobTitle: 'Some Job', keyResponsibilities: 4, onPayroll: true, supersededBy: null, title: 'Ready Standard', union: '', ...over,
});

describe('job descriptions and job standards', () => {
  setupRouteTest();

  it('lists every job description, filters the ones not yet in an envelope, and opens one', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/descriptions' });

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

  it('sends the old Corpus & standards address to Descriptions', async () => {
    useContractHandlers();
    renderRoute({ initialPath: '/backend/corpus' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Descriptions' })).toBeInTheDocument();
  });

  it('lists every job standard, with what it is attached to, and shows one', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/standards' });

    const list = await screen.findByTestId('standards-list');
    expect(within(list).getAllByRole('button', { name: 'View' })).toHaveLength(3);
    expect(within(list).getByText('Not attached to a job code')).toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox', { name: 'Content' }), 'blank');
    expect(within(screen.getByTestId('standards-list')).getByText('Academic Achievement Counselor 3')).toBeInTheDocument();
    expect(within(screen.getByTestId('standards-list')).queryByText('Laboratory Assistant 1')).not.toBeInTheDocument();
    expect(screen.getByTestId('standards-summary')).toHaveTextContent('1 standard · 1 attached to a job code · 0 with a class · 1 blank');

    // Like Codes: by whether the job is active at UC Davis.
    await user.selectOptions(screen.getByRole('combobox', { name: 'Content' }), 'any');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Job code' }), 'none');
    expect(within(screen.getByTestId('standards-list')).getAllByRole('listitem')).toHaveLength(1);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Job code' }), 'active');
    expect(within(screen.getByTestId('standards-list')).getAllByRole('listitem')).toHaveLength(2);

    await user.selectOptions(screen.getByRole('combobox', { name: 'Job code' }), 'all');
    const row = within(screen.getByTestId('standards-list')).getByText('Laboratory Assistant 1').closest('li') as HTMLElement;
    await user.click(within(row).getByRole('button', { name: 'View' }));
    expect(await within(row).findByText(/standard laboratory support tasks/)).toBeInTheDocument();
  });

  it('links a standard to its job code, and a job code back to its standard and JDs', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/standards' });

    const list = await screen.findByTestId('standards-list');
    await user.click(within(list).getByRole('link', { name: /Acad Achievement Cnslr 3 · code 004501/ }));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('heading', { name: 'Acad Achievement Cnslr 3' })).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Academic Achievement Counselor 3' })).toHaveAttribute(
      'href',
      '/backend/standards?open=Academic+Achievement+Counselor+3&q=Academic+Achievement+Counselor+3'
    );
  });

  it('opens the standard a link names, searched to it', async () => {
    useContractHandlers();
    renderRoute({ initialPath: '/backend/standards?open=Laboratory%20Assistant%201&q=Laboratory' });

    const list = await screen.findByTestId('standards-list');
    expect(within(list).getAllByRole('listitem')).toHaveLength(1);
    expect(await within(list).findByText(/standard laboratory support tasks/)).toBeInTheDocument();
  });

  it("links a job code's JDs to the job descriptions, searched to its code", async () => {
    useContractHandlers();
    const user = userEvent.setup();
    renderRoute({ initialPath: '/backend/codes?code=009605' });

    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('link', { name: 'Learned from 155 JDs →' }));

    expect(await screen.findByRole('searchbox', { name: 'Search job descriptions' })).toHaveValue('009605');
    expect(within(await screen.findByTestId('jd-list')).getAllByRole('listitem')).toHaveLength(2);
  });

  it('sends an old Jobs link to Codes, keeping the job it opened', async () => {
    useContractHandlers();
    renderRoute({ initialPath: '/backend/jobs?code=009605' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Codes' })).toBeInTheDocument();
    expect(within(await screen.findByRole('dialog')).getByRole('heading', { name: 'Lab Ast 1' })).toBeInTheDocument();
  });

  it('says why a standard has no class, and starts one when it can', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    let created: unknown = null;
    testServer.use(
      http.get('/api/standards', () =>
        HttpResponse.json({
          standards: [
            standardRow({}),
            standardRow({ blank: true, code: '009611', keyResponsibilities: 0, title: 'Blank Standard' }),
            standardRow({ code: '009612', onPayroll: false, title: 'Inactive Standard' }),
            standardRow({ code: '009613', supersededBy: '009614', title: 'Superseded Standard' }),
            standardRow({ code: null, how: null, jobTitle: null, title: 'Unmatched Standard' }),
          ],
        })
      ),
      http.post('/api/admin/bootstrap', async ({ request }) => {
        created = await request.json();
        return HttpResponse.json({ slug: '009610-ready-standard', title: 'Ready Standard', ucJobCode: '009610' });
      })
    );
    renderRoute({ initialPath: '/backend/standards' });

    const reasons = await screen.findAllByTestId('no-class');
    expect(reasons.map((r) => r.textContent)).toEqual([
      expect.stringContaining('Ready: no JDs yet'),
      expect.stringContaining('blank'),
      expect.stringContaining("isn't active at UC Davis"),
      expect.stringContaining('superseded by 009614'),
      expect.stringContaining('Not matched to a job code'),
    ]);
    expect(within(reasons[2]).getByRole('link', { name: 'mark it active on Codes →' })).toHaveAttribute('href', '/backend/codes?code=009612');

    await user.click(within(reasons[0]).getByRole('button', { name: 'Create class from standard' }));
    await waitFor(() => expect(created).toEqual({ title: 'Ready Standard' }));
  });
});
