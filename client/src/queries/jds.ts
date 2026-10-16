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

/** Rewrite an assembled JD's summary. Its downloads and its corpus copy follow the saved record. */
export const useSetSummary = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, summary }: { id: number; summary: string }) =>
      fetchJson<{ jobSummary: string; summaryEdited: boolean }>(`/api/jds/${id}/summary`, {
        body: JSON.stringify({ summary }),
        method: 'PUT',
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jds'] }),
  });
};

/** Delete a saved JD. Its corpus copy, if it has one, goes with it. */
export const useDeleteJd = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => fetchJson<{ ok: true }>(`/api/jds/${id}`, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jds'] }),
  });
};
