import { fetchJson } from '../lib/api.ts';
import { type QueryClient, useQuery } from '@tanstack/react-query';

export type User = {
  email: string;
  iamId: string | null;
  id: string;
  name: string;
  roles: string[];
};

export const meQueryOptions = () => ({
  queryFn: async (): Promise<User> => {
    return await fetchJson<User>('/api/user/me');
  },
  queryKey: ['users', 'me'] as const,
  staleTime: 5 * 60_000, // 5 minutes
});

/** HR classification analysts get the curation surfaces. Cosmetic — the server enforces it. */
export const ANALYST_ROLE = 'Analyst';

/**
 * For route loaders that prefetch an Analyst-only endpoint. Prefetching it for anyone else
 * throws a 403 out of the loader, which surfaces the app-wide "not authorized to use this
 * application" page instead of AnalystOnly's explanation — wrong for an Author who is
 * simply not an analyst.
 */
export const isAnalyst = async (queryClient: QueryClient) =>
  (await queryClient.ensureQueryData(meQueryOptions())).roles.includes(ANALYST_ROLE);

export const useMeQuery = () => {
  return useQuery(meQueryOptions());
};
