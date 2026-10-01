import { DescriptionClassifier } from '@/features/classify/DescriptionClassifier.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/classify')({
  component: ClassifyPage,
});

function ClassifyPage() {
  return (
    <>
      <PageHeader
        back={{ label: 'Start', to: '/' }}
        eyebrow="Classify an existing description"
        sub="Drop in the description your unit already uses — a Word file, a PDF, or pasted text. We'll match it to a job class, tell you how confident we are, and show exactly which duties fall inside and outside that class."
        title="Already have a description?"
      />
      <DescriptionClassifier />
    </>
  );
}
