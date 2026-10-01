import { fetchJson } from '../lib/api.ts';
import { useQuery } from '@tanstack/react-query';
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
