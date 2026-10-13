import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import {
  useBootstrapCandidates,
  useBlankStandardClasses,
  useBootstrapClass,
  useDecideStandardMatch,
  useStandardMatches,
  useUndoStandardMatch,
  useImportDocument,
  useQualificationRules,
  useReparseDocuments,
  useIngestUploaded,
  useUploadStandards,
  useUploadedPending,
  useIngestStandards,
  useRemoveBlankStandardClasses,
  useRetireSuperseded,
  useRetirementPreview,
} from '@/queries/admin.ts';
import type { SupersededProfile } from '@/lib/contracts.ts';
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
 * Classes with JDs added since they were last built, waiting to be re-ingested: new documents,
 * JDs written in the app, Classify submissions. Re-ingesting rebuilds the class's envelope from all
 * of its JDs.
 *
 * ONE CLASS PER REQUEST, sequentially. A large class takes a minute or two to consolidate and
 * synthesize, so batching them would let one slow class time out the whole run and leave the
 * operator unable to tell which ones landed.
 */
export const PendingRebuildsPanel = ({ hideWhenEmpty = false }: { hideWhenEmpty?: boolean }) => {
  const pending = useUploadedPending();
  const ingest = useIngestUploaded();
  const [status, setStatus] = useState<Record<string, RowStatus>>({});
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const classes = pending.data?.classes ?? [];
  // Rows stay while a run is under way, so each shows how it went.
  const [shown, setShown] = useState<typeof classes | null>(null);
  const list = shown ?? classes;

  const ingestAll = async () => {
    setRunning(true);
    setError(null);
    setShown(classes);
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

  if (hideWhenEmpty && list.length === 0) {
    return null;
  }

  return (
    <Card className="mb-5 p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Eyebrow>Waiting to be re-ingested</Eyebrow>
          <p className="mt-1 text-base text-base-content/65">
            {pending.isLoading
              ? 'Checking for new JDs…'
              : list.length === 0
                ? 'Every class is up to date with its JDs.'
                : `${list.length} class${list.length === 1 ? ' has' : 'es have'} new JDs not yet in ${
                    list.length === 1 ? 'its' : 'their'
                  } envelope. Re-ingesting rebuilds each envelope from all of its JDs.`}
          </p>
        </div>
        {classes.length > 0 ? (
          <button
            className="btn btn-primary btn-sm"
            disabled={running}
            onClick={() => void ingestAll()}
            type="button"
          >
            {running ? 'Re-ingesting…' : `Re-ingest ${classes.length}`}
          </button>
        ) : null}
      </div>

      {list.length > 0 ? (
        <ul className="mt-3 max-h-96 divide-y divide-base-300 overflow-y-auto rounded-lg border border-base-300">
          {list.map((c) => (
            <li className="flex items-center justify-between gap-3 px-3 py-2.5" key={c.code}>
              <span className="flex flex-col">
                <span className="text-base font-semibold">{c.title}</span>
                <span className="text-sm text-base-content/65 tnum">
                  Code {c.code} ·{' '}
                  {[
                    c.newImported > 0 ? `${c.newImported} new JD${c.newImported === 1 ? '' : 's'}` : null,
                    c.newFiles > 0 ? `${c.newFiles} uploaded` : null,
                    c.newAuthored > 0 ? `${c.newAuthored} written in the app` : null,
                    c.newClassified > 0 ? `${c.newClassified} from Classify` : null,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                  {c.replacesStarter
                    ? ' · replaces its starter envelope'
                    : c.existingSlug
                      ? ` · ${c.corpusJds} JDs in all`
                      : ' · new class'}
                </span>
                {c.hasManualEnvelope ? (
                  <span className="text-sm text-warning">
                    This class&apos;s envelope was edited by hand — re-ingesting replaces those edits.
                  </span>
                ) : null}
              </span>
              {statusBadge(status[c.code], { done: 're-ingested', running: 're-ingesting…' })}
            </li>
          ))}
        </ul>
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
 * How job descriptions come in: JD exports copied out of JDX into Word. Each is parsed from its
 * layout, verbatim, and filed under the job code it states; only an unrecognized layout goes to the AI.
 *
 * ONE DOCUMENT PER REQUEST, sequentially: a folder sent as one request would let one slow file
 * (an AI read) time out the lot, and each row reports its own result.
 */
export const DocumentImportPanel = () => {
  const importDocument = useImportDocument();
  const reparse = useReparseDocuments();
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
              ? `${outcome.title ?? ''} (${outcome.ucJobCode ?? ''}) · ${
                  outcome.readBy === 'ai' ? 'read by AI (unrecognized layout)' : 'parsed'
                }`
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
    void queryClient.invalidateQueries({ queryKey: ['admin', 'corpus'] });
  };

  const done = rows.filter((r) => r.status !== 'waiting' && r.status !== 'running').length;
  const count = (s: DocumentRow['status']) => rows.filter((r) => r.status === s).length;
  const tone = { added: 'green', duplicate: 'muted', failed: 'red', running: 'yellow', waiting: 'muted' } as const;
  const label = { added: 'added', duplicate: 'already added', failed: 'failed', running: 'reading…', waiting: 'waiting' };

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>Add job descriptions</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        JD exports copied out of JDX into Word (.docx) — files or a whole folder. Each is parsed
        straight from its layout — no AI, wording kept exactly — and filed under the job code it
        states. A document with no responsibilities or no duties (a job posting, for one) is
        incomplete and not added. Only a layout the parser doesn&apos;t recognize is read by the AI.
        Added JDs wait below until their class is re-ingested.
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <button
          className="btn btn-ghost btn-sm"
          disabled={reparse.isPending || running}
          onClick={() => {
            if (
              window.confirm(
                'Re-read every imported Word document with the parser? Earlier imports read by the AI become verbatim records; incomplete ones (no duties) are removed from the corpus.'
              )
            ) {
              reparse.mutate();
            }
          }}
          type="button"
        >
          {reparse.isPending ? 'Re-reading…' : 'Re-read imported documents'}
        </button>
      </div>
      {reparse.error ? (
        <div className="mt-2">
          <Note tone="red">{messageOf(reparse.error)}</Note>
        </div>
      ) : null}
      {reparse.data ? (
        <div className="mt-2" data-testid="reparse-summary">
          <Note tone="green">
            Re-read {reparse.data.reparsed} · discarded {reparse.data.discarded.length} incomplete
            {reparse.data.refiled.length > 0 ? ` · ${reparse.data.refiled.length} moved to the class their code states` : ''}
            {reparse.data.unrecognized > 0 ? ` · ${reparse.data.unrecognized} not recognized, left as they were` : ''}.
          </Note>
          {reparse.data.discarded.length > 0 ? (
            <details className="mt-2">
              <summary className="cursor-pointer text-base">Discarded ({reparse.data.discarded.length})</summary>
              <ul className="mt-1 max-h-60 list-disc overflow-y-auto pl-5 text-sm text-base-content/75">
                {reparse.data.discarded.map((d) => (
                  <li key={d.fileName}>
                    {d.fileName}: {d.reason}
                  </li>
                ))}
              </ul>
            </details>
          ) : null}
        </div>
      ) : null}

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

/**
 * The education house rules, applied to envelopes written before they existed. New, rebuilt,
 * bootstrapped, moved-in and hand-edited envelopes follow them automatically.
 */
export const QualificationRulesPanel = () => {
  const rules = useQualificationRules();
  const summary = rules.data;

  const apply = () => {
    if (
      summary &&
      window.confirm(
        `Apply the education rules to ${summary.changed} envelope${summary.changed === 1 ? '' : 's'}? ` +
          'Preferred degrees move to preferred qualifications, and degrees gain “or equivalent experience”.'
      )
    ) {
      rules.mutate(true);
    }
  };

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>Education rules</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        A preferred degree belongs in preferred qualifications, not education, and every degree
        reads “or equivalent experience”. New and edited envelopes follow these rules
        automatically; this applies them to envelopes written before.
      </p>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <button
          className="btn btn-outline btn-sm"
          disabled={rules.isPending}
          onClick={() => rules.mutate(false)}
          type="button"
        >
          {rules.isPending && !summary ? 'Checking…' : 'Preview'}
        </button>
        {summary && !summary.applied && summary.changed > 0 ? (
          <button
            className="btn btn-primary btn-sm"
            disabled={rules.isPending}
            onClick={apply}
            type="button"
          >
            Apply to {summary.changed}
          </button>
        ) : null}
      </div>

      {rules.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(rules.error)}</Note>
        </div>
      ) : null}

      {summary ? (
        <div className="mt-3">
          <Note tone={summary.applied ? 'green' : 'yellow'}>
            {summary.changed === 0
              ? `All ${summary.envelopes} envelopes already follow the rules.`
              : `${summary.applied ? 'Applied to' : 'Would change'} ${summary.changed} of ${summary.envelopes} envelopes: ` +
                `${summary.movedToPreferred} preferred degree${summary.movedToPreferred === 1 ? '' : 's'} moved to preferred qualifications, ` +
                `${summary.equivalentAdded} degree${summary.equivalentAdded === 1 ? '' : 's'} given “or equivalent experience”.`}
          </Note>
        </div>
      ) : null}

      {summary && !summary.applied && summary.examples.length > 0 ? (
        <ul className="mt-3 max-h-96 space-y-3 overflow-y-auto" data-testid="qualification-examples">
          {summary.examples.map((e) => (
            <li className="rounded-lg border border-base-300 p-3" key={e.slug}>
              <div className="font-semibold">{e.title}</div>
              <div className="mt-1 text-sm text-base-content/50">Education</div>
              <ul className="list-disc pl-5 text-sm">
                {e.educationAfter.map((t) => (
                  <li key={t}>{t}</li>
                ))}
              </ul>
              {e.preferredChanged.length > 0 ? (
                <>
                  <div className="mt-1 text-sm text-base-content/50">Preferred qualifications</div>
                  <ul className="list-disc pl-5 text-sm">
                    {e.preferredChanged.map((t) => (
                      <li key={t}>{t}</li>
                    ))}
                  </ul>
                </>
              ) : null}
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
/** How many standards the match review renders before asking for a filter. */
const MATCH_LIMIT = 100;

/**
 * Standards whose titles match no job code exactly — a spelling the matcher doesn't know, a typo,
 * a reworded title — with up to three codes each might be, for an admin to confirm. Suggestions
 * share the standard's level, union suffix and site, and are ranked by how alike the titles are;
 * near titles can still be different jobs, so nothing is matched without a person. A confirmed
 * match gives the standard its code everywhere and survives re-uploading the workbooks.
 */
export const StandardMatchPanel = () => {
  const [includeNotActive, setIncludeNotActive] = useState(false);
  const [filter, setFilter] = useState('');
  const review = useStandardMatches(includeNotActive);
  const decide = useDecideStandardMatch();
  const undo = useUndoStandardMatch();
  const [typed, setTyped] = useState<Record<string, string>>({});
  const [rejecting, setRejecting] = useState<string | null>(null);

  const unmatched = (review.data?.unmatched ?? []).filter((u) =>
    filter ? u.standardTitle.toLowerCase().includes(filter.toLowerCase()) : true
  );
  const busy = decide.isPending || undo.isPending || rejecting !== null;

  /** "None of these": reject each suggestion shown, so the standard isn't offered them again. */
  const rejectAll = async (standardTitle: string, codes: string[]) => {
    setRejecting(standardTitle);
    try {
      for (const ucJobCode of codes) {
        await decide.mutateAsync({ match: false, standardTitle, ucJobCode });
      }
    } finally {
      setRejecting(null);
    }
  };

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>Match standards to job codes</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        These standards&apos; titles don&apos;t match any job code exactly. Each is shown with the
        closest codes at the same level, union and site. Confirm the right one, reject them, or
        type the right code. A confirmed match links the standard everywhere and survives
        re-uploading the workbooks.
      </p>

      <div className="mt-3 flex flex-wrap items-center gap-3">
        <label className="flex cursor-pointer items-center gap-2 text-base">
          <input
            checked={includeNotActive}
            className="checkbox checkbox-sm"
            onChange={() => setIncludeNotActive((v) => !v)}
            type="checkbox"
          />
          Also suggest jobs not active at UC Davis
        </label>
        <input
          aria-label="Filter standards"
          className="input input-sm input-bordered min-w-48 flex-1"
          onChange={(e) => setFilter(e.target.value)}
          placeholder="Filter standards…"
          value={filter}
        />
      </div>

      {review.data ? (
        <p className="mt-3 text-base" data-testid="match-summary">
          {review.data.unmatched.length} standard{review.data.unmatched.length === 1 ? '' : 's'} to
          review · {review.data.confirmed.length} matched by hand ·{' '}
          {review.data.withoutSuggestion} with no close job code
        </p>
      ) : review.isPending ? (
        <p className="mt-3 text-base text-base-content/65">Loading…</p>
      ) : null}
      {review.error || decide.error || undo.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(review.error ?? decide.error ?? undo.error)}</Note>
        </div>
      ) : null}

      {unmatched.length > 0 ? (
        <ul className="mt-3 max-h-[560px] space-y-3 overflow-y-auto" data-testid="match-list">
          {unmatched.slice(0, MATCH_LIMIT).map((u) => (
            <li className="rounded-lg border border-base-300 p-3" key={u.standardTitle}>
              <div className="text-base font-semibold">{u.standardTitle}</div>
              <ul className="mt-2 space-y-1.5">
                {u.suggestions.map((c) => (
                  <li className="flex flex-wrap items-center justify-between gap-2" key={c.ucJobCode}>
                    <span className="text-base">
                      <span className="tnum">{c.ucJobCode}</span> {c.title}{' '}
                      <Badge tone={c.active ? 'green' : 'muted'}>{c.active ? 'active' : 'not active'}</Badge>{' '}
                      {c.fromCtListing ? (
                        <Badge tone="accent">same title in the CT listing</Badge>
                      ) : (
                        <span className="text-sm text-base-content/50 tnum">{Math.round(c.score * 100)}% alike</span>
                      )}
                    </span>
                    <button
                      className="btn btn-primary btn-sm"
                      disabled={busy}
                      onClick={() => decide.mutate({ match: true, standardTitle: u.standardTitle, ucJobCode: c.ucJobCode })}
                      type="button"
                    >
                      This is the one
                    </button>
                  </li>
                ))}
              </ul>
              <div className="mt-2.5 flex flex-wrap items-center gap-2 border-t border-base-300 pt-2.5">
                <button
                  className="btn btn-ghost btn-sm"
                  disabled={busy}
                  onClick={() => void rejectAll(u.standardTitle, u.suggestions.map((c) => c.ucJobCode))}
                  type="button"
                >
                  {rejecting === u.standardTitle ? 'Rejecting…' : 'None of these'}
                </button>
                <input
                  aria-label={`Job code for ${u.standardTitle}`}
                  className="input input-sm input-bordered w-32 tnum"
                  inputMode="numeric"
                  onChange={(e) => setTyped((t) => ({ ...t, [u.standardTitle]: e.target.value }))}
                  placeholder="Job code"
                  value={typed[u.standardTitle] ?? ''}
                />
                <button
                  className="btn btn-outline btn-sm"
                  disabled={busy || !(typed[u.standardTitle] ?? '').trim()}
                  onClick={() =>
                    decide.mutate({
                      match: true,
                      standardTitle: u.standardTitle,
                      ucJobCode: (typed[u.standardTitle] ?? '').trim(),
                    })
                  }
                  type="button"
                >
                  Use this code
                </button>
              </div>
            </li>
          ))}
        </ul>
      ) : review.data ? (
        <p className="mt-3 text-base text-base-content/65">Nothing to review.</p>
      ) : null}
      {unmatched.length > MATCH_LIMIT ? (
        <p className="mt-2 text-sm text-base-content/50">
          Showing the first {MATCH_LIMIT} of {unmatched.length} — filter to narrow.
        </p>
      ) : null}

      {review.data && review.data.confirmed.length > 0 ? (
        <details className="mt-4">
          <summary className="cursor-pointer text-base font-semibold">
            Matched by hand ({review.data.confirmed.length})
          </summary>
          <ul className="mt-2 divide-y divide-base-300 rounded-lg border border-base-300" data-testid="confirmed-list">
            {review.data.confirmed.map((m) => (
              <li className="flex items-center justify-between gap-3 px-3 py-2" key={m.standardTitle}>
                <span className="text-base">
                  {m.standardTitle} → <span className="tnum">{m.ucJobCode}</span> {m.title}
                </span>
                <button
                  className="btn btn-ghost btn-sm"
                  disabled={busy}
                  onClick={() => undo.mutate(m.standardTitle)}
                  type="button"
                >
                  Undo
                </button>
              </li>
            ))}
          </ul>
        </details>
      ) : null}
    </Card>
  );
};

/**
 * Starter classes bootstrapped from blank standards — a title, no responsibilities — before blank
 * standards were refused. They aren't usable classes, so they are removed and the job waits for
 * real JDs or the full standard. A class with saved JDs against it is kept and listed.
 */
export const BlankStandardClassesPanel = () => {
  const preview = useBlankStandardClasses();
  const remove = useRemoveBlankStandardClasses();
  const classes = preview.data?.classes;
  const removable = (classes ?? []).filter((c) => c.savedJds === 0);

  const run = () => {
    if (
      window.confirm(
        `Remove ${removable.length} starter class${removable.length === 1 ? '' : 'es'} built from blank standards? ` +
          'Their jobs stay listed, waiting for JDs or the full standard.'
      )
    ) {
      remove.mutate(undefined, { onSuccess: () => preview.reset() });
    }
  };

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>Classes built from blank standards</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        A blank standard has a title but no responsibilities, so a class bootstrapped from one is
        little more than a title. These are removed; the job waits for real JDs or the full
        standard. Any with saved JDs are kept.
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <button
          className="btn btn-outline btn-sm"
          disabled={preview.isPending || remove.isPending}
          onClick={() => preview.mutate()}
          type="button"
        >
          {preview.isPending ? 'Checking…' : 'Find them'}
        </button>
        {removable.length > 0 ? (
          <button className="btn btn-primary btn-sm" disabled={remove.isPending} onClick={run} type="button">
            {remove.isPending ? 'Removing…' : `Remove ${removable.length}`}
          </button>
        ) : null}
      </div>
      {preview.error || remove.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(preview.error ?? remove.error)}</Note>
        </div>
      ) : null}
      {remove.data ? (
        <div className="mt-3" data-testid="blank-cleanup-result">
          <Note tone="green">
            Removed {remove.data.removed.length} class{remove.data.removed.length === 1 ? '' : 'es'}
            {remove.data.kept.length > 0
              ? `; kept ${remove.data.kept.length} with saved JDs (${remove.data.kept.map((k) => k.title).join(', ')})`
              : ''}
            .
          </Note>
        </div>
      ) : null}
      {classes ? (
        classes.length === 0 ? (
          <p className="mt-3 text-base text-base-content/65">None — no class was built from a blank standard.</p>
        ) : (
          <ul className="mt-3 max-h-80 divide-y divide-base-300 overflow-y-auto rounded-lg border border-base-300" data-testid="blank-classes">
            {classes.map((c) => (
              <li className="flex items-center justify-between gap-3 px-3 py-2" key={c.slug}>
                <span className="text-base">
                  {c.title} <span className="text-sm text-base-content/50 tnum">{c.ucJobCode}</span>
                </span>
                {c.savedJds > 0 ? (
                  <Badge tone="yellow">
                    kept — {c.savedJds} saved JD{c.savedJds === 1 ? '' : 's'}
                  </Badge>
                ) : null}
              </li>
            ))}
          </ul>
        )
      ) : null}
    </Card>
  );
};

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
      {bootstrap.data?.blank ? (
        <p className="mt-2 text-base text-base-content/65" data-testid="bootstrap-blank">
          {bootstrap.data.blank} more {bootstrap.data.blank === 1 ? 'is' : 'are'} blank — a title
          but no responsibilities — so there&apos;s nothing to build from. Add the full standard on
          the Codes page, or ingest JDs for the class.
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
