import { fetchJson } from '../lib/api.ts';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  BootstrapResponse,
  IngestScanResponse,
  StandardsIngestResponse,
} from '../lib/contracts.ts';

export const ingestScanQueryOptions = () => ({
  queryFn: () => fetchJson<IngestScanResponse>('/api/admin/ingest/pending'),
  queryKey: ['admin', 'ingest', 'pending'] as const,
  // A scan walks the corpus directory; never serve a cached answer for it.
  staleTime: 0,
});

export const useIngestScan = () => useQuery(ingestScanQueryOptions());

/**
 * Ingest ONE class per request. A large class takes a minute or two on its own, so
 * batching them into a single call would let one slow class time out the whole run.
 */
export const useIngestClass = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (code: string) =>
      fetchJson<unknown>('/api/admin/ingest/class', {
        body: JSON.stringify({ code }),
        method: 'POST',
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'ingest', 'pending'] });
    },
  });
};

export const useIngestStandards = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      fetchJson<StandardsIngestResponse>('/api/admin/standards/ingest', {
        method: 'POST',
      }),
    // Standard-linked badges across the whole class list change on success.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['classes'] }),
  });
};

export const useBootstrapCandidates = () =>
  useMutation({
    mutationFn: () =>
      // A read, but run on demand from a button, so it stays a mutation.
      fetchJson<BootstrapResponse>('/api/admin/bootstrap/candidates'),
  });
