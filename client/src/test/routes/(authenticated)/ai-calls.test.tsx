import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { AiCall } from '@/lib/contracts.ts';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const call = (over: Partial<AiCall>): AiCall => ({
  at: '2026-10-27T17:00:00Z',
  effort: 'medium',
  inputTokens: 12_400,
  label: 'classify.rank',
  ms: 61_500,
  outcome: 'ok',
  outputTokens: 5200,
  provider: 'OpenAiCompatible',
  reasoningTokens: 3900,
  ...over,
});

describe('settings: recent AI calls', () => {
  setupRouteTest();

  it('shows each recent call’s time, tokens, thinking and effort, newest first, and refreshes', async () => {
    const user = userEvent.setup();
    let served = 0;
    testServer.use(
      http.get('/api/admin/settings/ai-calls', () => {
        served++;
        return HttpResponse.json({
          calls: [call({}), call({ effort: 'low', label: 'classify.distill', ms: 27_300, reasoningTokens: 900 })],
        });
      })
    );
    renderRoute({ initialPath: '/backend/settings' });

    const table = await screen.findByTestId('ai-calls');
    const rows = within(table).getAllByRole('row').slice(1).map((r) => r.textContent);
    expect(rows[0]).toContain('classify.rank');
    expect(rows[0]).toContain('61.5 s');
    expect(rows[0]).toContain('3,900');
    expect(rows[1]).toContain('classify.distill');
    expect(rows[1]).toContain('low');

    await user.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(served).toBeGreaterThanOrEqual(2);
  });

  it('says so when there have been no calls since the server started', async () => {
    renderRoute({ initialPath: '/backend/settings' });

    expect(await screen.findByTestId('ai-calls-empty')).toHaveTextContent('No AI calls since the server last started.');
  });
});
