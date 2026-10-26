import { usePageTitle } from '@/shared/ui/usePageTitle.ts';
import { VrfFlow } from '@/features/vrf/VrfFlow.tsx';
import { PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/vrf')({
  component: VrfPage,
  validateSearch: (search: Record<string, unknown>): { jd?: number } => {
    const jd = Number(search.jd);
    return Number.isInteger(jd) && jd > 0 ? { jd } : {};
  },
});

function VrfPage() {
  usePageTitle('Vacancy request');
  const { jd } = Route.useSearch();

  return (
    <>
      <PageHeader
        eyebrow="Vacancy Request Form"
        sub="Everything HR needs for the campus VRF, a step at a time. Download it at the end and attach it, with the job description and an org chart, to your AggieService ticket. Nothing is saved: your answers stay here until you reload the page."
        title="Vacancy request"
      />
      <VrfFlow jdId={jd} />
    </>
  );
}
