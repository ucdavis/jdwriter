import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import {
  useBootstrapCandidates,
  useBootstrapClass,
  useIngestClass,
  useIngestScan,
  useIngestStandards,
} from '@/queries/admin.ts';
import { useState } from 'react';

type RowStatus = 'done' | 'error' | 'running' | 'waiting';

const statusBadge = (s: RowStatus | undefined) => {
  switch (s) {
    case 'done':
      return <Badge tone="green">ingested</Badge>;
    case 'error':
      return <Badge tone="red">failed</Badge>;
    case 'running':
      return <Badge tone="yellow">ingesting…</Badge>;
    default:
      return <Badge tone="muted">new</Badge>;
  }
};

/**
 * Detects classes dropped into the corpus and ingests them.
 *
 * ONE CLASS PER REQUEST, sequentially. A large class takes a minute or two to parse,
 * consolidate and synthesize, so batching them would let one slow class time out the
 * whole run and leave the operator unable to tell which ones landed.
 */
export const IngestPanel = () => {
  const scan = useIngestScan();
  const ingest = useIngestClass();
  const [status, setStatus] = useState<Record<string, RowStatus>>({});
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const pending = scan.data?.pending ?? [];

  const ingestAll = async () => {
    setRunning(true);
    setError(null);
    for (const c of pending) {
      setStatus((s) => ({ ...s, [c.code]: 'running' }));
      try {
        await ingest.mutateAsync(c.code);
        setStatus((s) => ({ ...s, [c.code]: 'done' }));
      } catch (error_) {
        setStatus((s) => ({ ...s, [c.code]: 'error' }));
        setError(`${c.title}: ${messageOf(error_)}`);
      }
    }
    setRunning(false);
  };

  return (
    <Card className="mb-5 p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <Eyebrow>New JDs</Eyebrow>
          <p className="mt-1 text-[13px] text-base-content/65">
            {scan.isFetching
              ? 'Scanning the corpus for new classes…'
              : pending.length === 0
                ? 'No new classes detected — every class in the corpus is ingested.'
                : `${pending.length} new class${pending.length === 1 ? '' : 'es'} detected.`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            className="btn btn-outline btn-sm"
            disabled={scan.isFetching || running}
            onClick={() => void scan.refetch()}
            type="button"
          >
            {scan.isFetching ? 'Checking…' : 'Check for new JDs'}
          </button>
          {pending.length > 0 ? (
            <button
              className="btn btn-primary btn-sm"
              disabled={running}
              onClick={() => void ingestAll()}
              type="button"
            >
              {running ? 'Ingesting…' : `Review & ingest ${pending.length}`}
            </button>
          ) : null}
        </div>
      </div>

      {error ? (
        <div className="mt-3">
          <Note tone="red">{error}</Note>
        </div>
      ) : null}

      {pending.length > 0 ? (
        <ul className="mt-3 divide-y divide-base-300 rounded-lg border border-base-300">
          {pending.map((c) => (
            <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={c.code}>
              <span className="flex flex-col">
                <span className="text-[13.5px] font-medium">{c.title}</span>
                <span className="text-[11.5px] text-base-content/65 tnum">
                  Code {c.code} · {c.fileCount} JD{c.fileCount === 1 ? '' : 's'}
                </span>
              </span>
              {statusBadge(status[c.code])}
            </li>
          ))}
        </ul>
      ) : null}
      {running ? (
        <p className="mt-2 text-[11.5px] text-base-content/50">
          Each class is parsed, consolidated and synthesized — a large class can take a
          minute or two.
        </p>
      ) : null}
    </Card>
  );
};

export const StandardsPanel = () => {
  const ingest = useIngestStandards();
  const result = ingest.data;

  return (
    <Card className="mb-5 p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <Eyebrow>Official job standards</Eyebrow>
          <p className="mt-1 text-[13px] text-base-content/65">
            Parse UC job-standard workbooks. They augment envelopes with authoritative
            KSAs, education, certifications and scope — the corpus still drives the % time
            responsibilities, which is the actual-JD advantage.
          </p>
        </div>
        <button
          className="btn btn-primary btn-sm whitespace-nowrap"
          disabled={ingest.isPending}
          onClick={() => ingest.mutate()}
          type="button"
        >
          {ingest.isPending ? 'Parsing…' : 'Ingest standards'}
        </button>
      </div>
      {ingest.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(ingest.error)}</Note>
        </div>
      ) : null}
      {result ? (
        <div className="mt-3 flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={result.count ? 'green' : 'muted'}>
              {result.count} standards parsed
            </Badge>
            <Badge tone={result.coded === result.count ? 'green' : 'yellow'}>
              {result.coded} of {result.count} job codes resolved
            </Badge>
            <Badge tone={result.linkedCount ? 'teal' : 'yellow'}>
              {result.linkedCount} of {result.totalClasses} classes linked
            </Badge>
          </div>
          {result.linkedCount > 0 ? (
            <span className="text-[12px] text-base-content/65">
              Linked: {result.linkedSample.join(', ')}
            </span>
          ) : null}
          {result.uncodedSample.length > 0 ? (
            <span className="text-[12px] text-base-content/50">
              {/* Unresolved codes are usually UC-systemwide titles with no UCD
                  equivalent, which is a coverage fact rather than a matching bug. */}
              No job code: {result.uncodedSample.join(', ')}
            </span>
          ) : null}
        </div>
      ) : null}
    </Card>
  );
};

export const BootstrapPanel = () => {
  const bootstrap = useBootstrapCandidates();
  const create = useBootstrapClass();
  const [filter, setFilter] = useState('');
  const candidates = bootstrap.data?.candidates;
  const shown = (candidates ?? []).filter((c) =>
    filter ? c.title.toLowerCase().includes(filter.toLowerCase()) : true
  );
  const error = bootstrap.error ?? create.error;

  const createFor = (title: string) =>
    create.mutate(title, {
      // The created class is no longer a candidate; re-ask rather than editing the list
      // locally, so what is shown is always what the server would offer.
      onSuccess: () => bootstrap.mutate(),
    });

  return (
    <Card className="mb-5 p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <Eyebrow>Bootstrap from a standard</Eyebrow>
          <p className="mt-1 text-[13px] text-base-content/65">
            Create a starter envelope for a class that has an official standard but{' '}
            <span className="font-medium">no job descriptions yet</span>. It is marked
            standard-derived (% time is estimated) and converges to a learned envelope once
            JDs for that class are ingested. Superseded classes are never offered — authoring
            against one would produce a JD under a dead classification.
          </p>
        </div>
        <button
          className="btn btn-outline btn-sm whitespace-nowrap"
          disabled={bootstrap.isPending}
          onClick={() => bootstrap.mutate()}
          type="button"
        >
          {bootstrap.isPending ? 'Looking…' : candidates ? 'Reload list' : 'Find candidates'}
        </button>
      </div>
      {error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(error)}</Note>
        </div>
      ) : null}
      {create.data ? (
        <div className="mt-3">
          <Note tone="green">
            Created {create.data.title} ({create.data.ucJobCode}) from its standard.
          </Note>
        </div>
      ) : null}
      {candidates ? (
        candidates.length === 0 ? (
          <p className="mt-3 text-[12.5px] text-base-content/65">
            No candidates — every standard with a resolvable code already has a profile.
          </p>
        ) : (
          <div className="mt-3">
            <div className="mb-2 flex items-center justify-between gap-3">
              <input
                aria-label="Filter candidates by title"
                className="input input-sm input-bordered flex-1"
                onChange={(e) => setFilter(e.target.value)}
                placeholder="Filter by title…"
                value={filter}
              />
              <span className="whitespace-nowrap text-[12px] text-base-content/50">
                {shown.length} of {candidates.length}
              </span>
            </div>
            <ul className="max-h-[360px] divide-y divide-base-300 overflow-y-auto rounded-lg border border-base-300">
              {shown.map((c) => (
                <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={c.title}>
                  <div className="min-w-0">
                    <div className="truncate text-[13px] font-medium">{c.title}</div>
                    <div className="text-[11.5px] text-base-content/65 tnum">
                      {c.code ? `Code ${c.code}` : 'no code match'}
                      {c.family ? ` · ${c.family}` : ''}
                      {c.grade ? ` · ${c.grade}` : ''}
                    </div>
                  </div>
                  <button
                    className="btn btn-primary btn-sm shrink-0"
                    disabled={create.isPending}
                    onClick={() => createFor(c.title)}
                    type="button"
                  >
                    {create.isPending && create.variables === c.title
                      ? 'Creating…'
                      : 'Create envelope'}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )
      ) : null}
    </Card>
  );
};
