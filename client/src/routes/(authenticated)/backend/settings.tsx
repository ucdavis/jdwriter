import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { usePageTitle } from '@/shared/ui/usePageTitle.ts';
import { isAdmin } from '@/queries/user.ts';
import { Badge, Card, Eyebrow, Note, PageHeader } from '@/shared/ui/primitives.tsx';
import {
  adminsQueryOptions,
  apiKeyQueryOptions,
  useAdmins,
  useApiKeyStatus,
  usePackageApply,
  usePackageInfo,
  usePackagePreview,
  useClearApiKey,
  useGrantAdmin,
  useRevokeAdmin,
  useSecurityStatus,
  useSetApiKey,
  aiCallsQueryOptions,
  useAccessList,
  useApproveWaiting,
  useGrantAccess,
  useRevokeAccess,
} from '@/queries/admin.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { appUrl } from '@/lib/basePath.ts';
import { createFileRoute } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
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
  usePageTitle('Settings · Back end');
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · settings"
        sub="Who can reach the back end, the API key the app uses, and moving everything between environments. Everyone who signs in can author and classify."
        title="Settings"
      />
      <AccessPanel />
      <AdminsPanel />
      <ApiKeyPanel />
      <AiCallsPanel />
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
  const info = usePackageInfo();
  const preview = usePackagePreview();
  const apply = usePackageApply();
  const [file, setFile] = useState<File | null>(null);
  const plan = apply.data ?? preview.data;
  const changes = plan ? plan.sections.reduce((n, s) => n + s.added + s.updated, 0) : 0;

  return (
    <Card className="mt-5 p-5">
      <Eyebrow>Environment package</Eyebrow>
      <p className="mt-1 text-base text-base-content/70">
        Everything built here, in one file: every envelope and class in full, the job codes and which
        are active, the standards and match decisions, every job description with the original it was
        read from, and the envelope rules. Accounts, admins, the API key and authors&apos; saved JDs
        stay behind. It holds real job descriptions, so keep the file somewhere protected — or export
        it scrubbed for the test environment, which replaces position numbers, departments and file
        names and leaves the originals out.
      </p>
      {info.data ? (
        <p className="mt-1 text-base text-base-content/70" data-testid="package-accepts">
          This environment imports{' '}
          {info.data.acceptsRealData && info.data.acceptsScrubbed
            ? 'both kinds (development).'
            : info.data.acceptsRealData
              ? 'packages with real job descriptions only.'
              : 'scrubbed packages only — real job descriptions belong in prod.'}
        </p>
      ) : null}
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <a className="btn btn-primary btn-sm" download href={appUrl('/api/admin/package/export')}>
          Export package
        </a>
        <a className="btn btn-outline btn-sm" download href={appUrl('/api/admin/package/export?scrubbed=true')}>
          Export scrubbed (for test)
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
        {preview.isPending ? <span className="text-base text-base-content/70">Comparing with what&apos;s here…</span> : null}
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
          <div aria-label="What the import would change" className="mt-2 overflow-x-auto" role="region" tabIndex={0}>
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
                <span className="text-base text-base-content/70">A whole environment can take a minute.</span>
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
          <div className="text-sm font-semibold text-base-content/70">
            {l.label}
            {l.items.length < l.n ? ` (first ${l.items.length} of ${l.n})` : ''}
          </div>
          <ul aria-label={l.label} className="max-h-48 list-disc overflow-y-auto pl-5 text-sm" tabIndex={0}>
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

/**
 * The access allow-list (decided 2026-10-28: gated here, not in Azure). Admins always have access.
 * Anyone else who signs in sees only a page to ask, and their request lands in Reports; it can be
 * approved there or here. Changes take effect on the person's next page.
 */
function AccessPanel() {
  const { data } = useAccessList();
  const grant = useGrantAccess();
  const revoke = useRevokeAccess();
  const approveAll = useApproveWaiting();
  const [loginId, setLoginId] = useState('');
  const approved = data?.approved ?? [];
  const waiting = data?.waiting ?? [];
  const error = grant.error ?? revoke.error ?? approveAll.error;

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>Who can use JDWriter</Eyebrow>
      <p className="mt-1 text-base text-base-content/70">
        Only the people listed here, and the admins below, can use JDWriter. Anyone else who signs in is asked to
        request access; their request comes to Reports. Add someone by UC Davis login ID — it takes effect on their
        next page.
      </p>

      <form
        className="mt-4 flex flex-wrap items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          grant.mutate(loginId, { onSuccess: () => setLoginId('') });
        }}
      >
        <input
          aria-label="UC Davis login ID to approve"
          className="input input-sm input-bordered w-64"
          onChange={(e) => setLoginId(e.target.value)}
          placeholder="e.g. rsmith"
          value={loginId}
        />
        <button className="btn btn-primary btn-sm" disabled={!loginId.trim() || grant.isPending} type="submit">
          {grant.isPending ? 'Adding…' : 'Give access'}
        </button>
      </form>

      {error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(error)}</Note>
        </div>
      ) : null}

      {waiting.length > 0 ? (
        <div className="mt-5" data-testid="access-waiting">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-base font-semibold">Signed in without access ({waiting.length})</h3>
            <button
              className="btn btn-outline btn-sm"
              disabled={approveAll.isPending}
              onClick={() => approveAll.mutate()}
              type="button"
            >
              {approveAll.isPending ? 'Approving…' : `Approve all ${waiting.length}`}
            </button>
          </div>
          <ul className="mt-2 divide-y divide-base-300 rounded-lg border border-base-300">
            {waiting.map((w) => (
              <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={w.loginId}>
                <div className="min-w-0">
                  <div className="text-base font-semibold">
                    {w.loginId}
                    {w.displayName ? <span className="font-normal text-base-content/70"> · {w.displayName}</span> : null}
                    {w.requestedAt ? <span className="ml-2"><Badge tone="yellow">asked {when(w.requestedAt)}</Badge></span> : null}
                  </div>
                  <div className="text-sm text-base-content/70">{w.lastSeenAt ? `Last seen ${when(w.lastSeenAt)}` : ''}</div>
                </div>
                <button
                  className="btn btn-primary btn-sm"
                  disabled={grant.isPending}
                  onClick={() => grant.mutate(w.loginId)}
                  type="button"
                >
                  Approve
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <h3 className="mt-5 text-base font-semibold">Approved ({approved.length})</h3>
      {approved.length === 0 ? (
        <p className="mt-1 text-base text-base-content/70">Nobody yet — only the admins can use JDWriter.</p>
      ) : (
        <ul className="mt-2 divide-y divide-base-300 rounded-lg border border-base-300" data-testid="access-approved">
          {approved.map((a) => (
            <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={a.loginId}>
              <div className="min-w-0">
                <div className="text-base font-semibold">
                  {a.loginId}
                  {a.displayName ? <span className="font-normal text-base-content/70"> · {a.displayName}</span> : null}
                </div>
                <div className="text-sm text-base-content/70">
                  {a.lastSeenAt ? `Last seen ${when(a.lastSeenAt)}` : 'Has not signed in yet'}
                  {a.grantedAt ? ` · added ${when(a.grantedAt)}${a.grantedBy ? ` by ${a.grantedBy}` : ''}` : ''}
                </div>
              </div>
              <button
                aria-label={`Remove access for ${a.loginId}`}
                className="btn btn-ghost btn-sm text-error"
                disabled={revoke.isPending}
                onClick={() => revoke.mutate(a.loginId)}
                type="button"
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

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
      <p className="mt-1 text-base text-base-content/70">
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
                  <span className="font-normal text-base-content/70"> · {a.displayName}</span>
                ) : null}
              </div>
              <div className="text-sm text-base-content/70">
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
        {status?.model ? <span className="text-base-content/70"> · {status.model}</span> : null}
        {status?.endpoint ? (
          <span className="text-base-content/70"> · {status.endpoint}</span>
        ) : null}
      </div>
      <p className="mt-1 text-base text-base-content/70">
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
        <p className="mt-3 text-base text-base-content/70" data-testid="key-vault-only">
          Keys for this environment are managed in Azure Key Vault and can&apos;t be entered or
          changed here.
        </p>
      ) : (
        <>
          <p className="mt-3 text-base text-base-content/70">
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
            <p className="mt-2 text-sm text-base-content/70">
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
          <div className="text-sm font-semibold uppercase tracking-wide text-base-content/70">
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
      <p className="mt-3 text-base text-base-content/70">
        Uploaded job description exports and the JD corpus carry position numbers and
        reporting lines. They are kept only in the database — never on the server&apos;s disk —
        so database encryption covers them. The API key entered above is additionally
        encrypted by the application before it is stored.
      </p>
    </Card>
  );
}

const tokens = (v: number | null) => (v === null ? '—' : v.toLocaleString());

/**
 * How long the AI calls are taking: the last 50 on this server, newest first, measured where they're
 * made. Time an AI step by running it, then refreshing here. Metadata only, and gone on restart.
 */
function AiCallsPanel() {
  const calls = useQuery(aiCallsQueryOptions());
  const rows = calls.data?.calls ?? [];

  return (
    <Card className="mt-5 p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Eyebrow>Recent AI calls</Eyebrow>
        <button className="btn btn-ghost btn-sm" disabled={calls.isFetching} onClick={() => void calls.refetch()} type="button">
          {calls.isFetching ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>
      <p className="mt-1 text-base text-base-content/70">
        The last {50} calls on this server, newest first: how long each took and what it used. Run a step — Classify,
        say — then refresh. Kept in memory only, with no content; a restart clears it.
      </p>
      {rows.length === 0 ? (
        <p className="mt-3 text-base text-base-content/70" data-testid="ai-calls-empty">
          No AI calls since the server last started.
        </p>
      ) : (
        <div aria-label="Recent AI calls" className="mt-3 overflow-x-auto" role="region" tabIndex={0}>
          <table className="table table-sm" data-testid="ai-calls">
            <thead>
              <tr>
                <th>When</th>
                <th>Call</th>
                <th className="text-right">Time</th>
                <th className="text-right">Tokens in</th>
                <th className="text-right">Out</th>
                <th className="text-right">Of which thinking</th>
                <th>Effort</th>
                <th>Result</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((c, i) => (
                // Calls have no id; newest first, so the position is stable between refreshes.
                <tr key={`${c.at}-${i}`}>
                  <td className="whitespace-nowrap">{new Date(c.at).toLocaleTimeString()}</td>
                  <td className="font-mono text-sm">{c.label}</td>
                  <td className="text-right tnum">{(c.ms / 1000).toFixed(1)} s</td>
                  <td className="text-right tnum">{tokens(c.inputTokens)}</td>
                  <td className="text-right tnum">{tokens(c.outputTokens)}</td>
                  <td className="text-right tnum">{tokens(c.reasoningTokens)}</td>
                  <td>{c.effort}</td>
                  <td className={c.outcome === 'ok' ? '' : 'text-error'}>{c.outcome}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
