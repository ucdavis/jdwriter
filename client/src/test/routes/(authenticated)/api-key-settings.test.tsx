import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { ApiKeyStatus } from '@/lib/contracts.ts';
import { screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const status = (over: Partial<ApiKeyStatus>): ApiKeyStatus => ({
  configurationHasKey: true,
  lastFour: 'cfg1',
  source: 'configuration',
  storedKeyUnreadable: false,
  updatedAt: null,
  updatedBy: null,
  ...over,
});

describe('settings: Anthropic API key', () => {
  setupRouteTest();

  it('says which key is in use without ever showing it', async () => {
    testServer.use(
      http.get('/api/admin/settings/api-key', () => HttpResponse.json(status({})))
    );
    renderRoute({ initialPath: '/backend/settings' });

    expect(await screen.findByTestId('api-key-in-use')).toHaveTextContent(
      'Key from server configuration, ending …cfg1.'
    );
  });

  it('saves a key, clears the field, and shows only its last four', async () => {
    const user = userEvent.setup();
    const sent: string[] = [];
    testServer.use(
      http.get('/api/admin/settings/api-key', () => HttpResponse.json(status({}))),
      http.put('/api/admin/settings/api-key', async ({ request }) => {
        const { key } = (await request.json()) as { key: string };
        sent.push(key);
        return HttpResponse.json(
          status({ lastFour: 'wxyz', source: 'app', updatedAt: '2026-10-01T00:00:00Z', updatedBy: 'Mock Admin' })
        );
      })
    );
    renderRoute({ initialPath: '/backend/settings' });

    const input = await screen.findByLabelText('New Anthropic API key');
    expect(input).toHaveAttribute('type', 'password');
    await user.type(input, 'sk-ant-api03-secret-value-wxyz');
    await user.click(screen.getByRole('button', { name: 'Save key' }));

    expect(await screen.findByTestId('api-key-in-use')).toHaveTextContent(/ending …wxyz — set by Mock Admin/);
    expect(input).toHaveValue('');
    expect(sent).toEqual(['sk-ant-api03-secret-value-wxyz']);
    expect(screen.queryByText(/secret-value/)).not.toBeInTheDocument();
    expect(screen.getByText(/switches back to the key in server configuration/)).toBeInTheDocument();
  });

  it('shows why Anthropic rejected a key', async () => {
    const user = userEvent.setup();
    testServer.use(
      http.get('/api/admin/settings/api-key', () => HttpResponse.json(status({}))),
      http.put('/api/admin/settings/api-key', () =>
        HttpResponse.json(
          { message: 'Anthropic rejected this key. Check that it was copied completely and has not been revoked.' },
          { status: 400 }
        )
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    await user.type(await screen.findByLabelText('New Anthropic API key'), 'sk-ant-api03-bad-key-0000');
    await user.click(screen.getByRole('button', { name: 'Save key' }));

    await screen.findByText(/Anthropic rejected this key/);
  });

  it('warns when a stored key can no longer be decrypted', async () => {
    testServer.use(
      http.get('/api/admin/settings/api-key', () =>
        HttpResponse.json(status({ storedKeyUnreadable: true }))
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    await screen.findByText(/can no longer be decrypted/);
  });
});
