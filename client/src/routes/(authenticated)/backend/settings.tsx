import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { isAdmin } from '@/queries/user.ts';
import { Badge, Card, Eyebrow, Note, PageHeader } from '@/shared/ui/primitives.tsx';
import {
  adminsQueryOptions,
  apiKeyQueryOptions,
  useAdmins,
  useApiKeyStatus,
  usePackageApply,
  usePackagePreview,
  useClearApiKey,
  useGrantAdmin,
  useRevokeAdmin,
  useSecurityStatus,
  useSetApiKey,
} from '@/queries/admin.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { appUrl } from '@/lib/basePath.ts';
import { createFileRoute } from '@tanstack/react-router';
import { useState } from 'react';
import type { RouterContext } from '@/main.tsx';
import type { LlmProvider, PackageSection } from '@/lib/contracts.ts';

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
        eyebrow="Back end · settings"
        sub="Who can reach the back end, the API key the app uses, and moving everything between environments. Everyone who signs in can author and classify."
        title="Settings"
      />
      <AdminsPanel />
      <ApiKeyPanel />
      <PackagePanel />
      <DataProtectionPanel />
    </AdminOnly>
  );
}

/**
 * Everything this environment has built, in one file, to carry to another — from a laptop to the
 * server, and back. The one place to move work between environments: export here, import there,
 * with a preview of exactly what will change before anything does.
 */
function PackagePanel() {
  const preview = usePackagePreview();
  const apply = usePackageApply();
  const [file, setFile] = useState<File | null>(null);
  const plan = apply.data ?? preview.data;
  const changes = plan ? plan.sections.reduce((n, s) => n + s.added + s.updated, 0) : 0;

  return (
    <Card className="mt-5 p-5">
      <Eyebrow>Environment package</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        Everything built here, in one file: every envelope and class in full, the job codes and which
        are active, the standards and match decisions, every job description with the original it was
        read from, and the envelope rules. Accounts, admins, the API key and authors&apos; saved JDs
        stay behind. It holds real job descriptions, so keep the file somewhere protected.
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <a className="btn btn-primary btn-sm" download href={appUrl('/api/admin/package/export')}>
          Export package
        </a>
        <label className="btn btn-outline btn-sm">
          Import a package…
          <input
            accept=".gz,application/gzip"
            aria-label="Choose a package to import"
            className="hidden"
            disabled={preview.isPending || apply.isPending}
            onChange={(e) => {
              const chosen = e.target.files?.[0] ?? null;
              e.target.value = '';
              if (chosen) {
                setFile(chosen);
                apply.reset();
                preview.mutate(chosen);
              }
            }}
            type="file"
          />
        </label>
        {preview.isPending ? <span className="text-base text-base-content/65">Comparing with what&apos;s here…</span> : null}
      </div>

      {preview.error || apply.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(apply.error ?? preview.error)}</Note>
        </div>
      ) : null}

      {plan && file ? (
        <div className="mt-4" data-testid="package-plan">
          <p className="text-base">
            {plan.applied ? 'Imported' : 'Importing'} <strong>{file.name}</strong>, exported{' '}
            {new Date(plan.exportedAt).toLocaleString()}.{' '}
            {plan.applied
              ? 'Done.'
              : changes === 0
                ? 'Everything in it is already here: there is nothing to import.'
                : 'Nothing has changed yet. Where both have a record, the more recently changed one is kept; nothing here is removed.'}
          </p>
          <div className="mt-2 overflow-x-auto">
            <table className="table table-sm">
              <thead>
                <tr>
                  <th>Records</th>
                  <th className="text-right">Added</th>
                  <th className="text-right">Updated (newer in file)</th>
                  <th className="text-right">Unchanged</th>
                  <th className="text-right">Kept here (newer here)</th>
                  <th className="text-right">Skipped</th>
                  <th className="text-right">Only here</th>
                </tr>
              </thead>
              <tbody className="tnum">
                {plan.sections.map((s) => (
                  <tr key={s.name}>
                    <td>{s.name}</td>
                    <td className="text-right">{s.added}</td>
                    <td className="text-right">{s.updated}</td>
                    <td className="text-right">{s.same}</td>
                    <td className="text-right">{s.keptHere}</td>
                    <td className="text-right">{s.skipped}</td>
                    <td className="text-right">{s.onlyHere}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {plan.sections.map((s) => (
            <SectionDetails key={s.name} section={s} />
          ))}
          {!plan.applied && changes > 0 ? (
            <div className="mt-3 flex items-center gap-2">
              <button
                className="btn btn-primary btn-sm"
                disabled={apply.isPending}
                onClick={() => apply.mutate(file)}
                type="button"
              >
                {apply.isPending ? 'Importing…' : `Import ${changes} change${changes === 1 ? '' : 's'}`}
              </button>
              <button
                className="btn btn-ghost btn-sm"
                disabled={apply.isPending}
                onClick={() => {
                  setFile(null);
                  preview.reset();
                }}
                type="button"
              >
                Cancel
              </button>
              {apply.isPending ? (
                <span className="text-base text-base-content/65">A whole environment can take a minute.</span>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </Card>
  );
}

/** The records an import updates, keeps, skips or leaves here, by name — the counts alone don't say which. */
const SectionDetails = ({ section }: { section: PackageSection }) => {
  const lists = [
    { items: section.updatedItems, label: 'updated from the file', n: section.updated },
    { items: section.keptHereItems, label: 'kept here (changed more recently here)', n: section.keptHere },
    { items: section.skippedItems, label: 'skipped', n: section.skipped },
    { items: section.onlyHereItems, label: 'only here (not removed)', n: section.onlyHere },
  ].filter((l) => l.n > 0);
  if (lists.length === 0) {
    return null;
  }

  return (
    <details className="mt-2">
      <summary className="cursor-pointer text-base">
        {section.name}: {lists.map((l) => `${l.n} ${l.label}`).join(' · ')}
      </summary>
      {lists.map((l) => (
        <div className="mt-1 pl-4" key={l.label}>
          <div className="text-sm font-semibold text-base-content/65">
            {l.label}
            {l.items.length < l.n ? ` (first ${l.items.length} of ${l.n})` : ''}
          </div>
          <ul className="max-h-48 list-disc overflow-y-auto pl-5 text-sm">
            {l.items.map((i) => (
              <li key={i}>{i}</li>
            ))}
          </ul>
        </div>
      ))}
    </details>
  );
};

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
      <p className="mt-1 text-base text-base-content/65">
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
          placeholder="e.g. rsmith"
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
              <div className="text-base font-semibold">
                {a.loginId}
                {a.displayName ? (
                  <span className="font-normal text-base-content/65"> · {a.displayName}</span>
                ) : null}
              </div>
              <div className="text-sm text-base-content/65">
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
                className="btn btn-ghost btn-sm text-error"
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

const providerName: Record<LlmProvider, string> = {
  anthropic: 'Anthropic (Claude)',
  azureOpenAi: 'Azure OpenAI',
  openAiCompatible: 'OpenAI-compatible server',
};

function ApiKeyPanel() {
  const { data: status } = useApiKeyStatus();
  const save = useSetApiKey();
  const clear = useClearApiKey();
  const [key, setKey] = useState('');
  const error = save.error ?? clear.error;
  const provider = status ? providerName[status.provider] : 'the AI provider';

  const inUse =
    status?.source === 'app'
      ? `Key entered here, ending …${status.lastFour}${
          status.updatedBy ? ` — set by ${status.updatedBy}` : ''
        }${status.updatedAt ? ` on ${when(status.updatedAt)}` : ''}.`
      : status?.source === 'configuration'
        ? `Key from server configuration, ending …${status.lastFour}.`
        : status?.keyRequired === false
          ? 'No key — this server does not need one.'
          : 'No key — intake, classification and tailoring a JD are unavailable.';

  return (
    <Card className="mt-5 p-5">
      <Eyebrow>AI provider</Eyebrow>
      <div className="mt-2 text-base" data-testid="ai-provider">
        <span className="font-semibold">{status ? provider : 'Loading…'}</span>
        {status?.model ? <span className="text-base-content/65"> · {status.model}</span> : null}
        {status?.endpoint ? (
          <span className="text-base-content/50"> · {status.endpoint}</span>
        ) : null}
      </div>
      <p className="mt-1 text-base text-base-content/55">
        Which provider receives JD text is set per environment in server configuration
        (<code>Llm:Provider</code>), not here.
      </p>

      <div className="mt-4 flex items-center gap-2 text-base">
        <Badge
          tone={
            status?.source === 'none' && status.keyRequired
              ? 'red'
              : status?.source === 'app'
                ? 'green'
                : 'accent'
          }
        >
          {status?.source === 'app' ? 'in app' : (status?.source ?? '…')}
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

      {status?.keyEntryAllowed === false ? (
        <p className="mt-3 text-base text-base-content/65" data-testid="key-vault-only">
          Keys for this environment are managed in Azure Key Vault and can&apos;t be entered or
          changed here.
        </p>
      ) : (
        <>
          <p className="mt-3 text-base text-base-content/65">
            A key entered here is checked with {provider}, stored encrypted, and used in place
            of the one in server configuration until it is removed. It can&apos;t be viewed again
            — only its last four characters are shown. Every change is logged.
          </p>
          <form
            className="mt-3 flex flex-wrap items-center gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              save.mutate(key, { onSuccess: () => setKey('') });
            }}
          >
            <input
              aria-label="New API key"
              autoComplete="off"
              className="input input-sm input-bordered w-full max-w-96"
              onChange={(e) => setKey(e.target.value)}
              placeholder={status?.provider === 'anthropic' ? 'sk-ant-…' : 'API key'}
              type="password"
              value={key}
            />
            <button
              className="btn btn-primary btn-sm"
              disabled={!key.trim() || save.isPending}
              type="submit"
            >
              {save.isPending
                ? 'Checking…'
                : status?.source === 'app'
                  ? 'Replace key'
                  : 'Save key'}
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
            <p className="mt-2 text-sm text-base-content/50">
              {status.configurationHasKey
                ? 'Removing it switches back to the key in server configuration.'
                : 'There is no key in server configuration, so removing this one turns model features off.'}
            </p>
          ) : null}
        </>
      )}

      {error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(error)}</Note>
        </div>
      ) : null}

      {status && status.audit.length > 0 ? (
        <div className="mt-4">
          <div className="text-sm font-semibold uppercase tracking-wide text-base-content/50">
            Key changes
          </div>
          <ul className="mt-1 space-y-0.5 text-base" data-testid="key-audit">
            {status.audit.map((a) => (
              <li key={`${a.at}-${a.action}`}>
                {a.action === 'set' ? `Set key ending …${a.lastFour}` : 'Removed the key'} —{' '}
                {a.by ?? 'unknown'}, {when(a.at)}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </Card>
  );
}

function DataProtectionPanel() {
  const { data } = useSecurityStatus();
  const encrypted = data?.databaseEncryptedAtRest;

  return (
    <Card className="mt-5 p-5">
      <Eyebrow>Data protection</Eyebrow>
      <div className="mt-2 flex items-center gap-2 text-base" data-testid="encryption-at-rest">
        <Badge tone={encrypted === true ? 'green' : encrypted === false ? 'yellow' : 'muted'}>
          {encrypted === true ? 'encrypted' : encrypted === false ? 'not encrypted' : 'unknown'}
        </Badge>
        <span>
          {encrypted === true
            ? 'The database is encrypted at rest (SQL Server reports Transparent Data Encryption on).'
            : encrypted === false
              ? 'The database is not encrypted at rest. Expected on a local development database; Azure SQL turns encryption on by default.'
              : 'Encryption at rest could not be determined.'}
        </span>
      </div>
      <p className="mt-3 text-base text-base-content/65">
        Uploaded job description exports and the JD corpus carry position numbers and
        reporting lines. They are kept only in the database — never on the server&apos;s disk —
        so database encryption covers them. The API key entered above is additionally
        encrypted by the application before it is stored.
      </p>
    </Card>
  );
}
