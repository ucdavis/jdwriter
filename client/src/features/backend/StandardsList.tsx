import { Badge, Card } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { StandardDetail } from '@/features/build/StandardDetail.tsx';
import { useStandardByTitle, useStandardsList } from '@/queries/admin.ts';
import type { StandardRow } from '@/lib/contracts.ts';
import { Link } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

const SHOWS = ['all', 'attached', 'unattached', 'blank'] as const;
type Show = (typeof SHOWS)[number];
const shows: Record<Show, { label: string; test: (s: StandardRow) => boolean }> = {
  all: { label: 'All', test: () => true },
  attached: { label: 'Attached to a job', test: (s) => s.code !== null },
  blank: { label: 'Blank', test: (s) => s.blank },
  unattached: { label: 'No job', test: (s) => s.code === null },
};
const PAGE = 100;

/** Every job standard, what it is attached to, and the standard itself on request. */
export const StandardsList = () => {
  const { data, error, isLoading } = useStandardsList();
  const [q, setQ] = useState('');
  const [show, setShow] = useState<Show>('all');
  const [limit, setLimit] = useState(PAGE);
  const [open, setOpen] = useState<string | null>(null);

  const standards = useMemo(() => data?.standards ?? [], [data]);
  const searched = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return needle
      ? standards.filter((s) =>
          [s.title, s.code ?? '', s.jobTitle ?? ''].join(' ').toLowerCase().includes(needle)
        )
      : standards;
  }, [standards, q]);
  const shown = searched.filter(shows[show].test);

  if (error) {
    return <p className="text-base text-error">{messageOf(error)}</p>;
  }

  return (
    <>
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <input
          aria-label="Search standards"
          className="input input-sm input-bordered w-full sm:w-72"
          onChange={(e) => {
            setQ(e.target.value);
            setLimit(PAGE);
          }}
          placeholder="Search title or code…"
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
              {shows[s].label}
              <span className="tnum opacity-70">{searched.filter(shows[s].test).length}</span>
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <p className="text-base text-base-content/65">Loading standards…</p>
      ) : shown.length === 0 ? (
        <Card className="p-6">
          <p className="text-base text-base-content/65">No standards match.</p>
        </Card>
      ) : (
        <Card>
          <ul className="divide-y divide-base-300" data-testid="standards-list">
            {shown.slice(0, limit).map((s) => (
              <li className="px-5 py-3" key={s.title}>
                <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-base font-bold">{s.title}</span>
                      {s.blank ? <Badge tone="orange">blank</Badge> : null}
                      {s.how === 'hand' ? <Badge tone="teal">matched by hand</Badge> : null}
                      {s.code && !s.onPayroll ? <Badge tone="muted">not on payroll</Badge> : null}
                    </div>
                    <div className="mt-0.5 text-base text-base-content/65">
                      {s.code ? (
                        <>
                          {s.jobTitle} · code <span className="tnum">{s.code}</span>
                          {s.classSlug ? (
                            <>
                              {' · '}
                              <Link
                                className="text-primary hover:underline"
                                params={{ slug: s.classSlug }}
                                to="/backend/edit/$slug"
                              >
                                {s.classTitle} envelope
                              </Link>
                            </>
                          ) : (
                            ' · no class yet'
                          )}
                        </>
                      ) : (
                        'Not attached to a job code'
                      )}
                    </div>
                    <div className="text-sm text-base-content/50 tnum">
                      {[s.grade && `Grade ${s.grade.replace(/^grade\s*/i, '')}`, s.flsa, s.union && `Unit ${s.union}`]
                        .filter(Boolean)
                        .join(' · ')}
                      {s.grade || s.flsa || s.union ? ' · ' : ''}
                      {s.keyResponsibilities} key responsibilit{s.keyResponsibilities === 1 ? 'y' : 'ies'}
                    </div>
                  </div>
                  <button
                    aria-expanded={open === s.title}
                    className="btn btn-ghost btn-sm"
                    onClick={() => setOpen(open === s.title ? null : s.title)}
                    type="button"
                  >
                    {open === s.title ? 'Hide' : 'View'}
                  </button>
                </div>
                {open === s.title ? <OpenStandard title={s.title} /> : null}
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

const OpenStandard = ({ title }: { title: string }) => {
  const { data, error, isLoading } = useStandardByTitle(title);
  if (isLoading) {
    return <p className="mt-2 text-base text-base-content/65">Loading…</p>;
  }

  if (error || !data) {
    return <p className="mt-2 text-base text-error">{error ? messageOf(error) : 'Not found.'}</p>;
  }

  return <StandardDetail className="mt-3" standard={data} />;
};
