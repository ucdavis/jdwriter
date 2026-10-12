import { DocumentImportPanel, PendingRebuildsPanel } from '@/features/backend/AdminPanels.tsx';
import { JobDescriptionList } from '@/features/backend/JobDescriptionList.tsx';
import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';
import { Section } from '@/features/backend/Section.tsx';

// Route options must follow TanStack Router's order (search validation first) for its type inference.
/* eslint-disable perfectionist/sort-objects */
export const Route = createFileRoute('/(authenticated)/backend/descriptions/')({
  validateSearch: (search: Record<string, unknown>): { q?: string } => ({
    q: typeof search.q === 'string' && search.q ? search.q : undefined,
  }),
  component: JobDescriptionsPage,
});
/* eslint-enable perfectionist/sort-objects */

function JobDescriptionsPage() {
  const { q } = Route.useSearch();
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · inputs"
        sub="The real job descriptions every envelope is built from: add them, re-ingest the classes they change, and read any one of them."
        title="Descriptions"
      />
      <Section
        id="add"
        sub="Parsed from Word and filed under the job code each states. A class's envelope takes in its new JDs when it is re-ingested."
        title="Add job descriptions"
      >
        <DocumentImportPanel />
        <PendingRebuildsPanel />
      </Section>
      <Section
        id="all"
        sub="Every job description in the corpus, newest first, whatever brought it in."
        title="All job descriptions"
      >
        <JobDescriptionList initialQuery={q} key={q ?? ''} />
      </Section>
    </AdminOnly>
  );
}
