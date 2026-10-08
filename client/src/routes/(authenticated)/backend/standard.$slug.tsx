import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { Card, PageHeader } from '@/shared/ui/primitives.tsx';
import { classProfileQueryOptions, useClassProfile } from '@/queries/classes.ts';
import { createFileRoute } from '@tanstack/react-router';
import { StandardDetail } from '@/features/build/StandardDetail.tsx';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/backend/standard/$slug')({
  component: StandardPage,
  loader: ({
    context,
    params,
  }: {
    context: RouterContext;
    params: { slug: string };
  }) => context.queryClient.ensureQueryData(classProfileQueryOptions(params.slug)),
});

function StandardPage() {
  const { slug } = Route.useParams();
  const { data: profile } = useClassProfile(slug);

  if (!profile) {
    return null;
  }

  return (
    <AdminOnly>
      <PageHeader
        back={{ label: `Back to ${profile.title}`, params: { slug }, to: '/class/$slug' }}
        eyebrow={`${profile.title} · code ${profile.ucJobCode}`}
        sub="The authoritative UC classification standard for this class — reference it while tailoring the envelope."
        title="Official job standard"
      />
      {profile.standard ? (
        <StandardDetail standard={profile.standard} />
      ) : (
        /* Roughly 46 of 65 classes have no standard, because the workbooks only cover 19
           families. That is a coverage fact about our sources, not a failure to match, so
           it reads as information rather than as an error. */
        <Card className="p-6">
          <p className="text-sm text-base-content/65">
            No official standard is linked to this class. The job-standard workbooks cover
            19 of the families in the corpus, so most classes legitimately have none —
            ingest the family workbook on the back end if one exists.
          </p>
        </Card>
      )}
    </AdminOnly>
  );
}
