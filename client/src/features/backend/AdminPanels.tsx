import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import {
  useBootstrapCandidates,
  useBootstrapClass,
  useIngestClass,
  useImportDocument,
  useIngestUploaded,
  useUploadStandards,
  useUploadedPending,
  useUploadExports,
  useIngestScan,
  useImportEnvelopes,
  useIngestStandards,
  useRetireSuperseded,
  useRetirementPreview,
} from '@/queries/admin.ts';
import type { EnvelopeImportRefusal, SupersededProfile } from '@/lib/contracts.ts';
import { appUrl } from '@/lib/basePath.ts';
import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';

type RowStatus = 'done' | 'error' | 'running' | 'waiting';

const statusBadge = (
  s: RowStatus | undefined,
  verb: { done: string; running: string } = {
    done: 'ingested',
    running: 'ingesting…',
  }
) => {
  switch (s) {
    case 'done':
      return <Badge tone="green">{verb.done}</Badge>;
    case 'error':
      return <Badge tone="red">failed</Badge>;
    case 'running':
      return <Badge tone="yellow">{verb.running}</Badge>;
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

  // No export directory on this server — the normal case when deployed. Uploads are the
  // path there, so this panel steps aside rather than showing an error.
  if (scan.data && !scan.data.configured) {
    return null;
  }

  return (
    <Card className="mb-5 p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <Eyebrow>New JDs from the corpus folder</Eyebrow>
          <p className="mt-1 text-base text-base-content/65">
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
                <span className="text-base font-semibold">{c.title}</span>
                <span className="text-sm text-base-content/65 tnum">
                  Code {c.code} · {c.fileCount} JD{c.fileCount === 1 ? '' : 's'}
                  {c.replacesStarter ? ' · replaces its starter envelope' : ''}
                </span>
              </span>
              {statusBadge(status[c.code])}
            </li>
          ))}
        </ul>
      ) : null}
      {running ? (
        <p className="mt-2 text-sm text-base-content/50">
          Each class is parsed, consolidated and synthesized — a large class can take a
          minute or two.
        </p>
      ) : null}
    </Card>
  );
};

/**
 * Upload HRTMS exports and ingest them, for servers with no export directory — every
 * deployed one. Uploaded files are stored in the database (encrypted at rest), never on
 * disk, because the exports carry position numbers and reporting lines.
 *
 * Like the folder panel, classes are ingested ONE PER REQUEST, in sequence.
 */
export const UploadPanel = () => {
  const upload = useUploadExports();
  const pending = useUploadedPending();
  const ingest = useIngestUploaded();
  const [status, setStatus] = useState<Record<string, RowStatus>>({});
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const classes = pending.data?.classes ?? [];
  const outcomes = upload.data?.files ?? [];
  const count = (r: string) => outcomes.filter((o) => o.result === r).length;
  const failed = outcomes.filter((o) => o.result === 'failed');

  const send = (list: FileList | null) => {
    if (list && list.length > 0) {
      setStatus({});
      upload.mutate([...list]);
    }
  };

  const ingestAll = async () => {
    setRunning(true);
    setError(null);
    for (const c of classes) {
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
      <Eyebrow>Upload JDs</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        Add HRTMS job description exports (.html) — individual files or a whole folder. New
        classes get an envelope; existing classes are refreshed with the added JDs, and a newer
        export of the same position replaces the old one. Hand-edited envelopes are kept.
      </p>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <label className="btn btn-outline btn-sm">
          Choose files
          <input
            accept=".html,.htm"
            aria-label="Upload HRTMS export files"
            className="hidden"
            disabled={upload.isPending}
            multiple
            onChange={(e) => {
              send(e.target.files);
              e.target.value = '';
            }}
            type="file"
          />
        </label>
        <label className="btn btn-outline btn-sm">
          Choose a folder
          <input
            aria-label="Upload a folder of HRTMS exports"
            className="hidden"
            disabled={upload.isPending}
            multiple
            onChange={(e) => {
              send(e.target.files);
              e.target.value = '';
            }}
            type="file"
            {...{ webkitdirectory: '' }}
          />
        </label>
        {upload.isPending ? (
          <span className="text-base text-base-content/65">Uploading…</span>
        ) : null}
      </div>

      {upload.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(upload.error)}</Note>
        </div>
      ) : null}

      {outcomes.length > 0 ? (
        <div className="mt-3 text-base" data-testid="upload-summary">
          {count('added')} added · {count('duplicate')} already uploaded · {failed.length} not
          usable
          {failed.length > 0 ? (
            <ul className="mt-1 list-disc pl-5 text-error">
              {failed.map((f) => (
                <li key={f.fileName}>
                  {f.fileName}: {f.error}
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}

      {classes.length > 0 ? (
        <>
          <div className="mt-4 flex items-center justify-between gap-3">
            <span className="text-base font-semibold">
              {classes.length} class{classes.length === 1 ? '' : 'es'} with new JDs to build in
            </span>
            <button
              className="btn btn-primary btn-sm"
              disabled={running}
              onClick={() => void ingestAll()}
              type="button"
            >
              {running ? 'Ingesting…' : `Ingest ${classes.length}`}
            </button>
          </div>
          <ul className="mt-2 divide-y divide-base-300 rounded-lg border border-base-300">
            {classes.map((c) => (
              <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={c.code}>
                <span className="flex flex-col">
                  <span className="text-base font-semibold">{c.title}</span>
                  <span className="text-sm text-base-content/65 tnum">
                    Code {c.code} ·{' '}
                    {[
                      c.newFiles > 0 ? `${c.newFiles} uploaded` : null,
                      c.newAuthored > 0 ? `${c.newAuthored} written in the app` : null,
                      c.newClassified > 0 ? `${c.newClassified} from Classify` : null,
                      c.newImported > 0 ? `${c.newImported} from other units` : null,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                    {c.replacesStarter
                      ? ' · replaces its starter envelope'
                      : c.existingSlug
                        ? ` · refreshes a class with ${c.corpusJds} JDs`
                        : ' · new class'}
                  </span>
                  {c.hasManualEnvelope ? (
                    <span className="text-sm text-warning">
                      This class&apos;s envelope was edited by hand — rebuilding replaces those edits.
                    </span>
                  ) : null}
                </span>
                {statusBadge(status[c.code])}
              </li>
            ))}
          </ul>
        </>
      ) : null}

      {error ? (
        <div className="mt-3">
          <Note tone="red">{error}</Note>
        </div>
      ) : null}
      {running ? (
        <p className="mt-2 text-sm text-base-content/50">
          Each class is consolidated and its envelope rebuilt — a large class can take a minute
          or two.
        </p>
      ) : null}
    </Card>
  );
};

/** What a folder can hold that this panel reads. Everything else is skipped, not failed. */
const DOCUMENT_TYPES = ['.docx', '.pdf', '.txt'];

const isDocument = (file: File) =>
  !file.name.startsWith('.') &&
  // Word's lock file for an open document ("~$Analyst.docx") is not a document.
  !file.name.startsWith('~$') &&
  DOCUMENT_TYPES.some((ext) => file.name.toLowerCase().endsWith(ext));

type DocumentRow = {
  detail: string | null;
  name: string;
  status: 'added' | 'duplicate' | 'failed' | 'running' | 'waiting';
};

/**
 * JDs from units outside the college. Those can't be exported from HRTMS; they arrive as copies out
 * of JDX, saved from Word. Each document is filed under the UC job title it states, and the AI reads
 * its responsibilities, % time and qualifications into the corpus.
 *
 * ONE DOCUMENT PER REQUEST, sequentially, as the ingest panels do: each is a model call, and a
 * folder of them sent as one request would let one slow file time out the lot.
 */
export const DocumentImportPanel = () => {
  const importDocument = useImportDocument();
  const queryClient = useQueryClient();
  const [rows, setRows] = useState<DocumentRow[]>([]);
  const [skipped, setSkipped] = useState(0);
  const [running, setRunning] = useState(false);
  const stop = useRef(false);

  const update = (i: number, row: Partial<DocumentRow>) =>
    setRows((r) => r.map((x, k) => (k === i ? { ...x, ...row } : x)));

  const run = async (list: FileList | null) => {
    const all = [...(list ?? [])];
    const files = all.filter(isDocument);
    setSkipped(all.length - files.length);
    setRows(files.map((f) => ({ detail: null, name: f.webkitRelativePath || f.name, status: 'waiting' })));
    if (files.length === 0) {
      return;
    }

    stop.current = false;
    setRunning(true);
    for (const [i, file] of files.entries()) {
      if (stop.current) {
        break;
      }

      update(i, { status: 'running' });
      try {
        const outcome = await importDocument.mutateAsync(file);
        update(i, {
          detail:
            outcome.result === 'added'
              ? `${outcome.title ?? ''} (${outcome.ucJobCode ?? ''})`
              : outcome.result === 'duplicate'
                ? 'Already added'
                : outcome.error,
          status: outcome.result,
        });
      } catch (error_) {
        update(i, { detail: messageOf(error_), status: 'failed' });
      }
    }

    setRunning(false);
    void queryClient.invalidateQueries({ queryKey: ['admin', 'uploads', 'pending'] });
  };

  const done = rows.filter((r) => r.status !== 'waiting' && r.status !== 'running').length;
  const count = (s: DocumentRow['status']) => rows.filter((r) => r.status === s).length;
  const tone = { added: 'green', duplicate: 'muted', failed: 'red', running: 'yellow', waiting: 'muted' } as const;
  const label = { added: 'added', duplicate: 'already added', failed: 'failed', running: 'reading…', waiting: 'waiting' };

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>Add JDs from other units</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        For job descriptions that can&apos;t be exported from HRTMS, such as copies out of JDX.
        Save each from Word as a Word Document (.docx); PDF and text files work too. Add files or a
        whole folder: each is filed under the UC job title it states, and the AI reads its
        responsibilities, % time and qualifications. A document whose title doesn&apos;t match a
        single UC title is listed, not guessed at. Added JDs appear above, ready to build in.
      </p>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <label className="btn btn-outline btn-sm">
          Choose files
          <input
            accept={DOCUMENT_TYPES.join(',')}
            aria-label="Add job description documents"
            className="hidden"
            disabled={running}
            multiple
            onChange={(e) => {
              void run(e.target.files);
              e.target.value = '';
            }}
            type="file"
          />
        </label>
        <label className="btn btn-outline btn-sm">
          Choose a folder
          <input
            aria-label="Add a folder of job description documents"
            className="hidden"
            disabled={running}
            multiple
            onChange={(e) => {
              void run(e.target.files);
              e.target.value = '';
            }}
            type="file"
            {...{ webkitdirectory: '' }}
          />
        </label>
        {running ? (
          <>
            <span className="text-base text-base-content/65 tnum">
              Reading {Math.min(done + 1, rows.length)} of {rows.length}…
            </span>
            <button
              className="btn btn-ghost btn-sm"
              onClick={() => {
                stop.current = true;
              }}
              type="button"
            >
              Stop
            </button>
          </>
        ) : null}
      </div>

      {rows.length > 0 && !running ? (
        <p className="mt-3 text-base" data-testid="document-summary">
          {count('added')} added · {count('duplicate')} already added · {count('failed')} not usable
          {skipped > 0 ? ` · ${skipped} other file${skipped === 1 ? '' : 's'} skipped` : ''}
        </p>
      ) : null}

      {rows.length > 0 ? (
        <ul className="mt-2 max-h-96 divide-y divide-base-300 overflow-y-auto rounded-lg border border-base-300">
          {rows.map((r) => (
            <li className="flex items-start justify-between gap-3 px-3 py-2" key={r.name}>
              <span className="flex min-w-0 flex-col">
                <span className="truncate text-base">{r.name}</span>
                {r.detail ? (
                  <span
                    className={`text-sm ${r.status === 'failed' ? 'text-error' : 'text-base-content/65'}`}
                  >
                    {r.detail}
                  </span>
                ) : null}
              </span>
              <Badge tone={tone[r.status]}>{label[r.status]}</Badge>
            </li>
          ))}
        </ul>
      ) : null}
    </Card>
  );
};

export const StandardsPanel = () => {
  const ingest = useIngestStandards();
  const upload = useUploadStandards();
  const result = ingest.data;
  const uploaded = upload.data;
  const failed = uploaded?.files.filter((f) => f.result === 'failed') ?? [];

  return (
    <Card className="mb-5 p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <Eyebrow>Official job standards</Eyebrow>
          <p className="mt-1 text-base text-base-content/65">
            Upload UC job-standard workbooks (Job Builder .xlsx exports). They augment envelopes
            with authoritative KSAs, education, certifications and scope — the corpus still
            drives the % time responsibilities, which is the actual-JD advantage. Uploads add
            to the existing standards; a standard with the same title is replaced.
          </p>
        </div>
        <div className="flex shrink-0 flex-col items-end gap-2">
          <label className="btn btn-primary btn-sm whitespace-nowrap">
            {upload.isPending ? 'Uploading…' : 'Upload workbooks'}
            <input
              accept=".xlsx"
              aria-label="Upload job standards workbooks"
              className="hidden"
              disabled={upload.isPending}
              multiple
              onChange={(e) => {
                if (e.target.files && e.target.files.length > 0) {
                  upload.mutate([...e.target.files]);
                }
                e.target.value = '';
              }}
              type="file"
            />
          </label>
          <button
            className="btn btn-ghost btn-sm whitespace-nowrap"
            disabled={ingest.isPending}
            onClick={() => ingest.mutate()}
            title="Rebuild every standard from the workbook folder configured on the server"
            type="button"
          >
            {ingest.isPending ? 'Parsing…' : 'Rebuild from server folder'}
          </button>
        </div>
      </div>
      {upload.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(upload.error)}</Note>
        </div>
      ) : null}
      {uploaded ? (
        <div className="mt-3 flex flex-col gap-2" data-testid="standards-upload-summary">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone="green">{uploaded.added} added</Badge>
            <Badge tone="accent">{uploaded.updated} updated</Badge>
            <Badge tone="muted">{uploaded.total} standards in total</Badge>
            <Badge tone={uploaded.linkedCount ? 'teal' : 'yellow'}>
              {uploaded.linkedCount} of {uploaded.totalClasses} classes linked
            </Badge>
          </div>
          {failed.length > 0 ? (
            <ul className="list-disc pl-5 text-base text-error">
              {failed.map((f) => (
                <li key={f.fileName}>
                  {f.fileName}: {f.error}
                </li>
              ))}
            </ul>
          ) : null}
          {uploaded.files.some((f) => f.result === 'duplicate') ? (
            <span className="text-base text-base-content/50">
              {uploaded.files.filter((f) => f.result === 'duplicate').length} workbook(s) had
              already been uploaded.
            </span>
          ) : null}
        </div>
      ) : null}
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
            <span className="text-base text-base-content/65">
              Linked: {result.linkedSample.join(', ')}
            </span>
          ) : null}
          {result.uncodedSample.length > 0 ? (
            <span className="text-base text-base-content/50">
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

const retireOutcome = (p: SupersededProfile) => {
  const successor = `${p.successorTitle} (${p.successorCode})`;
  switch (p.action) {
    case 'merge':
      return `Merge into ${successor}`;
    case 'reidentify':
      return `Becomes ${successor}`;
    case 'remove':
      return `Remove — ${successor} is created from its own standard`;
  }
};

/**
 * Classes filed under a non-represented code that a union-designated successor (RP, CX, TX,
 * RX, HX) has superseded. Supersession is derived from the title reference, so a class can
 * become dead after it was built; this retires it without losing its JDs or saved JDs.
 */
export const SupersessionPanel = () => {
  const preview = useRetirementPreview();
  const retire = useRetireSuperseded();
  const shown = retire.data ?? preview.data;
  const pending = retire.data ? [] : (preview.data?.profiles ?? []);
  const error = preview.error ?? retire.error;
  const rebuild = retire.data?.profiles.filter((p) => p.needsRebuild) ?? [];

  return (
    <Card className="mb-5 p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <Eyebrow>Superseded by a union title</Eyebrow>
          <p className="mt-1 text-base text-base-content/65">
            Non-represented classes replaced by a union-designated successor —
            RP, CX, TX, RX or HX after the title. Retiring one refiles its JDs
            under the successor, then merges, renames or removes the old class;
            saved JDs move with it. Create all does this first.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button
            className="btn btn-outline btn-sm whitespace-nowrap"
            disabled={preview.isPending || retire.isPending}
            onClick={() => {
              retire.reset();
              preview.mutate();
            }}
            type="button"
          >
            {preview.isPending ? 'Checking…' : 'Check'}
          </button>
          {pending.length > 0 ? (
            <button
              className="btn btn-primary btn-sm whitespace-nowrap"
              disabled={retire.isPending}
              onClick={() => retire.mutate()}
              type="button"
            >
              {retire.isPending ? 'Retiring…' : `Retire ${pending.length}`}
            </button>
          ) : null}
        </div>
      </div>
      {error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(error)}</Note>
        </div>
      ) : null}
      {retire.data ? (
        <div className="mt-3 flex flex-col gap-2">
          <Note tone="green">
            Retired {retire.data.profiles.length} class
            {retire.data.profiles.length === 1 ? '' : 'es'} and refiled{' '}
            {retire.data.refiledJds} JD{retire.data.refiledJds === 1 ? '' : 's'}
            .
          </Note>
          {rebuild.length > 0 ? (
            <Note tone="yellow">
              Rebuild from the corpus to learn from the refiled JDs:{' '}
              {rebuild.map((p) => p.successorTitle).join(', ')}.
            </Note>
          ) : null}
        </div>
      ) : shown && shown.profiles.length === 0 ? (
        <p className="mt-3 text-base text-base-content/65">
          Nothing to retire — no class is filed under a superseded code.
        </p>
      ) : null}
      {shown && shown.profiles.length > 0 ? (
        <ul className="mt-3 divide-y divide-base-300 rounded-lg border border-base-300">
          {shown.profiles.map((p) => (
            <li
              className="flex items-center justify-between gap-3 px-3 py-2.5"
              key={p.slug}
            >
              <div className="min-w-0">
                <div className="truncate text-base font-semibold">
                  {p.title} ({p.code})
                </div>
                <div className="text-sm text-base-content/65 tnum">
                  {retireOutcome(p)}
                  {p.corpusJds
                    ? ` · ${p.corpusJds} corpus JD${p.corpusJds === 1 ? '' : 's'}`
                    : ''}
                  {p.authoredJds
                    ? ` · ${p.authoredJds} saved JD${p.authoredJds === 1 ? '' : 's'}`
                    : ''}
                </div>
              </div>
              {retire.data ? <Badge tone="green">retired</Badge> : null}
            </li>
          ))}
        </ul>
      ) : null}
    </Card>
  );
};

export const BootstrapPanel = () => {
  const bootstrap = useBootstrapCandidates();
  const create = useBootstrapClass();
  const retire = useRetireSuperseded();
  const [filter, setFilter] = useState('');
  const [status, setStatus] = useState<Record<string, RowStatus>>({});
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);
  const [summary, setSummary] = useState<{
    created: number;
    failed: number;
    retired: number;
    stopped: boolean;
  } | null>(null);
  const stopRequested = useRef(false);
  const candidates = bootstrap.data?.candidates;
  const shown = (candidates ?? []).filter((c) =>
    filter ? c.title.toLowerCase().includes(filter.toLowerCase()) : true
  );
  // Every candidate is on UC Davis payroll: the server leaves out classes UC Davis can't use.
  const all = candidates ?? [];
  const leftOut = (bootstrap.data?.notOnPayroll ?? 0) + (bootstrap.data?.noCodeMatch ?? 0);
  const mutationError = bootstrap.error ?? create.error;
  const error =
    runError ?? (running || !mutationError ? null : messageOf(mutationError));

  /**
   * ONE CLASS PER REQUEST, in sequence, like corpus ingest: each envelope is a model call,
   * and a single bulk request would let one slow class time out the whole run and leave the
   * operator unable to tell which ones landed.
   */
  const createAll = async () => {
    if (
      !window.confirm(
        `Create starter envelopes for all ${all.length} candidate classes? ` +
          'Superseded classes are retired first. Each envelope is a model call, so this can ' +
          'take a while; you can stop it part-way.'
      )
    ) {
      return;
    }

    setRunning(true);
    setRunError(null);
    setSummary(null);
    setStatus({});
    stopRequested.current = false;

    let retired = 0;
    try {
      retired = (await retire.mutateAsync()).profiles.length;
    } catch (error_) {
      setRunError(`Retiring superseded classes failed: ${messageOf(error_)}`);
      setRunning(false);
      return;
    }

    // Retiring can surface successors a dead class was hiding, so re-ask before creating.
    let list;
    try {
      list = (await bootstrap.mutateAsync()).candidates;
    } catch (error_) {
      setRunError(messageOf(error_));
      setRunning(false);
      return;
    }

    let created = 0;
    let failed = 0;
    for (const c of list) {
      if (stopRequested.current) {
        break;
      }
      setStatus((s) => ({ ...s, [c.title]: 'running' }));
      try {
        await create.mutateAsync(c.title);
        created++;
        setStatus((s) => ({ ...s, [c.title]: 'done' }));
      } catch (error_) {
        failed++;
        setStatus((s) => ({ ...s, [c.title]: 'error' }));
        setRunError(`${c.title}: ${messageOf(error_)}`);
      }
    }

    setSummary({ created, failed, retired, stopped: stopRequested.current });
    setRunning(false);
    // Created classes drop out; failed ones stay, still marked.
    bootstrap.mutate();
  };

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
          <p className="mt-1 text-base text-base-content/65">
            Create a starter envelope for a class that has an official standard but{' '}
            <span className="font-semibold">no job descriptions yet</span>. It is marked
            standard-derived (% time is estimated) and converges to a learned envelope once
            JDs for that class are ingested. Superseded classes are never offered — authoring
            against one would produce a JD under a dead classification.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button
            className="btn btn-outline btn-sm whitespace-nowrap"
            disabled={bootstrap.isPending || running}
            onClick={() => bootstrap.mutate()}
            type="button"
          >
            {bootstrap.isPending && !running
              ? 'Looking…'
              : candidates
                ? 'Reload list'
                : 'Find candidates'}
          </button>
          {running ? (
            <button
              className="btn btn-ghost btn-sm whitespace-nowrap"
              onClick={() => {
                stopRequested.current = true;
              }}
              type="button"
            >
              Stop after this one
            </button>
          ) : all.length > 0 ? (
            <button
              className="btn btn-primary btn-sm whitespace-nowrap"
              disabled={create.isPending}
              onClick={() => void createAll()}
              type="button"
            >
              Create all {all.length}
            </button>
          ) : null}
        </div>
      </div>
      {error ? (
        <div className="mt-3">
          <Note tone="red">{error}</Note>
        </div>
      ) : null}
      {summary ? (
        <div className="mt-3">
          <Note tone={summary.failed ? 'yellow' : 'green'}>
            {summary.stopped ? 'Stopped. ' : ''}Created {summary.created} class
            {summary.created === 1 ? '' : 'es'} from their standards
            {summary.failed ? `; ${summary.failed} failed` : ''}
            {summary.retired
              ? `. Retired ${summary.retired} superseded class${summary.retired === 1 ? '' : 'es'} first`
              : ''}
            .
          </Note>
        </div>
      ) : create.data && !running ? (
        <div className="mt-3">
          <Note tone="green">
            Created {create.data.title} ({create.data.ucJobCode}) from its standard.
          </Note>
        </div>
      ) : null}
      {running ? (
        <p className="mt-2 text-sm text-base-content/50">
          Creating {Object.values(status).filter((v) => v === 'done').length} of{' '}
          {all.length}… each envelope is a model call.
        </p>
      ) : null}
      {leftOut > 0 ? (
        <p className="mt-3 text-base text-base-content/65" data-testid="bootstrap-left-out">
          Only classes on UC Davis payroll are listed. {leftOut} standard
          {leftOut === 1 ? ' is' : 's are'} left out: their titles aren&apos;t used at UC Davis, so
          an envelope for them could never be used.
        </p>
      ) : null}
      {candidates ? (
        candidates.length === 0 ? (
          <p className="mt-3 text-base text-base-content/65">
            No candidates — every standard for a class on UC Davis payroll already has a profile.
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
              <span className="whitespace-nowrap text-base text-base-content/50">
                {shown.length} of {candidates.length}
              </span>
            </div>
            <ul className="max-h-[360px] divide-y divide-base-300 overflow-y-auto rounded-lg border border-base-300">
              {shown.map((c) => (
                <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={c.title}>
                  <div className="min-w-0">
                    <div className="truncate text-base font-semibold">{c.title}</div>
                    <div className="text-sm text-base-content/65 tnum">
                      Code {c.code}
                      {c.family ? ` · ${c.family}` : ''}
                      {c.grade ? ` · ${c.grade}` : ''}
                    </div>
                  </div>
                  {status[c.title] ? (
                    statusBadge(status[c.title], {
                      done: 'created',
                      running: 'creating…',
                    })
                  ) : (
                    <button
                      className="btn btn-primary btn-sm shrink-0"
                      disabled={create.isPending || running}
                      onClick={() => createFor(c.title)}
                      type="button"
                    >
                      {create.isPending && create.variables === c.title
                        ? 'Creating…'
                        : 'Create envelope'}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </div>
        )
      ) : null}
    </Card>
  );
};

const refusalLabel: Record<EnvelopeImportRefusal, string> = {
  codeMismatch: 'different job code here',
  exists: 'already has a class',
  invalid: 'invalid envelope',
  noStandard: 'no standard here',
  notOnPayroll: 'not on UC Davis payroll',
  superseded: 'superseded here',
};

/**
 * Bootstrap where it is cheap and safe — locally — then move the results. Only the envelope
 * travels: the receiving environment rebuilds each class from its own standard, refuses
 * superseded codes, and never overwrites a class it already has.
 */
export const EnvelopeTransferPanel = () => {
  const importEnvelopes = useImportEnvelopes();
  const result = importEnvelopes.data;

  return (
    <Card className="mb-5 p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <Eyebrow>Move envelopes between environments</Eyebrow>
          <p className="mt-1 text-base text-base-content/65">
            Download every standard-derived envelope here, then import the file in another
            environment — bootstrap locally, load into production, and pay for each envelope
            once. Each class is rebuilt from that environment&apos;s own standard; existing and
            superseded classes are skipped. The file holds system output: never commit it.
          </p>
        </div>
        <div className="flex shrink-0 flex-col items-end gap-2">
          <a
            className="btn btn-outline btn-sm whitespace-nowrap"
            download
            href={appUrl('/api/admin/envelopes/export')}
          >
            Download envelopes
          </a>
          <label className="btn btn-primary btn-sm whitespace-nowrap">
            {importEnvelopes.isPending ? 'Importing…' : 'Import envelopes'}
            <input
              accept=".json,application/json"
              aria-label="Import an envelope export"
              className="hidden"
              disabled={importEnvelopes.isPending}
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) {
                  importEnvelopes.mutate(file);
                }
                e.target.value = '';
              }}
              type="file"
            />
          </label>
        </div>
      </div>
      {importEnvelopes.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(importEnvelopes.error)}</Note>
        </div>
      ) : null}
      {result ? (
        <div className="mt-3 flex flex-col gap-2" data-testid="envelope-import-summary">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={result.created.length ? 'green' : 'muted'}>
              {result.created.length} class{result.created.length === 1 ? '' : 'es'} created
            </Badge>
            {result.skipped.length > 0 ? (
              <Badge tone="yellow">{result.skipped.length} skipped</Badge>
            ) : null}
          </div>
          {result.skipped.length > 0 ? (
            <ul className="text-base text-base-content/65">
              {result.skipped.map((s) => (
                <li key={s.title} title={s.message}>
                  {s.title} — {refusalLabel[s.reason]}
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </Card>
  );
};
