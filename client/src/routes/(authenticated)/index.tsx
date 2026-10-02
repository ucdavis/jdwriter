import { ClassPicker } from '@/features/browse/ClassPicker.tsx';
import { NlIntake } from '@/features/browse/NlIntake.tsx';
import { RecentJds } from '@/features/browse/RecentJds.tsx';
import { Card, Eyebrow, PageHeader } from '@/shared/ui/primitives.tsx';
import { classListQueryOptions, useClassList } from '@/queries/classes.ts';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/')({
  component: Home,
  loader: ({ context }: { context: RouterContext }) =>
    context.queryClient.ensureQueryData(classListQueryOptions()),
});

function Home() {
  const { data } = useClassList();
  const classes = data?.classes ?? [];
  const ready = classes.filter((c) => c.ready);

  return (
    <>
      <PageHeader
        eyebrow="Create a job description"
        sub="Pick a job class, describe the role you need, or drop in a description you already have. We'll show the standard, then guide you through a compliant JD on rails."
        title="Start with the role"
      />

      <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
        <div className="flex flex-col gap-5">
          <Card className="p-5">
            <Eyebrow>AI-powered request</Eyebrow>
            <p className="mb-3 mt-1 text-[13px] text-base-content/65">
              Describe what you need in plain language.
            </p>
            <NlIntake />
          </Card>
          <RecentJds />
        </div>

        <Card className="p-5">
          <Eyebrow>Browse job classes</Eyebrow>
          <p className="mb-3 mt-1 text-[13px] text-base-content/65">
            {ready.length} class{ready.length === 1 ? '' : 'es'} ingested and ready ·{' '}
            {classes.length - ready.length} known titles awaiting ingestion.
          </p>
          <ClassPicker classes={classes} />
        </Card>
      </div>

      <Card className="mt-5 p-5">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <Eyebrow>Already have a description?</Eyebrow>
            <p className="mt-1 max-w-xl text-[13px] text-base-content/65">
              Many units already have something written. Drop in the Word file, PDF or
              Position Description workbook and we&apos;ll match it to a class with a
              confidence level, a level check, and the duties that fall outside it.
            </p>
          </div>
          <Link className="btn btn-primary shrink-0" to="/classify">
            Classify a description
          </Link>
        </div>
      </Card>
    </>
  );
}
