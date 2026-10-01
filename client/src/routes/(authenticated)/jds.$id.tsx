import { Card, Eyebrow, PageHeader } from '@/shared/ui/primitives.tsx';
import { FinalJd } from '@/features/build/FinalJd.tsx';
import { savedJdQueryOptions } from '@/queries/jds.ts';
import { createFileRoute } from '@tanstack/react-router';
import { useSuspenseQuery } from '@tanstack/react-query';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/jds/$id')({
  component: SavedJdPage,
  loader: ({ context, params }: { context: RouterContext; params: { id: string } }) =>
    context.queryClient.ensureQueryData(savedJdQueryOptions(Number(params.id))),
});

function SavedJdPage() {
  const { id } = Route.useParams();
  const { data: jd } = useSuspenseQuery(savedJdQueryOptions(Number(id)));

  return (
    <>
      <PageHeader
        back={{ label: 'Back to saved JDs', to: '/jds' }}
        eyebrow={`Saved ${new Date(jd.updatedAt).toLocaleString()}${jd.createdBy ? ` · ${jd.createdBy}` : ''}`}
        title={jd.workingTitle || jd.title}
      />

      {jd.authorAdditions.length > 0 || jd.notes ? (
        <Card className="mb-5 p-5">
          {jd.authorAdditions.length > 0 ? (
            <>
              <Eyebrow>Added beyond the envelope</Eyebrow>
              <ul className="mt-2 list-disc space-y-1 pl-5 text-[13px]">
                {jd.authorAdditions.map((a) => (
                  <li key={a}>{a}</li>
                ))}
              </ul>
            </>
          ) : null}
          {jd.notes ? (
            <div className={jd.authorAdditions.length > 0 ? 'mt-4' : ''}>
              <Eyebrow>Notes to HR</Eyebrow>
              <p className="mt-1 text-[13px]">{jd.notes}</p>
            </div>
          ) : null}
        </Card>
      ) : null}

      <FinalJd result={jd} />
    </>
  );
}
