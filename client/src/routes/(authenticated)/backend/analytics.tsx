import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { isAdmin } from '@/queries/user.ts';
import { analyticsQueryOptions } from '@/queries/admin.ts';
import { BarList, ColumnChart } from '@/features/backend/Charts.tsx';
import { Card, Eyebrow, PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/analytics')({
  component: AnalyticsPage,
  loader: async ({ context }: { context: RouterContext }) => {
    if (await isAdmin(context.queryClient)) {
      await context.queryClient.ensureQueryData(analyticsQueryOptions());
    }
  },
});

function AnalyticsPage() {
  return (
    <AdminOnly>
      <AnalyticsContent />
    </AdminOnly>
  );
}

const Tile = ({ label, note, value }: { label: string; note?: string; value: number }) => (
  <div className="rounded-lg border border-base-300 p-4">
    <Eyebrow>{label}</Eyebrow>
    <div className="mt-1 text-[28px] font-bold leading-none tnum">{value}</div>
    {note ? <div className="mt-1 text-[11.5px] text-base-content/55">{note}</div> : null}
  </div>
);

const TableView = ({ children }: { children: ReactNode }) => (
  <details className="mt-3">
    <summary className="cursor-pointer text-[11.5px] text-base-content/55">Show as table</summary>
    <div className="mt-2 overflow-x-auto">{children}</div>
  </details>
);

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00Z`).toLocaleDateString(undefined, { day: 'numeric', month: 'short', timeZone: 'UTC' });

// Inside the gate so the admin-only query never fires for an Author.
function AnalyticsContent() {
  const { data } = useQuery(analyticsQueryOptions());
  if (!data) {
    return null;
  }

  const { classes, usage } = data;
  const weeks = usage.perWeek.map((w, i) => ({
    key: w.weekOf,
    label: `Week of ${shortDate(w.weekOf)}`,
    tick: i % 3 === 0 || i === usage.perWeek.length - 1 ? shortDate(w.weekOf) : '',
    value: w.count,
  }));

  return (
    <>
      <PageHeader
        eyebrow="Back end · analytics"
        sub="How JDWriter is being used, and how its classes are used and fit."
        title="Analytics"
      />

      <Card className="p-5">
        <Eyebrow>Usage</Eyebrow>
        <div className="mt-3 grid grid-cols-2 gap-3 md:grid-cols-6">
          <Tile label="Users" value={usage.users} />
          <Tile label="Active, 7 days" value={usage.active7Days} />
          <Tile label="Active, 30 days" value={usage.active30Days} />
          <Tile label="JDs saved" value={usage.jdsSaved} />
          <Tile label="Ready" note="time totals 100%" value={usage.ready} />
          <Tile label="Draft" value={usage.draft} />
        </div>

        <h3 className="mt-6 text-[13px] font-semibold">JDs created per week</h3>
        <ColumnChart data={weeks} label="JDs created per week, last 12 weeks" valueName="JDs" />
        <TableView>
          <table className="table table-xs">
            <thead>
              <tr>
                <th>Week of</th>
                <th className="text-right">JDs created</th>
              </tr>
            </thead>
            <tbody>
              {usage.perWeek.map((w) => (
                <tr key={w.weekOf}>
                  <td>{shortDate(w.weekOf)}</td>
                  <td className="text-right tnum">{w.count}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </TableView>
      </Card>

      <Card className="mt-5 p-5">
        <Eyebrow>Classes</Eyebrow>
        <div className="mt-3 grid gap-6 md:grid-cols-2">
          <section>
            <h3 className="text-[13px] font-semibold">Most-authored classes</h3>
            {classes.mostAuthored.length === 0 ? (
              <p className="mt-2 text-[12.5px] text-base-content/65">No JDs written yet.</p>
            ) : (
              <BarList
                data={classes.mostAuthored.map((c) => ({ key: c.slug, label: c.title, value: c.count }))}
                label="Most-authored classes"
                valueName="JDs"
              />
            )}
          </section>
          <section>
            <h3 className="text-[13px] font-semibold">Envelope match across classes</h3>
            <p className="mt-1 text-[11.5px] text-base-content/55">
              Classes by the share of their real JDs at least 90% covered by the envelope.
            </p>
            <BarList
              data={classes.envelopeMatch.map((b) => ({ key: b.label, label: b.label, value: b.classes }))}
              label="Classes by envelope match"
              valueName="classes"
            />
          </section>
        </div>

        <div className="mt-6 grid gap-6 md:grid-cols-2">
          <section>
            <h3 className="text-[13px] font-semibold">
              Lowest envelope match{' '}
              <span className="font-normal text-base-content/55">— where curation pays off first</span>
            </h3>
            <table className="table table-xs mt-2">
              <thead>
                <tr>
                  <th>Class</th>
                  <th className="text-right">≥90% covered</th>
                  <th className="text-right">Mean</th>
                  <th className="text-right">JDs</th>
                </tr>
              </thead>
              <tbody>
                {classes.lowestMatch.map((c) => (
                  <tr key={c.slug}>
                    <td>
                      <Link className="text-primary hover:underline" params={{ slug: c.slug }} to="/backend/jds/$slug">
                        {c.title}
                      </Link>
                    </td>
                    <td className="text-right tnum">{Math.round(c.wellCoveredPct * 100)}%</td>
                    <td className="text-right tnum">{Math.round(c.meanCoverage)}%</td>
                    <td className="text-right tnum">{c.jds}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
          <section>
            <h3 className="text-[13px] font-semibold">
              Never used{' '}
              <span className="font-normal text-base-content/55">
                — {classes.neverUsed.length} of {classes.totalClasses} classes have no JDs written yet
              </span>
            </h3>
            <div className="mt-2 max-h-72 overflow-y-auto">
              <table className="table table-xs">
                <thead>
                  <tr>
                    <th>Class</th>
                    <th>Code</th>
                    <th className="text-right">Corpus JDs</th>
                  </tr>
                </thead>
                <tbody>
                  {classes.neverUsed.map((c) => (
                    <tr key={c.slug}>
                      <td>
                        <Link className="text-primary hover:underline" params={{ slug: c.slug }} to="/class/$slug">
                          {c.title}
                        </Link>
                      </td>
                      <td className="tnum">{c.ucJobCode}</td>
                      <td className="text-right tnum">{c.count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      </Card>
    </>
  );
}
