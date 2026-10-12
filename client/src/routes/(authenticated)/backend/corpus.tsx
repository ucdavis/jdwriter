import { createFileRoute, redirect } from '@tanstack/react-router';

/** The page this was ("Corpus & standards") split into Job descriptions and Job standards. */
export const Route = createFileRoute('/(authenticated)/backend/corpus')({
  beforeLoad: () => {
    throw redirect({ replace: true, to: '/backend/job-descriptions' });
  },
});
