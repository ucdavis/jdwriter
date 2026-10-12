import { createFileRoute, redirect } from '@tanstack/react-router';

/** The page this was ("Jobs"), renamed Codes. A link to one job keeps its code. */
// Route options must follow TanStack Router's order (search validation first) for its type inference.
/* eslint-disable perfectionist/sort-objects */
export const Route = createFileRoute('/(authenticated)/backend/jobs')({
  validateSearch: (search: Record<string, unknown>): { code?: string } => ({
    code: typeof search.code === 'string' && search.code ? search.code : undefined,
  }),
  beforeLoad: ({ search }) => {
    throw redirect({ replace: true, search: { code: search.code }, to: '/backend/codes' });
  },
});
/* eslint-enable perfectionist/sort-objects */
