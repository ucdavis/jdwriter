import { Badge, Card, Eyebrow, Fact, PageHeader } from '@/shared/ui/primitives.tsx';
import { BuildFlow } from '@/features/build/BuildFlow.tsx';
import { asDraftState } from '@/features/build/useBuildState.ts';
import { savedJdQueryOptions } from '@/queries/jds.ts';
import { useQuery } from '@tanstack/react-query';
import { classProfileQueryOptions, useClassProfile } from '@/queries/classes.ts';
import { useIsAdmin } from '@/shared/ui/AppShell.tsx';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { Distribution, EnvelopeSource } from '@/lib/contracts.ts';
import type { RouterContext } from '@/main.tsx';

// Route options must follow TanStack Router's order (search validation before loader deps
// before the loader) for its type inference, which outranks alphabetical key sorting.
/* eslint-disable perfectionist/sort-objects */
export const Route = createFileRoute('/(authenticated)/class/$slug')({
  // ?draft=<id> continues a saved JD instead of starting from the envelope.
  validateSearch: (search: Record<string, unknown>): { draft?: number } => {
    const draft = Number(search.draft);
    return Number.isInteger(draft) && draft > 0 ? { draft } : {};
  },
  loaderDeps: ({ search }) => ({ draft: search.draft }),
  loader: async ({
    context,
    deps,
    params,
  }: {
    context: RouterContext;
    deps: { draft?: number };
    params: { slug: string };
  }) => {
    await Promise.all([
      context.queryClient.ensureQueryData(classProfileQueryOptions(params.slug)),
      deps.draft ? context.queryClient.ensureQueryData(savedJdQueryOptions(deps.draft)) : null,
    ]);
  },
  component: ClassPage,
});
/* eslint-enable perfectionist/sort-objects */

/** A tri-state consensus reads as Yes / No / Not specified, never as a bare false. */
const triStateLabel = (d: Distribution): string =>
  d.consensus === 'true' ? 'Yes' : d.consensus === 'false' ? 'No' : 'Not specified';

const sourceBadge = (
  source: EnvelopeSource | null
): { label: string; tone: 'muted' | 'purple' | 'teal' | 'yellow' } => {
  switch (source) {
    case 'claude':
      return { label: 'AI-synthesized', tone: 'purple' };
    case 'manual':
      return { label: 'partner-edited', tone: 'teal' };
    case 'standard':
      return { label: 'standard-derived · no JDs yet', tone: 'yellow' };
    default:
      return { label: 'computed', tone: 'muted' };
  }
};

function ClassPage() {
  const { slug } = Route.useParams();
  const { draft: draftId } = Route.useSearch();
  const { data: saved } = useQuery({ ...savedJdQueryOptions(draftId ?? 0), enabled: draftId != null });
  // Only a draft of THIS class resumes here; anything else starts fresh from the envelope.
  const draft =
    draftId != null && saved && saved.slug === slug
      ? { id: draftId, state: asDraftState(saved.draftState) }
      : null;
  const { data: profile } = useClassProfile(slug);
  const isAdmin = useIsAdmin();

  if (!profile) {
    return null;
  }

  const envelope = profile.envelope;
  const source = sourceBadge(profile.envelopeSource);

  return (
    <>
      <PageHeader
        back={{ label: 'Back to start', to: '/' }}
        eyebrow={`${profile.ctJobFamily} · ${profile.ctJobFunction} · ${profile.personnelProgram}`}
        sub={`Standardized template for UC job code ${profile.ucJobCode}, learned from ${profile.corpusSize} existing job descriptions. Tailor it below.`}
        title={profile.title}
      />

      {envelope ? (
        <Card className="mb-5 p-5">
          <div className="flex items-center justify-between gap-3">
            <Eyebrow>The Job Envelope</Eyebrow>
            <div className="flex items-center gap-2">
              {/* "No standard" is the NORMAL state for roughly 46 of 65 classes — the
                  workbooks only cover 19 families — so it is stated plainly, not as a
                  warning. */}
              <Badge tone={profile.standard ? 'teal' : 'muted'}>
                {profile.standard ? 'standard linked' : 'no standard'}
              </Badge>
              <Badge tone={source.tone}>{source.label}</Badge>
            </div>
          </div>
          <p className="mt-2.5 text-[14px] leading-relaxed">{envelope.summary}</p>
          <p className="mt-2 text-[13px] leading-relaxed text-base-content/65">
            {envelope.scopeStatement}
          </p>
          {/* The standard's detail page is part of the back end, so only admins get the link. */}
          {profile.standard && isAdmin ? (
            <Link
              className="mt-3 inline-block text-[12.5px] font-medium text-info hover:underline"
              params={{ slug: profile.slug }}
              to="/backend/standard/$slug"
            >
              → Reference the official job standard ({profile.standard.longTitle})
            </Link>
          ) : null}
          {envelope.outOfEnvelope.length > 0 ? (
            <details className="mt-3">
              <summary className="cursor-pointer text-[11.5px] font-semibold uppercase tracking-wide text-warning">
                Outside this envelope → suggests a different class
              </summary>
              <ul className="mt-2 space-y-1">
                {envelope.outOfEnvelope.map((o) => (
                  <li className="flex gap-2 text-[12.5px] text-base-content/65" key={o}>
                    <span className="text-warning">⚠</span>
                    <span>{o}</span>
                  </li>
                ))}
              </ul>
            </details>
          ) : null}
        </Card>
      ) : null}

      <Card className="mb-6 p-5">
        <Eyebrow>The standard for this class</Eyebrow>
        <div className="mt-3 grid grid-cols-2 gap-x-6 gap-y-4 md:grid-cols-4">
          <Fact agreement={1} label="UC Job Code" value={profile.ucJobCode} />
          <Fact
            agreement={profile.salaryGrade.agreement}
            label="Salary Grade"
            value={profile.salaryGrade.consensus ?? '—'}
          />
          <Fact
            agreement={profile.flsaStatus.agreement}
            label="FLSA"
            value={profile.flsaStatus.consensus ?? '—'}
          />
          <Fact
            agreement={profile.unionCode.agreement}
            label="Bargaining Unit"
            value={profile.unionCode.consensus ?? '—'}
          />
          <Fact
            agreement={profile.supervises.agreement}
            label="Supervises"
            value={triStateLabel(profile.supervises)}
          />
          <Fact
            agreement={profile.leads.agreement}
            label="Leads"
            value={triStateLabel(profile.leads)}
          />
          <Fact
            agreement={profile.worksOutdoorsOver50pct.agreement}
            label="Outdoors >50%"
            value={triStateLabel(profile.worksOutdoorsOver50pct)}
          />
          <Fact agreement={1} label="Corpus" value={`${profile.corpusSize} JDs`} />
        </div>
      </Card>

      <div className="mb-3">
        <div className="eyebrow">Build the job description</div>
        <p className="mt-1 text-[13px] text-base-content/65">
          Keep what applies, drop what doesn&apos;t, add anything unit-specific — then
          assemble.
        </p>
      </div>

      {envelope ? (
        <BuildFlow
          draft={draft}
          envelope={{
            certs: envelope.requiredCertifications,
            education: envelope.education,
            minKSA: envelope.minQualifications,
            prefKSA: envelope.prefQualifications,
            responsibilities: envelope.keyResponsibilities,
            workEnvironment: envelope.workEnvironment,
            workExperience: envelope.workExperience,
          }}
          key={draft ? `draft-${draft.id}` : 'fresh'}
          slug={profile.slug}
          title={profile.title}
        />
      ) : (
        <Card className="p-6">
          <p className="text-[13px] text-base-content/65">
            This class has no envelope yet, so there is nothing to tailor. Ingest its JDs
            or bootstrap it from an official standard on the back end first.
          </p>
        </Card>
      )}
    </>
  );
}
