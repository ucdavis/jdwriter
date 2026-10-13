import { Badge, Card } from '@/shared/ui/primitives.tsx';
import type { Tone } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { useCorpusJds } from '@/queries/admin.ts';
import type { CorpusJdRow } from '@/lib/contracts.ts';
import { Link } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

export const originLabel: Record<CorpusJdRow['origin'], { label: string; tone: Tone }> = {
  authored: { label: 'written in the app', tone: 'teal' },
  classify: { label: 'from Classify', tone: 'purple' },
  export: { label: 'HRTMS export', tone: 'muted' },
  imported: { label: 'from import', tone: 'accent' },
};

const SHOWS = ['all', 'pending', 'imported'] as const;
type Show = (typeof SHOWS)[number];
const showLabels: Record<Show, string> = {
  all: 'All',
  imported: 'From import',
  pending: 'Not yet in an envelope',
};
const PAGE = 100;

/** Every job description in the corpus, newest first, searchable, each one viewable whole. */
export const JobDescriptionList = ({ initialQuery }: { initialQuery?: string }) => {
  const { data, error, isLoading } = useCorpusJds();
  const [q, setQ] = useState(initialQuery ?? '');
  const [show, setShow] = useState<Show>('all');
  const [limit, setLimit] = useState(PAGE);

  const jds = useMemo(() => data?.jds ?? [], [data]);
  const searched = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return needle
      ? jds.filter((j) =>
          [j.ucJobTitle, j.workingTitle, j.departmentName, j.ucJobCode, j.classTitle ?? '']
            .join(' ')
            .toLowerCase()
            .includes(needle)
        )
      : jds;
  }, [jds, q]);
  const test = (s: Show) => (j: CorpusJdRow) =>
    s === 'all' ? true : s === 'pending' ? j.pending : j.origin === 'imported';
  const shown = searched.filter(test(show));

  if (error) {
    return <p className="text-base text-error">{messageOf(error)}</p>;
  }

  return (
    <>
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <input
          aria-label="Search job descriptions"
          className="input input-sm input-bordered w-full sm:w-72"
          onChange={(e) => {
            setQ(e.target.value);
            setLimit(PAGE);
          }}
          placeholder="Search title, department or code…"
          type="search"
          value={q}
        />
        <div aria-label="Show" className="flex flex-wrap gap-1" role="group">
          {SHOWS.map((s) => (
            <button
              aria-pressed={show === s}
              className={`btn btn-sm ${show === s ? 'btn-primary' : 'btn-ghost'}`}
              key={s}
              onClick={() => {
                setShow(s);
                setLimit(PAGE);
              }}
              type="button"
            >
              {showLabels[s]}
              <span className="tnum opacity-70">{searched.filter(test(s)).length}</span>
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <p className="text-base text-base-content/65">Loading job descriptions…</p>
      ) : shown.length === 0 ? (
        <Card className="p-6">
          <p className="text-base text-base-content/65">No job descriptions match.</p>
        </Card>
      ) : (
        <Card>
          <ul className="divide-y divide-base-300" data-testid="jd-list">
            {shown.slice(0, limit).map((j) => (
              <li className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1 px-5 py-3" key={j.id}>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-base font-bold">{j.workingTitle || j.ucJobTitle}</span>
                    <Badge tone={originLabel[j.origin].tone}>{originLabel[j.origin].label}</Badge>
                    {j.pending ? <Badge tone="yellow">not yet in an envelope</Badge> : null}
                  </div>
                  <div className="mt-0.5 text-base text-base-content/65">
                    <Link
                      className="hover:text-primary hover:underline"
                      search={{ code: j.ucJobCode }}
                      to="/backend/codes"
                    >
                      {j.classTitle ?? j.ucJobTitle} · code <span className="tnum">{j.ucJobCode}</span>
                    </Link>
                    {j.departmentName ? ` · ${j.departmentName}` : ''}
                  </div>
                  <div className="text-sm text-base-content/50 tnum">
                    {j.functions} function{j.functions === 1 ? '' : 's'} · {j.duties} dut
                    {j.duties === 1 ? 'y' : 'ies'} · {j.pctTotal}% time
                    {j.addedAt ? ` · added ${new Date(j.addedAt).toLocaleDateString()}` : ''}
                  </div>
                </div>
                <Link
                  className="whitespace-nowrap text-base font-semibold text-primary hover:underline"
                  params={{ id: String(j.id) }}
                  to="/backend/descriptions/$id"
                >
                  View →
                </Link>
              </li>
            ))}
          </ul>
          {shown.length > limit ? (
            <div className="border-t border-base-300 px-5 py-3">
              <button
                className="btn btn-ghost btn-sm"
                onClick={() => setLimit((l) => l + PAGE)}
                type="button"
              >
                Show more ({shown.length - limit} left)
              </button>
            </div>
          ) : null}
        </Card>
      )}
    </>
  );
};
