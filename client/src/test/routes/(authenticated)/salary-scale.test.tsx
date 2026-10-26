import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { useContractHandlers } from '@/test/mswHandlers.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const grade = (g: number, minimum: number, midpoint: number, maximum: number) => ({
  effectiveDate: '2026-07-01',
  grade: g,
  maximum,
  midpoint,
  minimum,
  note: 'Geo Diff 112% for Non-Represented Staff',
  scale: 'Career Tracks',
});

describe('the Career Tracks salary ranges', () => {
  setupRouteTest();

  it('imports Compensation’s workbook and shows each grade’s range', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    testServer.use(
      http.post('/api/admin/codes/salary-scale', () =>
        HttpResponse.json({ grades: [grade(20, 77_000, 100_000, 123_000), grade(19, 66_000, 85_000, 104_000)], importedAt: '2026-10-26T12:00:00Z' })
      )
    );
    renderRoute({ initialPath: '/backend/codes' });

    expect(await screen.findByTestId('salary-scale-status')).toHaveTextContent('No salary ranges imported yet.');
    await user.upload(
      screen.getByLabelText('Choose a Career Tracks salary ranges workbook'),
      new File(['xlsx'], 'Career Tracks Annual Salary Ranges eff 7.1.2026.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
    );

    expect(await screen.findByRole('status')).toHaveTextContent('Imported 2 grades.');
    expect(screen.getByTestId('salary-scale-status')).toHaveTextContent('Grades 19–20, effective');
    await user.click(screen.getByRole('button', { name: 'Show the ranges' }));
    const rows = within(screen.getByTestId('salary-scale')).getAllByRole('row').slice(1).map((r) => r.textContent);
    expect(rows[0]).toBe('20$77,000$100,000$123,000');
  });

  it('says why a workbook was refused', async () => {
    useContractHandlers();
    const user = userEvent.setup();
    testServer.use(
      http.post('/api/admin/codes/salary-scale', () =>
        HttpResponse.json({ message: 'No salary table was found: the sheet needs a header row starting Grade, Pay Type, Minimum.' }, { status: 400 })
      )
    );
    renderRoute({ initialPath: '/backend/codes' });

    await user.upload(await screen.findByLabelText('Choose a Career Tracks salary ranges workbook'), new File(['x'], 'other.xlsx'));
    expect(await screen.findByRole('alert')).toHaveTextContent('No salary table was found');
  });
});
