import { fetchJson } from '../lib/api.ts';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  ApiKeyStatus,
  AdminsResponse,
  BootstrapCreateResponse,
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

/**
 * Create a standard-derived envelope for one candidate class. It is marked as such (% time
 * is estimated) and converges to a learned envelope once JDs for the class are ingested.
 */
export const useBootstrapClass = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (title: string) =>
      fetchJson<BootstrapCreateResponse>('/api/admin/bootstrap', {
        body: JSON.stringify({ title }),
        method: 'POST',
      }),
    // A new class appears in the class list and the analyst index.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['classes'] }),
  });
};

export const adminsQueryOptions = () => ({
  queryFn: () => fetchJson<AdminsResponse>('/api/admin/admins'),
  queryKey: ['admin', 'admins'] as const,
});

export const useAdmins = () => useQuery(adminsQueryOptions());

export const useGrantAdmin = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (loginId: string) =>
      fetchJson<{ loginId: string }>('/api/admin/admins', {
        body: JSON.stringify({ loginId }),
        method: 'POST',
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'admins'] }),
  });
};

export const useRevokeAdmin = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (loginId: string) =>
      fetchJson<{ ok: true }>(`/api/admin/admins/${encodeURIComponent(loginId)}`, {
        method: 'DELETE',
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'admins'] }),
  });
};

export const apiKeyQueryOptions = () => ({
  queryFn: () => fetchJson<ApiKeyStatus>('/api/admin/settings/api-key'),
  queryKey: ['admin', 'api-key'] as const,
});

export const useApiKeyStatus = () => useQuery(apiKeyQueryOptions());

const useApiKeyMutation = <TArg,>(request: (arg: TArg) => Promise<ApiKeyStatus>) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: request,
    // The response IS the new status, so it replaces the cached one directly.
    onSuccess: (status) => queryClient.setQueryData(['admin', 'api-key'], status),
  });
};

/** Write-only: the key goes up once and only its status ever comes back. */
export const useSetApiKey = () =>
  useApiKeyMutation((key: string) =>
    fetchJson<ApiKeyStatus>('/api/admin/settings/api-key', {
      body: JSON.stringify({ key }),
      method: 'PUT',
    })
  );

export const useClearApiKey = () =>
  useApiKeyMutation(() =>
    fetchJson<ApiKeyStatus>('/api/admin/settings/api-key', { method: 'DELETE' })
  );
