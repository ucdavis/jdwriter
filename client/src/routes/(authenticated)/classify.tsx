import { DescriptionClassifier } from '@/features/classify/DescriptionClassifier.tsx';
import { usePageTitle } from '@/shared/ui/usePageTitle.ts';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/classify')({
  component: ClassifyPage,
});

function ClassifyPage() {
  usePageTitle('Classify a job description');
  return (
    <>
      <PageHeader
        back={{ label: 'Start', to: '/' }}
        eyebrow="Classify an existing description"
        sub="Drop in the description your unit already uses — a Word file, a PDF, or pasted text. AI reads it and matches it to a job class; we'll tell you how confident it is, and show exactly which duties fall inside and outside that class."
        title="Already have a description?"
      />
      <DescriptionClassifier />
    </>
  );
}
