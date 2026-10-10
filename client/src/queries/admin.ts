import { fetchJson } from '../lib/api.ts';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  AnalyticsReport,
  StandardsUploadResponse,
  SecurityStatus,
  UploadedPendingResponse,
  QualificationRulesSummary,
  UploadOutcome,
  UploadResponse,
  ApiKeyStatus,
  AdminsResponse,
  BootstrapCreateResponse,
  BootstrapResponse,
  CoverageResponse,
  EnvelopeImportResult,
  IngestScanResponse,
  RetirementResult,
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
    /**
     * `allowNotActive` builds a job not active at UC Davis — by an admin's explicit choice only;
     * every list and bulk path leaves those out.
     */
    mutationFn: (title: string | { allowNotActive: boolean; title: string }) =>
      fetchJson<BootstrapCreateResponse>('/api/admin/bootstrap', {
        body: JSON.stringify(typeof title === 'string' ? { title } : title),
        method: 'POST',
      }),
    // A new class appears in the class list, the analyst index and the coverage lists.
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'coverage'] });
    },
  });
};

/** UC Davis jobs with no class yet — active, or not active at UC Davis — and their standards. */
export const coverageQueryOptions = (active: boolean) => ({
  queryFn: () => fetchJson<CoverageResponse>(`/api/admin/coverage?active=${active}`),
  queryKey: ['admin', 'coverage', active] as const,
});

export const useCoverage = (active: boolean) => useQuery(coverageQueryOptions(active));

/** Profiles still filed under a code a union successor has superseded. Read-only. */
export const useRetirementPreview = () =>
  useMutation({
    mutationFn: () =>
      // A read, but run on demand from a button, so it stays a mutation.
      fetchJson<RetirementResult>('/api/admin/supersessions/retire'),
  });

/**
 * Retire every profile under a superseded code. Idempotent, so "Create all" runs it first
 * without asking: a retired class must not hide its successor's standard from the candidates.
 */
export const useRetireSuperseded = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      fetchJson<RetirementResult>('/api/admin/supersessions/retire', {
        method: 'POST',
      }),
    // Classes are merged, renamed or removed.
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

export const uploadedPendingQueryOptions = () => ({
  queryFn: () => fetchJson<UploadedPendingResponse>('/api/admin/uploads/pending'),
  queryKey: ['admin', 'uploads', 'pending'] as const,
});

export const useUploadedPending = () => useQuery(uploadedPendingQueryOptions());

/** Upload HRTMS exports. Each file comes back with its own verdict. */
export const useUploadExports = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (files: File[]) => {
      const body = new FormData();
      for (const file of files) {
        // A folder pick supplies the relative path, which keeps the class folder in the name.
        body.append('files', file, file.webkitRelativePath || file.name);
      }
      return fetchJson<UploadResponse>('/api/admin/uploads', { body, method: 'POST' });
    },
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ['admin', 'uploads', 'pending'] }),
  });
};

/**
 * Add ONE JD from a unit outside the college (a Word, PDF or text copy). One document per request:
 * each is a model call, so a folder is sent file by file and each verdict shows as it lands. The
 * caller refreshes the rebuild queue once the whole folder is done.
 */
export const useImportDocument = () =>
  useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append('file', file, file.name);
      return fetchJson<UploadOutcome>('/api/admin/uploads/documents', { body, method: 'POST' });
    },
  });

/**
 * Apply the education rules to existing envelopes: a preferred degree moves to preferred
 * qualifications, and a degree reads "or equivalent experience". `apply: false` previews.
 */
export const useQualificationRules = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (apply: boolean) =>
      fetchJson<QualificationRulesSummary>('/api/admin/envelopes/qualification-rules', {
        body: JSON.stringify({ apply }),
        method: 'POST',
      }),
    onSuccess: (summary) => {
      if (summary.applied) {
        void queryClient.invalidateQueries({ queryKey: ['classes'] });
      }
    },
  });
};

export const useIngestUploaded = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (code: string) =>
      fetchJson<unknown>('/api/admin/uploads/ingest', {
        body: JSON.stringify({ code }),
        method: 'POST',
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'uploads', 'pending'] });
    },
  });
};

export const securityQueryOptions = () => ({
  queryFn: () => fetchJson<SecurityStatus>('/api/admin/settings/security'),
  queryKey: ['admin', 'security'] as const,
});

export const useSecurityStatus = () => useQuery(securityQueryOptions());

/** Upload standards workbooks; they MERGE into the store rather than replacing it. */
export const useUploadStandards = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (files: File[]) => {
      const body = new FormData();
      for (const file of files) {
        body.append('files', file, file.name);
      }
      return fetchJson<StandardsUploadResponse>('/api/admin/standards/upload', {
        body,
        method: 'POST',
      });
    },
    // Standard-linked badges across the class list may change.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['classes'] }),
  });
};

/**
 * Load an envelope export from another environment. The file is sent as it was downloaded;
 * the server rebuilds each class from its own standard and never overwrites an existing one.
 */
export const useImportEnvelopes = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (file: File) => {
      // Read first, parse second: only a parse failure means "not JSON".
      const text = await file.text();
      let bundle: unknown;
      try {
        bundle = JSON.parse(text);
      } catch {
        throw new Error(`${file.name} is not an envelope export (it is not JSON).`);
      }
      return fetchJson<EnvelopeImportResult>('/api/admin/envelopes/import', {
        body: JSON.stringify(bundle),
        method: 'POST',
      });
    },
    // New classes appear in the class list.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['classes'] }),
  });
};

export const analyticsQueryOptions = () => ({
  queryFn: () => fetchJson<AnalyticsReport>('/api/admin/analytics'),
  queryKey: ['admin', 'analytics'] as const,
});
