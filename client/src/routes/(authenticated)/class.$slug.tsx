import { Badge, Card, Eyebrow, PageHeader } from '@/shared/ui/primitives.tsx';
import { StandardDetail } from '@/features/build/StandardDetail.tsx';
import { useState } from 'react';
import { BuildFlow } from '@/features/build/BuildFlow.tsx';
import { asDraftState } from '@/features/build/useBuildState.ts';
import { savedJdQueryOptions } from '@/queries/jds.ts';
import { useQuery } from '@tanstack/react-query';
import { classProfileQueryOptions, useClassProfile } from '@/queries/classes.ts';
import { useIsAdmin } from '@/shared/ui/AppShell.tsx';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { ClassProfileResponse, Distribution, EnvelopeSource } from '@/lib/contracts.ts';
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
      <PageHeader back={{ label: 'Back to start', to: '/' }} title={profile.title} />

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
          overview={<EnvelopeOverview isAdmin={isAdmin} profile={profile} source={source} />}
          slug={profile.slug}
          title={profile.title}
        />
      ) : (
        <Card className="p-6">
          <p className="text-base text-base-content/65">
            This class has no envelope yet, so there is nothing to tailor. Ingest its JDs
            or bootstrap it from an official standard on the back end first.
          </p>
        </Card>
      )}
    </>
  );
}

/**
 * The envelope at a glance, kept short so the duties start above the fold: one summary, the job
 * standard and the out-of-envelope signals behind toggles, and the class facts in a compact row.
 */
const EnvelopeOverview = ({
  isAdmin,
  profile,
  source,
}: {
  isAdmin: boolean;
  profile: ClassProfileResponse;
  source: ReturnType<typeof sourceBadge>;
}) => {
  const [showStandard, setShowStandard] = useState(false);
  const [showOutside, setShowOutside] = useState(false);
  const envelope = profile.envelope;
  const outside = envelope?.outOfEnvelope ?? [];

  return (
    <Card className="p-5">
      <Eyebrow>Summary</Eyebrow>
      <p className="mt-1 text-base leading-relaxed" data-testid="envelope-summary">
        {envelope?.summary}
      </p>

      <div className="mt-4">
        <Eyebrow>Class facts</Eyebrow>
      </div>
      <dl className="mt-1 flex flex-wrap gap-x-6 gap-y-2 text-base" data-testid="envelope-facts">
        <FactChip label="UC job code" value={profile.ucJobCode} />
        <FactChip label="Salary grade" value={profile.salaryGrade.consensus ?? '—'} />
        <FactChip label="FLSA" value={profile.flsaStatus.consensus ?? '—'} />
        <FactChip label="Bargaining unit" value={profile.unionCode.consensus ?? '—'} />
        <FactChip label="Supervises" value={triStateLabel(profile.supervises)} />
        <FactChip label="Leads" value={triStateLabel(profile.leads)} />
        <FactChip label="Outdoors >50%" value={triStateLabel(profile.worksOutdoorsOver50pct)} />
        <FactChip label="Learned from" value={`${profile.corpusSize} JDs`} />
      </dl>

      <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-2">
        {profile.standard ? (
          <label className="flex cursor-pointer items-center gap-2 text-base font-semibold">
            <input
              checked={showStandard}
              className="toggle toggle-primary toggle-sm"
              onChange={() => setShowStandard((v) => !v)}
              type="checkbox"
            />
            Show the job standard
          </label>
        ) : null}
        {outside.length > 0 ? (
          <label className="flex cursor-pointer items-center gap-2 text-base font-semibold">
            <input
              checked={showOutside}
              className="toggle toggle-warning toggle-sm"
              onChange={() => setShowOutside((v) => !v)}
              type="checkbox"
            />
            Show what&apos;s outside this envelope
          </label>
        ) : null}
        <Badge tone={source.tone}>{source.label}</Badge>
      </div>

      {showStandard && profile.standard ? (
        <div className="mt-4">
          <StandardDetail standard={profile.standard} />
          {/* The standard's detail page is part of the back end, so only admins get the link. */}
          {isAdmin ? (
            <Link
              className="mt-2 inline-block text-base font-semibold text-info hover:underline"
              params={{ slug: profile.slug }}
              to="/backend/standard/$slug"
            >
              → Open the standard in the back end
            </Link>
          ) : null}
        </div>
      ) : null}

      {showOutside ? (
        <div className="mt-4 rounded-lg border border-warning/30 bg-warning/5 p-3.5" data-testid="outside-envelope">
          <div className="text-sm font-semibold uppercase tracking-wide text-warning">
            Not part of this class — work like this suggests a different class
          </div>
          <ul className="mt-2 space-y-1">
            {outside.map((o) => (
              <li className="flex gap-2 text-base text-base-content/75" key={o}>
                <span className="text-warning">⚠</span>
                <span>{o}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </Card>
  );
};

const FactChip = ({ label, value }: { label: string; value: string }) => (
  <div className="flex gap-1.5">
    <dt className="text-base-content/55">{label}</dt>
    <dd className="font-semibold tnum">{value}</dd>
  </div>
);
