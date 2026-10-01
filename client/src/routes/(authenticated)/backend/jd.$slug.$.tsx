import { AnalystOnly } from '@/shared/ui/AppShell.tsx';
import { Badge, Card, Eyebrow, PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute, Link } from '@tanstack/react-router';
import { jdDetailQueryOptions } from '@/queries/classes.ts';
import { useSuspenseQuery } from '@tanstack/react-query';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/jd/$slug/$')({
  component: JdReviewPage,
  loader: ({
    context,
    params,
  }: {
    context: RouterContext;
    params: { _splat?: string; slug: string };
  }) =>
    context.queryClient.ensureQueryData(
      jdDetailQueryOptions(params.slug, params._splat ?? '')
    ),
});

/**
 * One real job description beside its class's standard envelope, with each responsibility
 * marked in-envelope or idiosyncratic.
 *
 * The in/out marking is computed SERVER-SIDE against the class's consolidated members, so
 * it is unaffected by how the envelope happens to be worded. The idiosyncratic items are
 * the drift the review-and-nudge workflow exists to pull back toward standard.
 */
function JdReviewPage() {
  const { _splat, slug } = Route.useParams();
  const { data } = useSuspenseQuery(jdDetailQueryOptions(slug, _splat ?? ''));
  const { envelope, profile, record } = data;

  const total = record.responsibilities.reduce((s, r) => s + (r.pct ?? 0), 0);
  const covered = record.responsibilities
    .filter((r) => r.inEnvelope)
    .reduce((s, r) => s + (r.pct ?? 0), 0);
  const coveredPct = total ? Math.round((covered / total) * 100) : 100;
  const tone = coveredPct >= 90 ? 'green' : coveredPct >= 70 ? 'yellow' : 'red';

  const meta = [
    record.salaryGrade,
    record.flsaStatus,
    record.unionCode && `Unit ${record.unionCode}`,
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <AnalystOnly>
      <PageHeader
        back={{ label: 'Back to review', to: '/backend/fit' }}
        eyebrow={`${profile.title} · code ${profile.ucJobCode}`}
        sub="How this real job description compares to the class's standard envelope. Idiosyncratic responsibilities are the drift we want to gently pull back toward standard."
        title="Review JD vs standard"
      />

      <div className="space-y-5">
        <Card className="p-5">
          <Eyebrow>Job description</Eyebrow>
          <h2 className="mt-1 text-[19px] font-bold">
            {record.workingTitle || record.ucJobTitle}
          </h2>
          <div className="mt-1 text-[12.5px] text-base-content/65">
            {record.ucJobTitle} · code <span className="tnum">{record.ucJobCode}</span>
            {record.departmentName ? ` · ${record.departmentName}` : ''}
          </div>
          <div className="mt-1 text-[12px] text-base-content/65">
            {meta}
            {record.supervises ? ' · supervises' : ''}
            {record.leads ? ' · leads' : ''}
          </div>
          <div className="mt-3 flex items-center gap-2">
            <Badge tone={tone}>{coveredPct}% fits the standard envelope</Badge>
            <Link
              className="text-[12px] text-primary hover:underline"
              params={{ slug: profile.slug }}
              to="/backend/edit/$slug"
            >
              View {profile.title} envelope →
            </Link>
          </div>
        </Card>

        {record.jobSummary || envelope?.summary ? (
          <div className="grid gap-4 md:grid-cols-2">
            <Card className="p-4">
              <Eyebrow>This JD — summary</Eyebrow>
              <p className="mt-2 text-[12.5px] leading-relaxed">
                {record.jobSummary || '—'}
              </p>
            </Card>
            <Card className="bg-base-200 p-4">
              <Eyebrow>Standard envelope — summary</Eyebrow>
              <p className="mt-2 text-[12.5px] leading-relaxed">
                {envelope?.summary || '—'}
              </p>
            </Card>
          </div>
        ) : null}

        <div className="grid gap-4 md:grid-cols-2">
          <Card className="p-4">
            <Eyebrow>This JD — responsibilities</Eyebrow>
            <ul className="mt-3 space-y-3">
              {record.responsibilities.map((r) => (
                <li key={r.functionName}>
                  <div className="flex items-center gap-2">
                    {r.pct == null ? null : (
                      <span className="text-[12px] font-semibold tnum">{r.pct}%</span>
                    )}
                    <span className="text-[12.5px] font-medium">{r.functionName}</span>
                    <Badge tone={r.inEnvelope ? 'green' : 'yellow'}>
                      {r.inEnvelope ? 'in envelope' : 'idiosyncratic'}
                    </Badge>
                  </div>
                  {r.duties.length > 0 ? (
                    <ul className="ml-4 mt-1 space-y-0.5">
                      {r.duties.map((d) => (
                        <li
                          className="list-disc text-[11.5px] leading-snug text-base-content/65"
                          key={d}
                        >
                          {d}
                        </li>
                      ))}
                    </ul>
                  ) : null}
                </li>
              ))}
            </ul>
          </Card>
          <Card className="bg-base-200 p-4">
            <Eyebrow>Standard envelope — key responsibilities</Eyebrow>
            <ul className="mt-3 space-y-3">
              {(envelope?.keyResponsibilities ?? []).map((r) => (
                <li key={r.functionName}>
                  <div className="flex items-center gap-2">
                    <span className="text-[12px] font-semibold tnum">{r.pctTime}%</span>
                    <span className="text-[12.5px] font-medium">{r.functionName}</span>
                  </div>
                  <ul className="ml-4 mt-1 space-y-0.5">
                    {r.duties.slice(0, 5).map((d) => (
                      <li
                        className="list-disc text-[11.5px] leading-snug text-base-content/65"
                        key={d}
                      >
                        {d}
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
              {!envelope ? (
                <li className="text-[12px] text-base-content/65">No envelope yet.</li>
              ) : null}
            </ul>
          </Card>
        </div>

        <Card className="p-4">
          <Eyebrow>This JD — qualifications</Eyebrow>
          <div className="mt-3 grid gap-x-6 gap-y-3 md:grid-cols-2">
            <QualList items={record.qualifications.licenses} label="Licenses & certifications" />
            <QualList
              items={record.qualifications.education ? [record.qualifications.education] : []}
              label="Education"
            />
            <QualList items={record.qualifications.minExperience} label="Minimum experience" />
            <QualList items={record.qualifications.ksaMin} label="Minimum KSAs" />
            <QualList items={record.qualifications.ksaPref} label="Preferred KSAs" />
          </div>
        </Card>
      </div>
    </AnalystOnly>
  );
}

const QualList = ({ items, label }: { items: string[]; label: string }) => (
  <div>
    <div className="mb-1 text-[10.5px] font-semibold uppercase tracking-wide text-base-content/50">
      {label}
    </div>
    {items.length ? (
      <ul className="space-y-0.5">
        {items.map((it) => (
          <li className="ml-4 list-disc text-[12px] leading-snug" key={it}>
            {it}
          </li>
        ))}
      </ul>
    ) : (
      <span className="text-[12px] text-base-content/50">—</span>
    )}
  </div>
);
