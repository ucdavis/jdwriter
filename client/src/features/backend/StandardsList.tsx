import { Badge, Card } from '@/shared/ui/primitives.tsx';
import { downloadExcelCsv, toExcelCsv } from '@/lib/csv.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { StandardDetail } from '@/features/build/StandardDetail.tsx';
import { useStandardByTitle, useStandardsList } from '@/queries/admin.ts';
import type { StandardRow } from '@/lib/contracts.ts';
import { Link } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

/** The same kinds of filter as the Codes page, so the two read alike. */
type CodeFilter = 'active' | 'all' | 'inactive' | 'none';
type ClassFilter = 'any' | 'none' | 'some';
type ContentFilter = 'any' | 'blank' | 'full';
type MatchFilter = 'any' | 'hand' | 'title';

const byCode: Record<CodeFilter, (s: StandardRow) => boolean> = {
  active: (s) => s.code !== null && s.onPayroll,
  all: () => true,
  inactive: (s) => s.code !== null && !s.onPayroll,
  none: (s) => s.code === null,
};
const byClass: Record<ClassFilter, (s: StandardRow) => boolean> = {
  any: () => true,
  none: (s) => s.classSlug === null,
  some: (s) => s.classSlug !== null,
};
const byContent: Record<ContentFilter, (s: StandardRow) => boolean> = {
  any: () => true,
  blank: (s) => s.blank,
  full: (s) => !s.blank,
};
const byMatch: Record<MatchFilter, (s: StandardRow) => boolean> = {
  any: () => true,
  hand: (s) => s.how === 'hand',
  title: (s) => s.how === 'title',
};
const PAGE = 100;

/** Every job standard, what it is attached to, and the standard itself on request. */
export const StandardsList = ({ initialOpen, initialQuery }: { initialOpen?: string; initialQuery?: string }) => {
  const { data, error, isLoading } = useStandardsList();
  const [q, setQ] = useState(initialQuery ?? '');
  const [codeFilter, setCodeFilter] = useState<CodeFilter>('all');
  const [classFilter, setClassFilter] = useState<ClassFilter>('any');
  const [contentFilter, setContentFilter] = useState<ContentFilter>('any');
  const [matchFilter, setMatchFilter] = useState<MatchFilter>('any');
  const [limit, setLimit] = useState(PAGE);
  const [open, setOpen] = useState<string | null>(initialOpen ?? null);

  const standards = useMemo(() => data?.standards ?? [], [data]);
  const searched = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return needle
      ? standards.filter((s) =>
          [s.title, s.code ?? '', s.jobTitle ?? ''].join(' ').toLowerCase().includes(needle)
        )
      : standards;
  }, [standards, q]);
  const shown = searched
    .filter(byCode[codeFilter])
    .filter(byClass[classFilter])
    .filter(byContent[contentFilter])
    .filter(byMatch[matchFilter]);
  const reset = () => setLimit(PAGE);

  if (error) {
    return <p className="text-base text-error">{messageOf(error)}</p>;
  }

  return (
    <>
      <Card className="mb-3 p-5">
        <div className="flex flex-wrap items-center gap-2">
          <select
            aria-label="Job code"
            className="select select-bordered select-sm"
            onChange={(e) => {
              setCodeFilter(e.target.value as CodeFilter);
              reset();
            }}
            value={codeFilter}
          >
            <option value="all">Every standard</option>
            <option value="active">Job active at UC Davis</option>
            <option value="inactive">Job not active at UC Davis</option>
            <option value="none">Not attached to a job code</option>
          </select>
          <select
            aria-label="Class"
            className="select select-bordered select-sm"
            onChange={(e) => {
              setClassFilter(e.target.value as ClassFilter);
              reset();
            }}
            value={classFilter}
          >
            <option value="any">With or without a class</option>
            <option value="some">Has a class</option>
            <option value="none">No class</option>
          </select>
          <select
            aria-label="Content"
            className="select select-bordered select-sm"
            onChange={(e) => {
              setContentFilter(e.target.value as ContentFilter);
              reset();
            }}
            value={contentFilter}
          >
            <option value="any">Blank or not</option>
            <option value="full">Has key responsibilities</option>
            <option value="blank">Blank (no responsibilities)</option>
          </select>
          <select
            aria-label="Match"
            className="select select-bordered select-sm"
            onChange={(e) => {
              setMatchFilter(e.target.value as MatchFilter);
              reset();
            }}
            value={matchFilter}
          >
            <option value="any">However matched</option>
            <option value="title">Matched by title</option>
            <option value="hand">Matched by hand</option>
          </select>
          <input
            aria-label="Search standards"
            className="input input-sm input-bordered min-w-48 flex-1"
            onChange={(e) => {
              setQ(e.target.value);
              reset();
            }}
            placeholder="Search title, code or job…"
            type="search"
            value={q}
          />
        </div>
        {data ? (
          <p className="mt-3 text-base" data-testid="standards-summary">
            {shown.length} standard{shown.length === 1 ? '' : 's'} ·{' '}
            {shown.filter((s) => s.code !== null).length} attached to a job code ·{' '}
            {shown.filter((s) => s.classSlug !== null).length} with a class ·{' '}
            {shown.filter((s) => s.blank).length} blank
          </p>
        ) : null}
        {shown.length > 0 ? (
          <button className="btn btn-outline btn-sm mt-3" onClick={() => downloadStandards(shown)} type="button">
            ⤓ Download these {shown.length} as CSV
          </button>
        ) : null}
      </Card>

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
                          <Link
                            className="text-primary hover:underline"
                            search={{ code: s.code }}
                            to="/backend/codes"
                          >
                            {s.jobTitle} · code <span className="tnum">{s.code}</span> →
                          </Link>
                          {s.classSlug ? null : ' · no class yet'}
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

const downloadStandards = (rows: StandardRow[]) =>
  downloadExcelCsv(
    toExcelCsv(
      rows.map((s) => ({
        active: s.code === null ? '' : s.onPayroll ? 'yes' : 'no',
        blank: s.blank ? 'yes' : 'no',
        class: s.classTitle ?? '',
        code: s.code ?? '',
        flsa: s.flsa,
        grade: s.grade,
        how: s.how === 'hand' ? 'by hand' : s.how === 'title' ? 'by title' : '',
        job: s.jobTitle ?? '',
        responsibilities: s.keyResponsibilities,
        title: s.title,
        union: s.union,
      })),
      [
        { header: 'Standard', key: 'title' },
        { header: 'Job code', key: 'code' },
        { header: 'Job', key: 'job' },
        { header: 'Active at UC Davis', key: 'active' },
        { header: 'Matched', key: 'how' },
        { header: 'Class', key: 'class' },
        { header: 'Grade', key: 'grade' },
        { header: 'FLSA', key: 'flsa' },
        { header: 'Union', key: 'union' },
        { header: 'Key responsibilities', key: 'responsibilities' },
        { header: 'Blank', key: 'blank' },
      ]
    ),
    'jdwriter-standards.csv'
  );
