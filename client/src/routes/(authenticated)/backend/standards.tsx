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

type StandardsSearch = { open?: string; q?: string };

// Route options must follow TanStack Router's order (search validation first) for its type inference.
/* eslint-disable perfectionist/sort-objects */
export const Route = createFileRoute('/(authenticated)/backend/standards')({
  validateSearch: (search: Record<string, unknown>): StandardsSearch => ({
    open: typeof search.open === 'string' && search.open ? search.open : undefined,
    q: typeof search.q === 'string' && search.q ? search.q : undefined,
  }),
  component: StandardsPage,
});
/* eslint-enable perfectionist/sort-objects */

function StandardsPage() {
  const { open, q } = Route.useSearch();
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · inputs"
        sub="The official UC job standards: load them, match them to job codes, retire superseded classes, and start envelopes for classes with a standard but no JDs yet."
        title="Standards"
      />
      <Section
        id="manage"
        sub="Every UC job code, and what it's attached to, is on the Codes page."
        title="Load and match"
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
        title="All standards"
      >
        <StandardsList initialOpen={open} initialQuery={q} key={`${q ?? ''}|${open ?? ''}`} />
      </Section>
    </AdminOnly>
  );
}
