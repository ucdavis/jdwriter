import { fetchJson } from '../lib/api.ts';
import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

export type User = {
  /** When this person, not yet approved, asked for access; null if they haven't. */
  accessRequestedAt?: string | null;
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
 * Using JDWriter at all. Only approved logins and admins have it (the allow-list); anyone else who
 * signs in sees only the page to ask for access.
 */
export const AUTHOR_ROLE = 'Author';

export const hasAccess = (user: Pick<User, 'roles'>) => user.roles.includes(AUTHOR_ROLE);

/** Ask to use JDWriter: lands in the admins' Reports inbox. */
export const useRequestAccess = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (message: string) =>
      fetchJson<{ hasAccess: boolean; requestedAt: string | null }>('/api/user/access-request', {
        body: JSON.stringify({ message }),
        method: 'POST',
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['users', 'me'] }),
  });
};

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
