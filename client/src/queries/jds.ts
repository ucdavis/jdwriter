import { fetchJson } from '../lib/api.ts';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { SavedJd, SavedJdListResponse } from '../lib/contracts.ts';

export type JdScope = 'all' | 'mine';

export const savedJdsQueryOptions = (scope: JdScope = 'mine') => ({
  queryFn: () =>
    fetchJson<SavedJdListResponse>(scope === 'all' ? '/api/jds?scope=all' : '/api/jds'),
  queryKey: ['jds', scope] as const,
});

export const useSavedJds = (scope: JdScope = 'mine') => useQuery(savedJdsQueryOptions(scope));

export const savedJdQueryOptions = (id: number) => ({
  queryFn: () => fetchJson<SavedJd>(`/api/jds/${id}`),
  queryKey: ['jds', 'one', id] as const,
});

/** Delete a saved JD. Its corpus copy, if it has one, goes with it. */
export const useDeleteJd = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => fetchJson<{ ok: true }>(`/api/jds/${id}`, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jds'] }),
  });
};
