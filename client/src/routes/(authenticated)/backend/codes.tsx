import { JobsDirectory } from '@/features/backend/JobsDirectory.tsx';
import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';

// Route options must follow TanStack Router's order (search validation first) for its type inference.
/* eslint-disable perfectionist/sort-objects */
export const Route = createFileRoute('/(authenticated)/backend/codes')({
  validateSearch: (search: Record<string, unknown>): { code?: string } => ({
    code: typeof search.code === 'string' && search.code ? search.code : undefined,
  }),
  component: JobsPage,
});
/* eslint-enable perfectionist/sort-objects */

function JobsPage() {
  const { code } = Route.useSearch();
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · inputs"
        sub="Every UC job code, and what it's attached to: its class and its standard. Open a job to bootstrap it, or to add its standard by hand."
        title="Codes"
      />
      <JobsDirectory initialCode={code} key={code ?? ''} />
    </AdminOnly>
  );
}
