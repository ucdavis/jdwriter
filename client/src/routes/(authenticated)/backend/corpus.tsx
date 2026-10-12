import { createFileRoute, redirect } from '@tanstack/react-router';

/** The page this was ("Corpus & standards"), split into Descriptions and Standards. */
export const Route = createFileRoute('/(authenticated)/backend/corpus')({
  beforeLoad: () => {
    throw redirect({ replace: true, to: '/backend/descriptions' });
  },
});
