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

/**
 * The back end — envelopes, review, ingest, standards, settings — is admin-only. Everyone
 * else is an Author. Cosmetic: the server enforces it regardless.
 */
export const ADMIN_ROLE = 'Admin';

/**
 * For route loaders that prefetch an admin-only endpoint. Prefetching it for anyone else
 * throws a 403 out of the loader, which surfaces the app-wide "not authorized to use this
 * application" page instead of AdminOnly's explanation — wrong for an Author, who is
 * fully authorized to use the app.
 */
export const isAdmin = async (queryClient: QueryClient) =>
  (await queryClient.ensureQueryData(meQueryOptions())).roles.includes(ADMIN_ROLE);

export const useMeQuery = () => {
  return useQuery(meQueryOptions());
};
