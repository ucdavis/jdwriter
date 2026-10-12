import { DocumentImportPanel, PendingRebuildsPanel } from '@/features/backend/AdminPanels.tsx';
import { JobDescriptionList } from '@/features/backend/JobDescriptionList.tsx';
import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';
import { Section } from '@/features/backend/Section.tsx';

export const Route = createFileRoute('/(authenticated)/backend/job-descriptions/')({
  component: JobDescriptionsPage,
});

function JobDescriptionsPage() {
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · inputs"
        sub="The real job descriptions every envelope is built from: add them, re-ingest the classes they change, and read any one of them."
        title="Job descriptions"
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
        <JobDescriptionList />
      </Section>
    </AdminOnly>
  );
}
