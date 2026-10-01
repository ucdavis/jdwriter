import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { isAdmin } from '@/queries/user.ts';
import { Badge, Card, Eyebrow, Note, PageHeader } from '@/shared/ui/primitives.tsx';
import {
  adminsQueryOptions,
  apiKeyQueryOptions,
  useAdmins,
  useApiKeyStatus,
  useClearApiKey,
  useGrantAdmin,
  useRevokeAdmin,
  useSetApiKey,
} from '@/queries/admin.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { createFileRoute } from '@tanstack/react-router';
import { useState } from 'react';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/settings')({
  component: SettingsPage,
  loader: async ({ context }: { context: RouterContext }) => {
    if (await isAdmin(context.queryClient)) {
      await Promise.all([
        context.queryClient.ensureQueryData(adminsQueryOptions()),
        context.queryClient.ensureQueryData(apiKeyQueryOptions()),
      ]);
    }
  },
});

function SettingsPage() {
  return (
    <AdminOnly>
      <PageHeader
        back={{ label: 'Back to envelopes', to: '/backend' }}
        eyebrow="Back end · settings"
        sub="Who can reach the back end, and the API key the app uses. Everyone who signs in can author and classify."
        title="Settings"
      />
      <AdminsPanel />
      <ApiKeyPanel />
    </AdminOnly>
  );
}

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleDateString() : null);

function AdminsPanel() {
  const { data } = useAdmins();
  const grant = useGrantAdmin();
  const revoke = useRevokeAdmin();
  const [loginId, setLoginId] = useState('');
  const admins = data?.admins ?? [];
  const error = grant.error ?? revoke.error;

  return (
    <Card className="p-5">
      <Eyebrow>Admins</Eyebrow>
      <p className="mt-1 text-[13px] text-base-content/65">
        Add an admin by UC Davis login ID (the part before @ucdavis.edu). It takes effect on
        their next request — they don&apos;t need to have signed in before, or to sign out and
        back in.
      </p>

      <form
        className="mt-4 flex items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          grant.mutate(loginId, { onSuccess: () => setLoginId('') });
        }}
      >
        <input
          aria-label="UC Davis login ID"
          className="input input-sm input-bordered w-64"
          onChange={(e) => setLoginId(e.target.value)}
          placeholder="e.g. ndlewis"
          value={loginId}
        />
        <button
          className="btn btn-primary btn-sm"
          disabled={!loginId.trim() || grant.isPending}
          type="submit"
        >
          {grant.isPending ? 'Adding…' : 'Add admin'}
        </button>
      </form>

      {error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(error)}</Note>
        </div>
      ) : null}

      <ul className="mt-4 divide-y divide-base-300 rounded-lg border border-base-300">
        {admins.map((a) => (
          <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={a.loginId}>
            <div className="min-w-0">
              <div className="text-[13px] font-medium">
                {a.loginId}
                {a.displayName ? (
                  <span className="font-normal text-base-content/65"> · {a.displayName}</span>
                ) : null}
              </div>
              <div className="text-[11.5px] text-base-content/65">
                {a.lastSeenAt ? `Last seen ${when(a.lastSeenAt)}` : 'Has not signed in yet'}
                {a.grantedAt
                  ? ` · added ${when(a.grantedAt)}${a.grantedBy ? ` by ${a.grantedBy}` : ''}`
                  : ''}
              </div>
            </div>
            {a.fromConfiguration ? (
              <Badge tone="muted">set in server configuration</Badge>
            ) : (
              <button
                className="btn btn-ghost btn-xs text-error"
                disabled={revoke.isPending}
                onClick={() => revoke.mutate(a.loginId)}
                type="button"
              >
                Remove
              </button>
            )}
          </li>
        ))}
      </ul>
    </Card>
  );
}

function ApiKeyPanel() {
  const { data: status } = useApiKeyStatus();
  const save = useSetApiKey();
  const clear = useClearApiKey();
  const [key, setKey] = useState('');
  const error = save.error ?? clear.error;

  const inUse =
    status?.source === 'app'
      ? `Key entered here, ending …${status.lastFour}${
          status.updatedBy ? ` — set by ${status.updatedBy}` : ''
        }${status.updatedAt ? ` on ${when(status.updatedAt)}` : ''}.`
      : status?.source === 'configuration'
        ? `Key from server configuration, ending …${status.lastFour}.`
        : 'No key — intake, classification and building a JD are unavailable.';

  return (
    <Card className="mt-5 p-5">
      <Eyebrow>Anthropic API key</Eyebrow>
      <p className="mt-1 text-[13px] text-base-content/65">
        A key entered here is checked with Anthropic, stored encrypted, and used in place of
        the one in server configuration until it is removed. It can&apos;t be viewed again —
        only its last four characters are shown.
      </p>

      <div className="mt-3 flex items-center gap-2 text-[13px]">
        <Badge
          tone={status?.source === 'none' ? 'red' : status?.source === 'app' ? 'green' : 'accent'}
        >
          {status?.source === 'app' ? 'in app' : status?.source ?? '…'}
        </Badge>
        <span data-testid="api-key-in-use">{status ? inUse : 'Loading…'}</span>
      </div>

      {status?.storedKeyUnreadable ? (
        <div className="mt-3">
          <Note tone="yellow">
            A key was entered here before but can no longer be decrypted (the server&apos;s
            encryption keys changed). Enter it again.
          </Note>
        </div>
      ) : null}

      <form
        className="mt-4 flex items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate(key, { onSuccess: () => setKey('') });
        }}
      >
        <input
          aria-label="New Anthropic API key"
          autoComplete="off"
          className="input input-sm input-bordered w-96"
          onChange={(e) => setKey(e.target.value)}
          placeholder="sk-ant-…"
          type="password"
          value={key}
        />
        <button
          className="btn btn-primary btn-sm"
          disabled={!key.trim() || save.isPending}
          type="submit"
        >
          {save.isPending ? 'Checking…' : status?.source === 'app' ? 'Replace key' : 'Save key'}
        </button>
        {status?.source === 'app' ? (
          <button
            className="btn btn-ghost btn-sm text-error"
            disabled={clear.isPending}
            onClick={() => clear.mutate(undefined)}
            type="button"
          >
            Remove
          </button>
        ) : null}
      </form>
      {status?.source === 'app' ? (
        <p className="mt-2 text-[11.5px] text-base-content/50">
          {status.configurationHasKey
            ? 'Removing it switches back to the key in server configuration.'
            : 'There is no key in server configuration, so removing this one turns model features off.'}
        </p>
      ) : null}

      {error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(error)}</Note>
        </div>
      ) : null}
    </Card>
  );
}
