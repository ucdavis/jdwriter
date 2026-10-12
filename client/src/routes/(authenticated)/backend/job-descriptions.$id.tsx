import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { isAdmin } from '@/queries/user.ts';
import { Badge, Card, Eyebrow, PageHeader } from '@/shared/ui/primitives.tsx';
import { corpusJdQueryOptions } from '@/queries/admin.ts';
import { originLabel } from '@/features/backend/JobDescriptionList.tsx';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useSuspenseQuery } from '@tanstack/react-query';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/job-descriptions/$id')({
  component: JobDescriptionPage,
  loader: async ({ context, params }: { context: RouterContext; params: { id: string } }) => {
    if (await isAdmin(context.queryClient)) {
      await context.queryClient.ensureQueryData(corpusJdQueryOptions(Number(params.id)));
    }
  },
});

/** One job description from the corpus, whole, as it was parsed. */
function JobDescriptionPage() {
  return (
    <AdminOnly>
      <JobDescriptionContent />
    </AdminOnly>
  );
}

// Inside the gate so the admin-only query never fires for an Author.
function JobDescriptionContent() {
  const { id } = Route.useParams();
  const { data } = useSuspenseQuery(corpusJdQueryOptions(Number(id)));
  const { record } = data;
  const total = record.responsibilities.reduce((s, r) => s + (r.pct ?? 0), 0);
  const meta = [
    record.salaryGrade,
    record.flsaStatus,
    record.unionCode && `Unit ${record.unionCode}`,
    record.supervises ? 'supervises' : null,
    record.leads ? 'leads' : null,
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <>
      <PageHeader
        back={{ label: 'Back to job descriptions', to: '/backend/job-descriptions' }}
        eyebrow={`${record.ucJobTitle} · code ${record.ucJobCode}`}
        title={record.workingTitle || record.ucJobTitle}
      />

      <div className="space-y-5">
        <Card className="p-5">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={originLabel[data.origin].tone}>{originLabel[data.origin].label}</Badge>
            {data.pending ? <Badge tone="yellow">not yet in an envelope</Badge> : null}
            {total !== 100 ? <Badge tone="muted">% time totals {total}</Badge> : null}
          </div>
          <div className="mt-2 text-base text-base-content/65">
            {[record.departmentName, record.division].filter(Boolean).join(' · ')}
          </div>
          {meta ? <div className="mt-0.5 text-base text-base-content/65">{meta}</div> : null}
          <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1">
            {data.classSlug ? (
              <>
                <Link
                  className="text-base font-semibold text-primary hover:underline"
                  params={{ slug: data.classSlug }}
                  to="/backend/edit/$slug"
                >
                  {data.classTitle} envelope →
                </Link>
                {data.pending ? null : (
                  <Link
                    className="text-base font-semibold text-primary hover:underline"
                    params={{ _splat: record.sourceFile, slug: data.classSlug }}
                    to="/backend/jd/$slug/$"
                  >
                    Compare with the envelope →
                  </Link>
                )}
              </>
            ) : (
              <span className="text-base text-base-content/65">
                Its class hasn&apos;t been built yet — re-ingest it to make one.
              </span>
            )}
          </div>
        </Card>

        {record.jobSummary ? (
          <Card className="p-5">
            <Eyebrow>Job summary</Eyebrow>
            <p className="mt-2 whitespace-pre-line text-base leading-relaxed">{record.jobSummary}</p>
          </Card>
        ) : null}

        <Card className="p-5">
          <Eyebrow>Key responsibilities</Eyebrow>
          <ul className="mt-3 space-y-4">
            {record.responsibilities.map((r, i) => (
              <li key={`${i}-${r.functionName}`}>
                <div className="flex items-baseline gap-2">
                  {r.pct == null ? null : <span className="text-base font-semibold tnum">{r.pct}%</span>}
                  <span className="text-base font-semibold">{r.functionName}</span>
                </div>
                {r.duties.length > 0 ? (
                  <ul className="ml-4 mt-1 space-y-0.5">
                    {r.duties.map((d, j) => (
                      <li className="list-disc text-base leading-snug text-base-content/80" key={`${j}-${d}`}>
                        {d}
                      </li>
                    ))}
                  </ul>
                ) : null}
              </li>
            ))}
          </ul>
        </Card>

        <Card className="p-5">
          <Eyebrow>Qualifications</Eyebrow>
          <div className="mt-3 grid gap-x-6 gap-y-3 md:grid-cols-2">
            <ItemList items={record.qualifications.ksaMin} label="Minimum KSAs" />
            <ItemList items={record.qualifications.ksaPref} label="Preferred KSAs" />
            <ItemList
              items={record.qualifications.education ? [record.qualifications.education] : []}
              label="Education"
            />
            <ItemList items={record.qualifications.minExperience} label="Minimum experience" />
            <ItemList items={record.qualifications.licenses} label="Licenses & certifications" />
          </div>
        </Card>

        {record.conditionsOfEmployment.length > 0 || record.workEnvironment.length > 0 ? (
          <Card className="p-5">
            <div className="grid gap-x-6 gap-y-3 md:grid-cols-2">
              <ItemList items={record.conditionsOfEmployment} label="Conditions of employment" />
              <ItemList items={record.workEnvironment} label="Work environment" />
            </div>
          </Card>
        ) : null}
      </div>
    </>
  );
}

const ItemList = ({ items, label }: { items: string[]; label: string }) => (
  <div>
    <div className="mb-1 text-sm font-semibold uppercase tracking-wide text-base-content/50">{label}</div>
    {items.length ? (
      <ul className="space-y-0.5">
        {items.map((it, i) => (
          <li className="ml-4 list-disc text-base leading-snug" key={`${i}-${it}`}>
            {it}
          </li>
        ))}
      </ul>
    ) : (
      <span className="text-base text-base-content/50">—</span>
    )}
  </div>
);
