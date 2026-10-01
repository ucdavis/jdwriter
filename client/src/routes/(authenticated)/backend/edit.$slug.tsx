import { AnalystOnly } from '@/shared/ui/AppShell.tsx';
import { Card, PageHeader } from '@/shared/ui/primitives.tsx';
import { classProfileQueryOptions, useClassProfile } from '@/queries/classes.ts';
import { createFileRoute, Link } from '@tanstack/react-router';
import { EnvelopeEditor } from '@/features/backend/EnvelopeEditor.tsx';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/edit/$slug')({
  component: EditEnvelopePage,
  loader: ({
    context,
    params,
  }: {
    context: RouterContext;
    params: { slug: string };
  }) => context.queryClient.ensureQueryData(classProfileQueryOptions(params.slug)),
});

function EditEnvelopePage() {
  const { slug } = Route.useParams();
  const { data: profile } = useClassProfile(slug);

  if (!profile) {
    return null;
  }

  return (
    <AnalystOnly>
      <PageHeader
        back={{ label: 'Back to envelopes', to: '/backend' }}
        eyebrow={`${profile.title} · code ${profile.ucJobCode}`}
        sub="Clarify language and set the standard for this class. Changes save into the class profile and drive the authoring flow."
        title="Edit the standard envelope"
      />
      {profile.standard ? (
        <Link
          className="mb-4 inline-block text-[12.5px] font-medium text-info hover:underline"
          params={{ slug }}
          to="/backend/standard/$slug"
        >
          → Reference the official job standard ({profile.standard.longTitle})
        </Link>
      ) : null}
      {profile.envelope ? (
        <EnvelopeEditor initial={profile.envelope} slug={slug} />
      ) : (
        <Card className="p-6">
          <p className="text-[13px] text-base-content/65">
            This class has no envelope to edit yet. Ingest its JDs, or bootstrap it from an
            official standard, first.
          </p>
        </Card>
      )}
    </AnalystOnly>
  );
}
