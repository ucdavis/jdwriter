import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { isAdmin } from '@/queries/user.ts';
import { Badge, Card, Eyebrow, PageHeader } from '@/shared/ui/primitives.tsx';
import { coverageQueryOptions } from '@/queries/classes.ts';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useSuspenseQuery } from '@tanstack/react-query';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/jds/$slug')({
  component: CoveragePage,
  loader: async ({
    context,
    params,
  }: {
    context: RouterContext;
    params: { slug: string };
  }) => {
    if (await isAdmin(context.queryClient)) {
      await context.queryClient.ensureQueryData(coverageQueryOptions(params.slug));
    }
  },
});

/**
 * Backwards coverage: how much of each real JD's responsibility time falls inside the
 * class template. A JD at ≥90% could adopt the template with under 10% unit edits, which
 * is the target the envelope is designed around.
 */
function CoveragePage() {
  return (
    <AdminOnly>
      <CoverageContent />
    </AdminOnly>
  );
}

// Inside the gate so the admin-only query never fires for an Author.
function CoverageContent() {
  const { slug } = Route.useParams();
  const { data: coverage } = useSuspenseQuery(coverageQueryOptions(slug));

  return (
    <>
      <PageHeader
        back={{ label: 'Back to envelopes', to: '/backend' }}
        eyebrow="Back end · backwards coverage"
        sub="How much of each real job description's responsibility time falls inside this class's standard categories."
        title="Coverage against the corpus"
      />

      {!coverage ? (
        <Card className="p-6">
          <p className="text-sm text-base-content/65">
            No coverage report for this class — it has no ingested JDs yet.
          </p>
        </Card>
      ) : (
        <div className="space-y-4">
          <Card className="p-5">
            <Eyebrow>Summary</Eyebrow>
            <div className="mt-2 grid grid-cols-3 gap-4">
              <div>
                <div className="text-3xl font-bold tnum">{coverage.n}</div>
                <div className="text-sm text-base-content/65">JDs compared</div>
              </div>
              <div>
                <div className="text-3xl font-bold tnum">{coverage.meanCoverage}%</div>
                <div className="text-sm text-base-content/65">mean coverage</div>
              </div>
              <div>
                <div className="text-3xl font-bold tnum">
                  {Math.round(coverage.wellCoveredPct * 100)}%
                </div>
                <div className="text-sm text-base-content/65">at ≥90% covered</div>
              </div>
            </div>
          </Card>

          <Card className="p-5">
            <Eyebrow>Per job description</Eyebrow>
            <ul className="mt-3 space-y-1.5">
              {coverage.perJd.map((j) => (
                <li
                  className="flex items-start justify-between gap-3 text-xs"
                  key={j.sourceFile}
                >
                  <span className="min-w-0 flex-1 truncate">
                    <Link
                      className="text-primary hover:underline"
                      params={{ _splat: j.sourceFile, slug }}
                      to="/backend/jd/$slug/$"
                    >
                      {j.sourceFile.replace(/\.html$/i, '')}
                    </Link>
                    {j.uncovered.length > 0 ? (
                      <span className="text-base-content/65">
                        {' '}
                        · idiosyncratic:{' '}
                        {j.uncovered.map((u) => `${u.name} (${u.pct}%)`).join(', ')}
                      </span>
                    ) : null}
                  </span>
                  <Badge
                    tone={
                      j.coveredPct >= 90 ? 'green' : j.coveredPct >= 70 ? 'yellow' : 'red'
                    }
                  >
                    {j.coveredPct}%
                  </Badge>
                </li>
              ))}
            </ul>
          </Card>
        </div>
      )}
    </>
  );
}
