import {
  BootstrapPanel,
  IngestPanel,
  StandardsPanel,
  SupersessionPanel,
  UploadPanel,
} from '@/features/backend/AdminPanels.tsx';
import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';
import type { ReactNode } from 'react';

export const Route = createFileRoute('/(authenticated)/backend/corpus')({
  component: CorpusPage,
});

function CorpusPage() {
  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · inputs"
        sub="Everything the envelopes are built from: real job descriptions, and the official UC job standards that cover classes with no JDs yet."
        title="Corpus & standards"
      />

      <Section
        id="jds"
        sub="Upload HRTMS exports, or ingest new classes from the corpus folder. Each class is rebuilt from its JDs."
        title="Add job descriptions"
      >
        <UploadPanel />
        <IngestPanel />
      </Section>

      <Section
        id="standards"
        sub="Load standards, retire classes a union title has superseded, and create starter envelopes for classes with a standard but no JDs."
        title="Standards"
      >
        <StandardsPanel />
        <SupersessionPanel />
        <BootstrapPanel />
      </Section>
    </AdminOnly>
  );
}

const Section = ({
  children,
  id,
  sub,
  title,
}: {
  children: ReactNode;
  id: string;
  sub: string;
  title: string;
}) => (
  <section aria-labelledby={`${id}-heading`} className="mb-8">
    <h2 className="text-[17px] font-bold" id={`${id}-heading`}>
      {title}
    </h2>
    <p className="mb-3 mt-0.5 text-[13px] text-base-content/65">{sub}</p>
    {children}
  </section>
);
