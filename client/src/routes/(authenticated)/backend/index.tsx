import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { Badge, Card, PageHeader } from '@/shared/ui/primitives.tsx';
import type { Tone } from '@/shared/ui/primitives.tsx';
import {
  classSummaryQueryOptions,
  useClassSummary,
} from '@/queries/classes.ts';
import { isAdmin } from '@/queries/user.ts';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { ClassSummary, EnvelopeSource } from '@/lib/contracts.ts';
import type { RouterContext } from '@/main.tsx';

const SHOWS = ['all', 'attention', 'standard', 'jds'] as const;
const SORTS = ['title', 'corpus', 'match'] as const;
type Show = (typeof SHOWS)[number];
type Sort = (typeof SORTS)[number];

/**
 * The list view lives in the URL, so returning from an envelope edit, or sharing a link,
 * lands on the same filtered list rather than the top of all of them.
 */
type EnvelopeSearch = { q?: string; show?: Show; sort?: Sort };

// Route options must follow TanStack Router's order (search validation before the loader)
// for its type inference, which outranks alphabetical key sorting.
/* eslint-disable perfectionist/sort-objects */
export const Route = createFileRoute('/(authenticated)/backend/')({
  validateSearch: (search: Record<string, unknown>): EnvelopeSearch => ({
    q: typeof search.q === 'string' && search.q ? search.q : undefined,
    show: SHOWS.find((s) => s === search.show && s !== 'all'),
    sort: SORTS.find((s) => s === search.sort && s !== 'title'),
  }),
  loader: async ({ context }: { context: RouterContext }) => {
    if (await isAdmin(context.queryClient)) {
      await context.queryClient.ensureQueryData(classSummaryQueryOptions());
    }
  },
  component: BackendPage,
});
/* eslint-enable perfectionist/sort-objects */

const sourceBadge: Record<EnvelopeSource, { label: string; tone: Tone }> = {
  claude: { label: 'AI-synthesized', tone: 'purple' },
  deterministic: { label: 'computed', tone: 'muted' },
  manual: { label: 'partner-edited', tone: 'teal' },
  standard: { label: 'standard-derived · no JDs yet', tone: 'yellow' },
};

const pct = (share: number) => `${Math.round(share * 100)}%`;

/** The same three problems the row badges flag in red, orange and grey. */
const needsAttention = (c: ClassSummary) =>
  !c.hasEnvelope ||
  c.envelopePctTotal !== 100 ||
  (c.corpusSize > 0 && c.consolidatedFunctions === 0);

const showFilters: Record<
  Show,
  { label: string; test: (c: ClassSummary) => boolean }
> = {
  all: { label: 'All', test: () => true },
  attention: { label: 'Needs attention', test: needsAttention },
  jds: { label: 'Has JDs', test: (c) => c.corpusSize > 0 },
  standard: { label: 'Standard only', test: (c) => c.corpusSize === 0 },
};

const sortLabels: Record<Sort, string> = {
  corpus: 'Most JDs',
  match: 'Weakest match first',
  title: 'Title',
};

const compare: Record<Sort, (a: ClassSummary, b: ClassSummary) => number> = {
  corpus: (a, b) =>
    b.corpusSize - a.corpusSize || a.title.localeCompare(b.title),
  // Classes with no coverage figure go last: there is nothing to compare yet.
  match: (a, b) =>
    (a.wellCoveredPct ?? 2) - (b.wellCoveredPct ?? 2) ||
    a.title.localeCompare(b.title),
  title: (a, b) => a.title.localeCompare(b.title),
};

const matches = (c: ClassSummary, q: string) => {
  const needle = q.trim().toLowerCase();
  return (
    !needle ||
    [c.title, c.ucJobCode, c.ctJobFamily, c.ctJobFunction].some((v) =>
      v.toLowerCase().includes(needle)
    )
  );
};

function BackendPage() {
  return (
    <AdminOnly>
      <EnvelopeIndex />
    </AdminOnly>
  );
}

// Inside the gate so the admin-only query never fires for an Author.
function EnvelopeIndex() {
  const { data } = useClassSummary();
  const search: EnvelopeSearch = Route.useSearch();
  const { q = '', show = 'all', sort = 'title' } = search;
  const navigate = Route.useNavigate();
  const classes = data?.classes ?? [];

  const setSearch = (next: EnvelopeSearch) =>
    void navigate({ replace: true, search: { ...search, ...next } });

  const searched = classes.filter((c) => matches(c, q));
  const shown = searched.filter(showFilters[show].test).toSorted(compare[sort]);

  return (
    <>
      <PageHeader
        eyebrow="Back end · averaging engine"
        sub="Every class the engine has built, with its computed envelope and provenance. Add JDs or create classes from standards under Corpus & standards."
        title="Job Envelopes"
      />

      {classes.length === 0 ? (
        <Card className="p-6">
          <p className="text-base text-base-content/65">
            No envelopes yet.{' '}
            <Link
              className="font-semibold text-primary hover:underline"
              to="/backend/corpus"
            >
              Add JDs or create classes from standards
            </Link>{' '}
            to build one.
          </p>
        </Card>
      ) : (
        <>
          <div className="mb-3 flex flex-wrap items-center gap-3">
            <input
              aria-label="Search classes"
              className="input input-sm input-bordered w-full sm:w-72"
              onChange={(e) => setSearch({ q: e.target.value || undefined })}
              placeholder="Search title, code or family…"
              type="search"
              value={q}
            />
            <div
              aria-label="Show"
              className="flex flex-wrap gap-1"
              role="group"
            >
              {SHOWS.map((s) => {
                const n = searched.filter(showFilters[s].test).length;
                return (
                  <button
                    aria-pressed={show === s}
                    className={`btn btn-sm ${show === s ? 'btn-primary' : 'btn-ghost'}`}
                    key={s}
                    onClick={() =>
                      setSearch({ show: s === 'all' ? undefined : s })
                    }
                    type="button"
                  >
                    {showFilters[s].label}
                    <span className="tnum opacity-70">{n}</span>
                  </button>
                );
              })}
            </div>
            <label className="ml-auto flex items-center gap-2 text-base text-base-content/65">
              Sort
              <select
                className="select select-sm select-bordered"
                onChange={(e) => {
                  const next = e.target.value as Sort;
                  setSearch({ sort: next === 'title' ? undefined : next });
                }}
                value={sort}
              >
                {SORTS.map((s) => (
                  <option key={s} value={s}>
                    {sortLabels[s]}
                  </option>
                ))}
              </select>
            </label>
          </div>

          {shown.length === 0 ? (
            <Card className="p-6">
              <p className="text-base text-base-content/65">
                No classes match.{' '}
                <button
                  className="font-semibold text-primary hover:underline"
                  onClick={() => setSearch({ q: undefined, show: undefined })}
                  type="button"
                >
                  Clear search and filter
                </button>
              </p>
            </Card>
          ) : (
            <Card>
              <ul className="divide-y divide-base-300">
                {shown.map((c) => (
                  <SummaryRow c={c} key={c.slug} />
                ))}
              </ul>
            </Card>
          )}
        </>
      )}
    </>
  );
}

function SummaryRow({ c }: { c: ClassSummary }) {
  const source = c.envelopeSource ? sourceBadge[c.envelopeSource] : null;
  const hasJds = c.corpusSize > 0;

  return (
    <li className="px-5 py-3.5">
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            {c.standardLinked && hasJds ? (
              <span
                className="inline-flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full bg-success/10 text-base font-bold text-success"
                title="All components ingested — JDs + official standard"
              >
                ✓
              </span>
            ) : null}
            <h2 className="text-base font-bold">{c.title}</h2>
            <Badge tone="accent">Code {c.ucJobCode || '—'}</Badge>
            {source ? <Badge tone={source.tone}>{source.label}</Badge> : null}
            {c.standardLinked ? (
              <Badge tone="green">standard linked</Badge>
            ) : null}
            {/* The envelope is ours to get right, so a total off 100 is the first thing to fix. */}
            {c.hasEnvelope && c.envelopePctTotal !== 100 ? (
              <Badge tone="red">% time sums to {c.envelopePctTotal}</Badge>
            ) : null}
            {!c.hasEnvelope ? <Badge tone="orange">no envelope</Badge> : null}
            {hasJds && c.consolidatedFunctions === 0 ? (
              <Badge tone="muted">not consolidated</Badge>
            ) : null}
          </div>
          <div className="mt-0.5 text-base text-base-content/65">
            {[c.ctJobFamily, c.ctJobFunction, c.personnelProgram]
              .filter(Boolean)
              .join(' · ')}
          </div>
        </div>
        <div className="flex items-center gap-3 whitespace-nowrap">
          <Link
            className="text-base font-semibold text-primary hover:underline"
            params={{ slug: c.slug }}
            to="/backend/edit/$slug"
          >
            Edit envelope
          </Link>
          <Link
            className="text-base font-semibold text-base-content/65 hover:underline"
            params={{ slug: c.slug }}
            to="/class/$slug"
          >
            Open →
          </Link>
        </div>
      </div>

      <dl className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-base">
        <Fact label="Corpus" value={`${c.corpusSize} JDs`} />
        <Fact label="Grade" value={c.grade ?? '—'} />
        <Fact label="Responsibilities" value={`${c.responsibilities}`} />
        <Fact label="KSAs" value={`${c.ksas}`} />
        <Fact
          label="Envelope match"
          value={
            !hasJds
              ? 'no JDs yet'
              : c.wellCoveredPct === null
                ? 'not computed'
                : pct(c.wellCoveredPct)
          }
        />
      </dl>

      {hasJds ? (
        <Link
          className="mt-1.5 inline-block text-base font-semibold text-primary hover:underline"
          params={{ slug: c.slug }}
          to="/backend/jds/$slug"
        >
          {c.wellCoveredPct === null || c.meanCoverage === null
            ? 'Backwards coverage — do the real JDs match this template? →'
            : `Backwards coverage · ${pct(c.wellCoveredPct)} of JDs are ≥90% covered (mean ${c.meanCoverage}%) →`}
        </Link>
      ) : null}
    </li>
  );
}

const Fact = ({ label, value }: { label: string; value: string }) => (
  <div className="flex gap-1.5">
    <dt className="text-base-content/50">{label}</dt>
    <dd className="font-semibold tnum">{value}</dd>
  </div>
);
