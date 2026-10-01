import { createFileRoute, redirect } from '@tanstack/react-router';

/**
 * Editing lives inline on the class page. This route exists only so links that predate
 * that change — including the class-switch suggestion in the envelope check — still land
 * somewhere correct rather than 404ing.
 */
export const Route = createFileRoute('/(authenticated)/class/$slug/build')({
  beforeLoad: ({ params }) => {
    throw redirect({ params: { slug: params.slug }, to: '/class/$slug' });
  },
});
