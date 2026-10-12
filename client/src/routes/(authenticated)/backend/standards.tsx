import {
  BlankStandardClassesPanel,
  BootstrapPanel,
  StandardMatchPanel,
  StandardsPanel,
  SupersessionPanel,
} from '@/features/backend/AdminPanels.tsx';
import { StandardsList } from '@/features/backend/StandardsList.tsx';
import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';
import { Section } from '@/features/backend/Section.tsx';

export const Route = createFileRoute('/(authenticated)/backend/standards')({
  component: StandardsPage,
});

function StandardsPage() {
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · inputs"
        sub="The official UC job standards: load them, match them to job codes, retire superseded classes, and start envelopes for classes with a standard but no JDs yet."
        title="Job standards"
      />
      <Section
        id="manage"
        sub="Every job, and what it's attached to, is on the Jobs page."
        title="Standards"
      >
        <StandardsPanel />
        <StandardMatchPanel />
        <SupersessionPanel />
        <BootstrapPanel />
        <BlankStandardClassesPanel />
      </Section>
      <Section
        id="all"
        sub="Every standard JDWriter has, the job code and class it's attached to, and the standard itself."
        title="All job standards"
      >
        <StandardsList />
      </Section>
    </AdminOnly>
  );
}
