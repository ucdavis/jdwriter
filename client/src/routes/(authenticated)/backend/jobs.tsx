import { JobsDirectory } from '@/features/backend/JobsDirectory.tsx';
import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/backend/jobs')({
  component: JobsPage,
});

function JobsPage() {
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · inputs"
        sub="Every UC job code, and what it's attached to: its class and its standard. Open a job to bootstrap it, or to add its standard by hand."
        title="Jobs"
      />
      <JobsDirectory />
    </AdminOnly>
  );
}
