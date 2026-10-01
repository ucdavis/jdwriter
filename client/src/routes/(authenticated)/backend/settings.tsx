import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { isAdmin } from '@/queries/user.ts';
import { Badge, Card, Eyebrow, Note, PageHeader } from '@/shared/ui/primitives.tsx';
import { adminsQueryOptions, useAdmins, useGrantAdmin, useRevokeAdmin } from '@/queries/admin.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { createFileRoute } from '@tanstack/react-router';
import { useState } from 'react';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/settings')({
  component: SettingsPage,
  loader: async ({ context }: { context: RouterContext }) => {
    if (await isAdmin(context.queryClient)) {
      await context.queryClient.ensureQueryData(adminsQueryOptions());
    }
  },
});

function SettingsPage() {
  return (
    <AdminOnly>
      <PageHeader
        back={{ label: 'Back to envelopes', to: '/backend' }}
        eyebrow="Back end · settings"
        sub="Who can reach the back end. Everyone else who signs in can author and classify."
        title="Settings"
      />
      <AdminsPanel />
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
