import type { JobCt, JobRow, JobsScope, StandardDraft } from '@/lib/contracts.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import {
  useBootstrapClass,
  useJobs,
  useReadStandard,
  useSaveManualStandard,
  useSetCodeActive,
} from '@/queries/admin.ts';
import { SiteBadge } from '@/shared/ui/SiteNote.tsx';
import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import { downloadExcelCsv, toExcelCsv } from '@/lib/csv.ts';
import { Link } from '@tanstack/react-router';
import { useDialog } from '@/shared/ui/useDialog.ts';
import { useState } from 'react';

/** How many jobs the list renders before asking for a filter. */
const LIMIT = 200;

type ClassFilter = 'any' | 'none' | 'some';

/**
 * What a job with no class needs before authors can use it, or null when it has one. A superseded
 * job needs nothing: its JDs go to its successor.
 */
export const needsOf = (j: JobRow): string | null =>
  j.class || j.supersededBy
    ? null
    : j.standard && !j.standard.blank
      ? 'Ready to bootstrap'
      : j.standard
        ? 'JDs, or the full standard (its standard is blank)'
        : 'JDs, or a standard';
type StandardFilter = 'any' | 'blank' | 'hand' | 'none' | 'some';

/**
 * Every UC job code and what it is attached to — its class and its standard — in one place. Each
 * job opens a dialog to bootstrap it, or to add or replace its standard by hand.
 */
/**
 * `initialCode` opens that job's pop-up, as a link from another page does (a standard's job, a JD's
 * code). It may be a job not active at UC Davis, so the list starts on all jobs, searched to it.
 */
export const JobsDirectory = ({ initialCode }: { initialCode?: string }) => {
  const [scope, setScope] = useState<JobsScope>(initialCode ? 'all' : 'active');
  const [classFilter, setClassFilter] = useState<ClassFilter>('any');
  const [standardFilter, setStandardFilter] = useState<StandardFilter>('any');
  const [query, setQuery] = useState(initialCode ?? '');
  const [open, setOpen] = useState<Pick<JobRow, 'code'> | null>(initialCode ? { code: initialCode } : null);
  const jobs = useJobs(scope);

  const shown = (jobs.data?.jobs ?? [])
    .filter((j) => (classFilter === 'some' ? j.class : classFilter === 'none' ? needsOf(j) : true))
    .filter((j) =>
      standardFilter === 'some'
        ? j.standard
        : standardFilter === 'none'
          ? !j.standard
          : standardFilter === 'hand'
            ? j.standard?.how === 'hand'
            : standardFilter === 'blank'
              ? j.standard?.blank
              : true
    )
    .filter((j) =>
      query
        ? `${j.title} ${j.code} ${j.family ?? ''} ${j.class?.title ?? ''} ${j.standard?.title ?? ''}`
            .toLowerCase()
            .includes(query.toLowerCase())
        : true
    );

  // The dialog shows the job as the list now has it, so a save or bootstrap shows at once.
  const current = open ? (jobs.data?.jobs.find((j) => j.code === open.code) ?? null) : null;

  return (
    <>
      <Card className="mb-5 p-5">
        <div className="flex flex-wrap items-center gap-2">
          <select
            aria-label="Which jobs"
            className="select select-bordered select-sm"
            onChange={(e) => setScope(e.target.value as JobsScope)}
            value={scope}
          >
            <option value="active">Active at UC Davis</option>
            <option value="inactive">Not active at UC Davis</option>
            <option value="all">All UC jobs</option>
          </select>
          <select
            aria-label="Class"
            className="select select-bordered select-sm"
            onChange={(e) => setClassFilter(e.target.value as ClassFilter)}
            value={classFilter}
          >
            <option value="any">With or without a class</option>
            <option value="some">Has a class</option>
            <option value="none">No class — waiting</option>
          </select>
          <select
            aria-label="Standard"
            className="select select-bordered select-sm"
            onChange={(e) => setStandardFilter(e.target.value as StandardFilter)}
            value={standardFilter}
          >
            <option value="any">With or without a standard</option>
            <option value="some">Has a standard</option>
            <option value="hand">Standard linked by hand</option>
            <option value="blank">Standard is blank</option>
            <option value="none">No standard</option>
          </select>
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => {
              setScope('active');
              setClassFilter('none');
              setStandardFilter('any');
            }}
            type="button"
          >
            Waiting for a class
          </button>
          <input
            aria-label="Search jobs"
            className="input input-sm input-bordered min-w-48 flex-1"
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search title, code, family, class or standard…"
            value={query}
          />
        </div>
        {jobs.data && classFilter === 'none' && shown.length > 0 ? (
          <p className="mt-2 text-base text-base-content/70" data-testid="jobs-needs">
            Needed: {shown.filter((j) => needsOf(j) === 'Ready to bootstrap').length} ready to bootstrap ·{' '}
            {shown.filter((j) => j.standard?.blank).length} JDs or the full standard (blank standard) ·{' '}
            {shown.filter((j) => !j.standard).length} JDs or a standard
          </p>
        ) : null}
        {jobs.data ? (
          <p className="mt-3 text-base" data-testid="jobs-summary">
            {shown.length} job{shown.length === 1 ? '' : 's'} · {shown.filter((j) => j.class).length} with a class ·{' '}
            {shown.filter((j) => j.standard).length} with a standard
            {shown.some((j) => j.standard?.blank)
              ? ` (${shown.filter((j) => j.standard?.blank).length} blank)`
              : ''}
          </p>
        ) : jobs.isPending ? (
          <p className="mt-3 text-base text-base-content/70">Loading…</p>
        ) : null}
        {jobs.error ? (
          <div className="mt-3">
            <Note tone="red">{messageOf(jobs.error)}</Note>
          </div>
        ) : null}
        {shown.length > 0 ? (
          <button className="btn btn-outline btn-sm mt-3" onClick={() => downloadJobs(shown)} type="button">
            ⤓ Download these {shown.length} as CSV
          </button>
        ) : null}
      </Card>

      {shown.length > 0 ? (
        <Card className="p-0">
          <div className="grid grid-cols-[2fr_1.5fr_1.5fr] gap-3 border-b border-base-300 px-4 py-2.5">
            <Eyebrow>Job</Eyebrow>
            <Eyebrow>Class</Eyebrow>
            <Eyebrow>Standard</Eyebrow>
          </div>
          <ul className="divide-y divide-base-300" data-testid="jobs-list">
            {shown.slice(0, LIMIT).map((j) => (
              <li key={j.code}>
                <button
                  className="grid w-full grid-cols-[2fr_1.5fr_1.5fr] gap-3 px-4 py-2.5 text-left hover:bg-base-200"
                  onClick={() => setOpen(j)}
                  type="button"
                >
                  <span className="min-w-0">
                    <span className="block truncate text-base font-semibold">{j.title}</span>
                    <span className="flex flex-wrap items-center gap-1.5 text-sm text-base-content/70 tnum">
                      {j.code}
                      {j.active ? null : <Badge tone="muted">not active</Badge>}
                      {j.site ? <SiteBadge site={j.site} /> : null}
                      {j.supersededBy ? <span>→ superseded by {j.supersededBy}</span> : null}
                    </span>
                  </span>
                  <span className="min-w-0 text-base">
                    {j.class ? (
                      <>
                        <span className="block truncate">{j.class.title}</span>
                        <span className="text-sm text-base-content/70">
                          {j.class.corpusSize > 0 ? `${j.class.corpusSize} JDs` : 'starter envelope'}
                        </span>
                        {j.class.fromBlankStandard ? <Badge tone="yellow">built from a blank standard</Badge> : null}
                      </>
                    ) : needsOf(j) ? (
                      <>
                        <span className="block text-base-content/70">—</span>
                        <span className="text-sm text-base-content/70">Needs: {needsOf(j)}</span>
                      </>
                    ) : (
                      <span className="text-base-content/70">—</span>
                    )}
                  </span>
                  <span className="min-w-0 text-base">
                    {j.standard ? (
                      <>
                        <span className="block truncate">{j.standard.title}</span>
                        {j.standard.how === 'hand' ? <Badge tone="teal">linked by hand</Badge> : null}{' '}
                        {j.standard.blank ? <Badge tone="yellow">blank — no responsibilities</Badge> : null}
                        {j.standard.fromSuccessor ? <Badge tone="muted">successor&apos;s</Badge> : null}
                      </>
                    ) : (
                      <span className="text-base-content/70">—</span>
                    )}
                  </span>
                </button>
              </li>
            ))}
          </ul>
          {shown.length > LIMIT ? (
            <p className="px-4 py-2.5 text-sm text-base-content/70">
              Showing the first {LIMIT} of {shown.length} — search or filter to narrow.
            </p>
          ) : null}
        </Card>
      ) : jobs.data ? (
        <p className="text-base text-base-content/70">No jobs match.</p>
      ) : null}

      {current ? <JobDialog job={current} onClose={() => setOpen(null)} /> : null}
    </>
  );
};

/** The jobs shown, as a spreadsheet to work from: what each is attached to, and what it needs. */
const downloadJobs = (rows: JobRow[]) =>
  downloadExcelCsv(
    toExcelCsv(
      rows.map((j) => ({
        active: j.active ? 'yes' : 'no',
        class: j.class?.title ?? '',
        code: j.code,
        family: j.family ?? '',
        grade: j.grade ?? '',
        jds: j.class?.corpusSize ?? '',
        needs: needsOf(j) ?? '',
        site: j.site ?? '',
        standard: j.standard ? `${j.standard.title}${j.standard.blank ? ' (blank)' : ''}` : '',
        supersededBy: j.supersededBy ?? '',
        title: j.title,
      })),
      [
        { header: 'Job code', key: 'code' },
        { header: 'Title', key: 'title' },
        { header: 'Active at UC Davis', key: 'active' },
        { header: 'Family', key: 'family' },
        { header: 'Grade', key: 'grade' },
        { header: 'Site', key: 'site' },
        { header: 'Class', key: 'class' },
        { header: 'JDs', key: 'jds' },
        { header: 'Standard', key: 'standard' },
        { header: 'Needs', key: 'needs' },
        { header: 'Superseded by', key: 'supersededBy' },
      ]
    ),
    'jdwriter-jobs.csv'
  );

const emptyDraft = (title: string): StandardDraft => ({
  education: [],
  keyResponsibilities: [],
  ksa: [],
  licenses: [],
  scope: '',
  specialConditions: [],
  title,
});

const origins: Record<string, string> = { AG: 'Agriculture', C: 'Campus', MC: 'Medical Center', OP: 'Office of the President' };

/** What the Career Tracks listing says of the code: its full title, and how it's graded and levelled. */
const CtFacts = ({ ct }: { ct: JobCt }) => (
  <div className="mt-2 rounded-lg bg-base-200 px-3 py-2 text-base" data-testid="ct-facts">
    <div>
      <span className="font-semibold">{ct.title}</span>{' '}
      <span className="text-sm text-base-content/70">(CT listing)</span>
    </div>
    <div className="text-sm text-base-content/70">
      {[
        `${ct.family} · ${ct.function}`,
        `level ${ct.level}${ct.supervisory ? ' — supervises' : ''}`,
        ct.grade && `grade ${ct.grade}`,
        ct.flsa === 'NE' || ct.flsa === 'N' ? 'non-exempt' : ct.flsa === 'E' ? 'exempt' : ct.flsa,
        ct.personnelProgram,
        origins[ct.origin] ?? ct.origin,
        ct.status === 'GF' ? 'grandfathered' : null,
        ct.opOnly ? 'Office of the President only' : null,
      ]
        .filter(Boolean)
        .join(' · ')}
    </div>
  </div>
);

/**
 * Whether the code is active at UC Davis: from the payroll reference, changeable by hand. Being
 * active decides whether a code can be bootstrapped, take imported JDs, or be matched to a standard,
 * and which codes a union successor supersedes, so a change that moves a supersession says so.
 */
const ActiveCheckbox = ({ job }: { job: JobRow }) => {
  const setActive = useSetCodeActive();
  const byHand = job.active !== job.onPayroll;
  return (
    <span className="flex flex-col">
      <label className="flex cursor-pointer items-center gap-2 text-base text-base-content">
        <input
          checked={job.active}
          className="checkbox checkbox-primary"
          disabled={setActive.isPending}
          onChange={(e) => setActive.mutate({ active: e.target.checked, code: job.code })}
          type="checkbox"
        />
        Active at UC Davis
        {byHand ? (
          <Badge tone="teal">
            set by hand · payroll says {job.onPayroll ? 'active' : 'not active'}
          </Badge>
        ) : null}
      </label>
      {setActive.error ? <span className="text-sm text-error">{messageOf(setActive.error)}</span> : null}
      {setActive.data?.supersessionChanges.map((c) => (
        <span className="text-sm text-warning-ink" key={c}>
          {c}
        </span>
      ))}
    </span>
  );
};

/** One job: what it's attached to, and what can be done about it. */
const JobDialog = ({ job, onClose }: { job: JobRow; onClose: () => void }) => {
  const [editing, setEditing] = useState(false);
  const create = useBootstrapClass();
  const [created, setCreated] = useState<string | null>(null);
  const box = useDialog<HTMLDivElement>(onClose);

  const bootstrap = () => {
    if (!job.standard) {
      return;
    }
    if (
      !job.active &&
      !window.confirm(`${job.title} (${job.code}) isn't active at UC Davis. Create a starter envelope from its standard anyway?`)
    ) {
      return;
    }
    create.mutate(
      { allowNotActive: !job.active, title: job.standard.title },
      { onSuccess: (c) => setCreated(`Created ${c.title} (${c.ucJobCode}) from its standard.`) }
    );
  };

  return (
    <div aria-labelledby="job-dialog-title" aria-modal className="modal modal-open" role="dialog">
      <div className="modal-box max-w-3xl" ref={box}>
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="text-2xl font-bold" id="job-dialog-title">
              {job.title}
            </h2>
            <div className="mt-1 flex flex-wrap items-center gap-2 text-base text-base-content/70 tnum">
              {job.code}
              {job.family ? ` · ${job.family}` : ''}
              {job.grade ? ` · ${job.grade}` : ''}
              {job.site ? <SiteBadge site={job.site} /> : null}
            </div>
            <div className="mt-2">
              <ActiveCheckbox job={job} />
            </div>
            {job.ct ? <CtFacts ct={job.ct} /> : null}
            {job.supersededBy ? (
              <p className="mt-1 text-base text-warning-ink">Superseded by {job.supersededBy}: its JDs go to that class.</p>
            ) : null}
          </div>
          <button aria-label="Close" className="btn btn-ghost btn-sm" onClick={onClose} type="button">
            ✕
          </button>
        </div>

        <div className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2">
          <div>
            <Eyebrow>Class</Eyebrow>
            {job.class ? (
              <div className="mt-1 text-base">
                <Link className="font-semibold text-primary hover:underline" params={{ slug: job.class.slug }} to="/class/$slug">
                  {job.class.title}
                </Link>
                <div className="text-base-content/70">
                  {job.class.corpusSize > 0 ? (
                    <Link
                      className="text-primary hover:underline"
                      search={{ q: job.supersededBy ?? job.code }}
                      to="/backend/descriptions"
                    >
                      Learned from {job.class.corpusSize} JDs →
                    </Link>
                  ) : (
                    'Starter envelope from its standard'
                  )}
                </div>
                {job.class.fromBlankStandard ? (
                  <p className="mt-1 text-warning-ink">Built from a blank standard: little more than a title went into it.</p>
                ) : null}
              </div>
            ) : (
              <p className="mt-1 text-base text-base-content/70">No class yet.</p>
            )}
          </div>
          <div>
            <Eyebrow>Standard</Eyebrow>
            {job.standard ? (
              <div className="mt-1 text-base">
                <Link
                  className="font-semibold text-primary hover:underline"
                  search={{ open: job.standard.title, q: job.standard.title }}
                  to="/backend/standards"
                >
                  {job.standard.title}
                </Link>
                <div className="text-base-content/70">
                  {job.standard.fromSuccessor
                    ? `Its successor's (${job.supersededBy}) — where its JDs now go`
                    : job.standard.how === 'hand'
                      ? 'Linked by hand'
                      : 'Its title matches the job'}
                </div>
                {job.standard.blank ? (
                  <p className="mt-1 text-warning-ink" data-testid="blank-standard">
                    Blank: it has a title but no responsibilities, so it can&apos;t be used to build an
                    envelope. Replace it with the full standard, or ingest JDs for this job.
                  </p>
                ) : null}
              </div>
            ) : (
              <p className="mt-1 text-base text-base-content/70">No standard yet.</p>
            )}
          </div>
        </div>

        {create.error ? (
          <div className="mt-3">
            <Note tone="red">{messageOf(create.error)}</Note>
          </div>
        ) : null}
        {created ? (
          <div className="mt-3">
            <Note tone="green">{created}</Note>
          </div>
        ) : null}

        {editing ? (
          <StandardForm job={job} onDone={() => setEditing(false)} />
        ) : (
          <div className="mt-5 flex flex-wrap gap-2">
            {job.standard && !job.standard.blank && !job.class && !job.supersededBy ? (
              <button className="btn btn-primary btn-sm" disabled={create.isPending} onClick={bootstrap} type="button">
                {create.isPending ? 'Creating…' : 'Bootstrap from its standard'}
              </button>
            ) : null}
            <button className="btn btn-outline btn-sm" onClick={() => setEditing(true)} type="button">
              {job.standard ? 'Replace the standard' : 'Add the standard'}
            </button>
          </div>
        )}
      </div>
      <button aria-label="Close dialog" className="modal-backdrop" onClick={onClose} tabIndex={-1} type="button" />
    </div>
  );
};

/** One item per line, in the form; a list on the wire. */
const lines = (items: string[]) => items.join('\n');
const items = (text: string) => text.split('\n');

const LISTS: Array<[keyof Omit<StandardDraft, 'scope' | 'title'>, string]> = [
  ['keyResponsibilities', 'Key responsibilities'],
  ['ksa', 'Knowledge, skills & abilities'],
  ['education', 'Education'],
  ['licenses', 'Licenses & certifications'],
  ['specialConditions', 'Special conditions'],
];

/**
 * A job's standard, entered by hand: read from a Word export of the standard — the AI copies its
 * sections — or typed. Nothing is saved until the admin has reviewed it.
 */
const StandardForm = ({ job, onDone }: { job: JobRow; onDone: () => void }) => {
  const [draft, setDraft] = useState<StandardDraft>(() => emptyDraft(job.standard?.title ?? job.title));
  const read = useReadStandard();
  const save = useSaveManualStandard();

  return (
    <div className="mt-5 border-t border-base-300 pt-4" data-testid="standard-form">
      <Eyebrow>{job.standard ? 'Replace the standard' : 'Add the standard'}</Eyebrow>
      <p className="mt-1 text-base text-base-content/70">
        Drop in the standard&apos;s Word export and the AI will copy its sections into the fields
        below — or type them. Review everything, then save. One item per line.
      </p>
      <label className="btn btn-outline btn-sm mt-3">
        {read.isPending ? 'Reading…' : 'Read from a Word document'}
        <input
          accept=".docx,.pdf,.txt"
          aria-label="Standard document"
          className="hidden"
          disabled={read.isPending}
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) {
              read.mutate(file, { onSuccess: (d) => setDraft({ ...d, title: d.title || draft.title }) });
            }
            e.target.value = '';
          }}
          type="file"
        />
      </label>
      {read.error ? (
        <div className="mt-2">
          <Note tone="red">{messageOf(read.error)}</Note>
        </div>
      ) : null}

      <label className="mt-3 block">
        <span className="eyebrow">Title</span>
        <input
          className="input input-bordered mt-1 w-full text-base"
          onChange={(e) => setDraft({ ...draft, title: e.target.value })}
          value={draft.title}
        />
      </label>
      <label className="mt-3 block">
        <span className="eyebrow">Scope</span>
        <textarea
          className="textarea textarea-bordered mt-1 w-full text-base"
          onChange={(e) => setDraft({ ...draft, scope: e.target.value })}
          rows={3}
          value={draft.scope}
        />
      </label>
      {LISTS.map(([key, label]) => (
        <label className="mt-3 block" key={key}>
          <span className="eyebrow">{label}</span>
          <textarea
            className="textarea textarea-bordered mt-1 w-full text-base"
            onChange={(e) => setDraft({ ...draft, [key]: items(e.target.value) })}
            rows={3}
            value={lines(draft[key])}
          />
        </label>
      ))}

      {save.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(save.error)}</Note>
        </div>
      ) : null}
      <div className="mt-4 flex gap-2">
        <button
          className="btn btn-primary btn-sm"
          disabled={save.isPending || !draft.title.trim()}
          onClick={() => save.mutate({ standard: draft, ucJobCode: job.code }, { onSuccess: onDone })}
          type="button"
        >
          {save.isPending ? 'Saving…' : 'Save the standard'}
        </button>
        <button className="btn btn-ghost btn-sm" onClick={onDone} type="button">
          Cancel
        </button>
      </div>
    </div>
  );
};
