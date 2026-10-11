import type { JobRow, JobsScope, StandardDraft } from '@/lib/contracts.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { useBootstrapClass, useJobs, useReadStandard, useSaveManualStandard } from '@/queries/admin.ts';
import { SiteBadge } from '@/shared/ui/SiteNote.tsx';
import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

/** How many jobs the list renders before asking for a filter. */
const LIMIT = 200;

type ClassFilter = 'any' | 'none' | 'some';
type StandardFilter = 'any' | 'hand' | 'none' | 'some';

/**
 * Every UC job code and what it is attached to — its class and its standard — in one place. Each
 * job opens a dialog to bootstrap it, or to add or replace its standard by hand.
 */
export const JobsDirectory = () => {
  const [scope, setScope] = useState<JobsScope>('active');
  const [classFilter, setClassFilter] = useState<ClassFilter>('any');
  const [standardFilter, setStandardFilter] = useState<StandardFilter>('any');
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState<JobRow | null>(null);
  const jobs = useJobs(scope);

  const shown = (jobs.data?.jobs ?? [])
    .filter((j) => (classFilter === 'some' ? j.class : classFilter === 'none' ? !j.class : true))
    .filter((j) =>
      standardFilter === 'some'
        ? j.standard
        : standardFilter === 'none'
          ? !j.standard
          : standardFilter === 'hand'
            ? j.standard?.how === 'hand'
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
  const current = open ? (jobs.data?.jobs.find((j) => j.code === open.code) ?? open) : null;

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
            <option value="none">No class</option>
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
            <option value="none">No standard</option>
          </select>
          <input
            aria-label="Search jobs"
            className="input input-sm input-bordered min-w-48 flex-1"
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search title, code, family, class or standard…"
            value={query}
          />
        </div>
        {jobs.data ? (
          <p className="mt-3 text-base" data-testid="jobs-summary">
            {shown.length} job{shown.length === 1 ? '' : 's'} · {shown.filter((j) => j.class).length} with a class ·{' '}
            {shown.filter((j) => j.standard).length} with a standard
          </p>
        ) : jobs.isPending ? (
          <p className="mt-3 text-base text-base-content/65">Loading…</p>
        ) : null}
        {jobs.error ? (
          <div className="mt-3">
            <Note tone="red">{messageOf(jobs.error)}</Note>
          </div>
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
                    <span className="flex flex-wrap items-center gap-1.5 text-sm text-base-content/65 tnum">
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
                        <span className="text-sm text-base-content/65">
                          {j.class.corpusSize > 0 ? `${j.class.corpusSize} JDs` : 'starter envelope'}
                        </span>
                      </>
                    ) : (
                      <span className="text-base-content/50">—</span>
                    )}
                  </span>
                  <span className="min-w-0 text-base">
                    {j.standard ? (
                      <>
                        <span className="block truncate">{j.standard.title}</span>
                        {j.standard.how === 'hand' ? <Badge tone="teal">linked by hand</Badge> : null}
                      </>
                    ) : (
                      <span className="text-base-content/50">—</span>
                    )}
                  </span>
                </button>
              </li>
            ))}
          </ul>
          {shown.length > LIMIT ? (
            <p className="px-4 py-2.5 text-sm text-base-content/50">
              Showing the first {LIMIT} of {shown.length} — search or filter to narrow.
            </p>
          ) : null}
        </Card>
      ) : jobs.data ? (
        <p className="text-base text-base-content/65">No jobs match.</p>
      ) : null}

      {current ? <JobDialog job={current} onClose={() => setOpen(null)} /> : null}
    </>
  );
};

const emptyDraft = (title: string): StandardDraft => ({
  education: [],
  keyResponsibilities: [],
  ksa: [],
  licenses: [],
  scope: '',
  specialConditions: [],
  title,
});

/** One job: what it's attached to, and what can be done about it. */
const JobDialog = ({ job, onClose }: { job: JobRow; onClose: () => void }) => {
  const [editing, setEditing] = useState(false);
  const create = useBootstrapClass();
  const [created, setCreated] = useState<string | null>(null);

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
      <div className="modal-box max-w-3xl">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="text-2xl font-bold" id="job-dialog-title">
              {job.title}
            </h2>
            <div className="mt-1 flex flex-wrap items-center gap-2 text-base text-base-content/65 tnum">
              {job.code}
              {job.family ? ` · ${job.family}` : ''}
              {job.grade ? ` · ${job.grade}` : ''}
              <Badge tone={job.active ? 'green' : 'muted'}>{job.active ? 'active at UC Davis' : 'not active at UC Davis'}</Badge>
              {job.site ? <SiteBadge site={job.site} /> : null}
            </div>
            {job.supersededBy ? (
              <p className="mt-1 text-base text-warning">Superseded by {job.supersededBy}: its JDs go to that class.</p>
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
                <div className="text-base-content/65">
                  {job.class.corpusSize > 0
                    ? `Learned from ${job.class.corpusSize} JDs`
                    : 'Starter envelope from its standard'}
                </div>
              </div>
            ) : (
              <p className="mt-1 text-base text-base-content/65">No class yet.</p>
            )}
          </div>
          <div>
            <Eyebrow>Standard</Eyebrow>
            {job.standard ? (
              <div className="mt-1 text-base">
                <div className="font-semibold">{job.standard.title}</div>
                <div className="text-base-content/65">
                  {job.standard.how === 'hand' ? 'Linked by hand' : 'Its title matches the job'}
                </div>
              </div>
            ) : (
              <p className="mt-1 text-base text-base-content/65">No standard yet.</p>
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
            {job.standard && !job.class && !job.supersededBy ? (
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
      <button aria-label="Close dialog" className="modal-backdrop" onClick={onClose} type="button" />
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
      <p className="mt-1 text-base text-base-content/65">
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
