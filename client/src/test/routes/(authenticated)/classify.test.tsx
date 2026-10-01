import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import '@/test/mswUtils.ts';

describe('classify', () => {
  setupRouteTest();

  it('classifies pasted text and shows the coverage split', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/classify' });

    const textarea = await waitFor(() => screen.getByLabelText('Job description text'));
    await user.type(textarea, 'Analyses study datasets and reports findings.');
    await user.click(screen.getByRole('button', { name: 'Classify this description' }));

    await waitFor(() => {
      expect(screen.getByText('Clear match')).toBeInTheDocument();
    });
    // One per match card. Alternatives are compared on the SAME axes as the top match —
    // coverage, level fit, in/out functions — rather than merely being listed, so this
    // label legitimately appears more than once.
    expect(
      screen.getAllByText('Description covered by this class').length
    ).toBeGreaterThan(1);
    expect(screen.getByText('Other classes considered')).toBeInTheDocument();
  });

  it('reads a dropped file into the box WITHOUT classifying it', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/classify' });

    await waitFor(() => screen.getByLabelText('Job description text'));

    // Extraction is free; classification costs a model call. Uploading must fill the box
    // and stop, so a mangled PDF can be corrected before it costs anything.
    const file = new File(['pd'], 'PD.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    await user.upload(screen.getByLabelText('Upload a job description file'), file);

    await waitFor(() => {
      expect(
        screen.getByText('PD_Evaluation_Analyst_final.xlsx')
      ).toBeInTheDocument();
    });
    expect(screen.queryByText('Clear match')).not.toBeInTheDocument();
  });

  it('names an un-ingested proposal a corpus gap rather than a disagreement', async () => {
    const user = userEvent.setup();
    renderRoute({ initialPath: '/classify' });

    await waitFor(() => screen.getByLabelText('Job description text'));

    const file = new File(['pd'], 'PD.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    await user.upload(screen.getByLabelText('Upload a job description file'), file);
    await waitFor(() =>
      screen.getByText('PD_Evaluation_Analyst_final.xlsx')
    );

    await user.click(screen.getByRole('button', { name: 'Classify this description' }));

    // The classifier can only return classes it has ingested, so a proposal that was never
    // ingested could not have been returned. Calling that a disagreement would tell an
    // analyst their unit is wrong when the truth is our corpus is incomplete.
    await waitFor(() => {
      expect(screen.getByText('Corpus gap — not a disagreement')).toBeInTheDocument();
    });
    expect(screen.queryByText('Differs from the unit')).not.toBeInTheDocument();
    expect(
      screen.getByText(/could not have been returned by the classifier/)
    ).toBeInTheDocument();
  });
});
