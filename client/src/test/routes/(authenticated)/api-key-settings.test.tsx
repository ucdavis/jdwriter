import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import type { ApiKeyStatus } from '@/lib/contracts.ts';
import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

const status = (over: Partial<ApiKeyStatus>): ApiKeyStatus => ({
  audit: [],
  configurationHasKey: true,
  endpoint: '',
  keyEntryAllowed: true,
  keyRequired: true,
  lastFour: 'cfg1',
  model: 'claude-opus-5',
  provider: 'anthropic',
  source: 'configuration',
  storedKeyUnreadable: false,
  updatedAt: null,
  updatedBy: null,
  ...over,
});

describe('settings: AI provider and key', () => {
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

    const input = await screen.findByLabelText('New API key');
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

    await user.type(await screen.findByLabelText('New API key'), 'sk-ant-api03-bad-key-0000');
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

  it('names the configured provider and model, which the app cannot change', async () => {
    testServer.use(
      http.get('/api/admin/settings/api-key', () =>
        HttpResponse.json(
          status({ endpoint: 'https://campus.openai.azure.com', model: 'gpt-4o-jd', provider: 'azureOpenAi' })
        )
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    expect(await screen.findByTestId('ai-provider')).toHaveTextContent(
      'Azure OpenAI · gpt-4o-jd · https://campus.openai.azure.com'
    );
    expect(screen.getByText(/checked with Azure OpenAI/)).toBeInTheDocument();
  });

  it('offers no key entry where keys live only in Key Vault', async () => {
    testServer.use(
      http.get('/api/admin/settings/api-key', () => HttpResponse.json(status({ keyEntryAllowed: false })))
    );
    renderRoute({ initialPath: '/backend/settings' });

    expect(await screen.findByTestId('key-vault-only')).toHaveTextContent(/managed in Azure Key Vault/);
    expect(screen.queryByLabelText('New API key')).not.toBeInTheDocument();
  });

  it('lists who changed the key and when, never the key', async () => {
    testServer.use(
      http.get('/api/admin/settings/api-key', () =>
        HttpResponse.json(
          status({
            audit: [
              { action: 'cleared', at: '2026-10-05T00:00:00Z', by: 'Mock Admin', lastFour: null },
              { action: 'set', at: '2026-10-01T00:00:00Z', by: 'Mock Admin', lastFour: 'wxyz' },
            ],
          })
        )
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    const items = (await screen.findByTestId('key-audit')).querySelectorAll('li');
    expect([...items].map((li) => li.textContent)).toEqual([
      expect.stringMatching(/^Removed the key — Mock Admin/),
      expect.stringMatching(/^Set key ending …wxyz — Mock Admin/),
    ]);
  });

  it('does not alarm about a missing key on a local server that needs none', async () => {
    testServer.use(
      http.get('/api/admin/settings/api-key', () =>
        HttpResponse.json(
          status({ configurationHasKey: false, keyRequired: false, lastFour: null, provider: 'openAiCompatible', source: 'none' })
        )
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    expect(await screen.findByTestId('api-key-in-use')).toHaveTextContent('this server does not need one');
  });

  it.each([
    [true, 'encrypted', /Transparent Data Encryption on/],
    [false, 'not encrypted', /Expected on a local development database/],
    [null, 'unknown', /could not be determined/],
  ] as const)('reports database encryption at rest: %s', async (value, badge, text) => {
    testServer.use(
      http.get('/api/admin/settings/security', () =>
        HttpResponse.json({ databaseEncryptedAtRest: value })
      )
    );
    renderRoute({ initialPath: '/backend/settings' });

    const row = await screen.findByTestId('encryption-at-rest');
    await waitFor(() => expect(row).toHaveTextContent(badge));
    expect(row).toHaveTextContent(text);
  });
});
