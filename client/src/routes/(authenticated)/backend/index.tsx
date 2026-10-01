import {
  BootstrapPanel,
  IngestPanel,
  StandardsPanel,
} from '@/features/backend/AdminPanels.tsx';
import { AnalystOnly } from '@/shared/ui/AppShell.tsx';
import { Badge, Card, PageHeader, Stat } from '@/shared/ui/primitives.tsx';
import type { Tone } from '@/shared/ui/primitives.tsx';
import { classSummaryQueryOptions, useClassSummary } from '@/queries/classes.ts';
import { isAnalyst } from '@/queries/user.ts';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { ClassSummary, EnvelopeSource } from '@/lib/contracts.ts';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/')({
  component: BackendPage,
  loader: async ({ context }: { context: RouterContext }) => {
    if (await isAnalyst(context.queryClient)) {
      await context.queryClient.ensureQueryData(classSummaryQueryOptions());
    }
  },
});

const sourceBadge: Record<EnvelopeSource, { label: string; tone: Tone }> = {
  claude: { label: 'AI-synthesized', tone: 'purple' },
  deterministic: { label: 'computed', tone: 'muted' },
  manual: { label: 'partner-edited', tone: 'teal' },
  standard: { label: 'standard-derived · no JDs yet', tone: 'yellow' },
};

const pct = (share: number) => `${Math.round(share * 100)}%`;

function BackendPage() {
  return (
    <AnalystOnly>
      <EnvelopeIndex />
    </AnalystOnly>
  );
}

// Inside the gate so the Analyst-only query never fires for an Author.
function EnvelopeIndex() {
  const { data } = useClassSummary();
  const classes = data?.classes ?? [];

  return (
    <>
      <PageHeader
        back={{ label: 'Back to start', to: '/' }}
        eyebrow="Back end · averaging engine"
        sub="Every class the engine has ingested, with its computed envelope and provenance. Drop more JD folders into the corpus and ingest them below."
        title="Job Envelopes"
      />

      <div className="mb-4">
        <Link
          className="text-[13px] font-medium text-primary hover:underline"
          to="/backend/fit"
        >
          → Reclassification review (goodness-of-fit)
        </Link>
      </div>

      <IngestPanel />
      <StandardsPanel />
      <BootstrapPanel />

      {classes.length === 0 ? (
        <Card className="p-6">
          <p className="text-[13px] text-base-content/65">
            No envelopes yet. Ingest a class above to build one.
          </p>
        </Card>
      ) : null}

      <div className="space-y-5">
        {classes.map((c) => (
          <SummaryCard c={c} key={c.slug} />
        ))}
      </div>
    </>
  );
}

function SummaryCard({ c }: { c: ClassSummary }) {
  const source = c.envelopeSource ? sourceBadge[c.envelopeSource] : null;
  const hasJds = c.corpusSize > 0;

  return (
    <Card className="p-5">
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            {c.standardLinked && hasJds ? (
              <span
                className="inline-flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full bg-success/10 text-[12px] font-bold text-success"
                title="All components ingested — JDs + official standard"
              >
                ✓
              </span>
            ) : null}
            <h2 className="text-[17px] font-bold">{c.title}</h2>
            <Badge tone="accent">Code {c.ucJobCode || '—'}</Badge>
            {source ? <Badge tone={source.tone}>{source.label}</Badge> : null}
            {c.standardLinked ? <Badge tone="green">standard linked</Badge> : null}
            {/* The envelope is ours to get right, so a total off 100 is the first thing to fix. */}
            {c.hasEnvelope && c.envelopePctTotal !== 100 ? (
              <Badge tone="red">% time sums to {c.envelopePctTotal}</Badge>
            ) : null}
            {!c.hasEnvelope ? <Badge tone="orange">no envelope</Badge> : null}
            {hasJds && c.consolidatedFunctions === 0 ? (
              <Badge tone="muted">not consolidated</Badge>
            ) : null}
          </div>
          <div className="mt-1 text-[12px] text-base-content/65">
            {[c.ctJobFamily, c.ctJobFunction, c.personnelProgram]
              .filter(Boolean)
              .join(' · ')}
          </div>
        </div>
        <div className="flex items-center gap-3 whitespace-nowrap">
          <Link
            className="text-[13px] font-medium text-primary hover:underline"
            params={{ slug: c.slug }}
            to="/backend/edit/$slug"
          >
            Edit envelope
          </Link>
          <Link
            className="text-[13px] font-medium text-base-content/65 hover:underline"
            params={{ slug: c.slug }}
            to="/class/$slug"
          >
            Open →
          </Link>
        </div>
      </div>

      <div className="mt-4 grid grid-cols-2 gap-x-6 gap-y-3 md:grid-cols-5">
        <Stat label="Corpus" value={`${c.corpusSize} JDs`} />
        <Stat label="Grade" value={c.grade ?? '—'} />
        <Stat label="Responsibilities" value={`${c.responsibilities}`} />
        <Stat label="KSAs" value={`${c.ksas}`} />
        <Stat
          label="Envelope match"
          value={
            !hasJds
              ? 'no JDs yet'
              : c.wellCoveredPct === null
                ? 'not computed'
                : pct(c.wellCoveredPct)
          }
        />
      </div>

      {hasJds ? (
        <div className="mt-3">
          <Link
            className="text-[12px] font-medium text-primary hover:underline"
            params={{ slug: c.slug }}
            to="/backend/jds/$slug"
          >
            {c.wellCoveredPct === null || c.meanCoverage === null
              ? 'Backwards coverage — do the real JDs match this template? →'
              : `Backwards coverage · ${pct(c.wellCoveredPct)} of JDs are ≥90% covered (mean ${c.meanCoverage}%) →`}
          </Link>
        </div>
      ) : null}
    </Card>
  );
}
