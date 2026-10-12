import { fetchJson } from '../lib/api.ts';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  AnalyticsReport,
  ClassStandard,
  CorpusJdDetail,
  CorpusJdRow,
  StandardRow,
  StandardsUploadResponse,
  SecurityStatus,
  UploadedPendingResponse,
  QualificationRulesSummary,
  ReparseSummary,
  UploadOutcome,
  ApiKeyStatus,
  AdminsResponse,
  BootstrapCreateResponse,
  BootstrapResponse,
  BlankStandardClass,
  BlankStandardCleanup,
  JobsResponse,
  JobsScope,
  StandardDraft,
  StandardMatchReview,
  EnvelopeImportResult,
  RetirementResult,
  StandardsIngestResponse,
} from '../lib/contracts.ts';

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
    // A new class appears in the class list, the analyst index and the jobs directory.
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'jobs'] });
    },
  });
};

/** Every UC job code and what it is attached to: its class and its standard. */
export const jobsQueryOptions = (scope: JobsScope) => ({
  queryFn: () => fetchJson<JobsResponse>(`/api/admin/jobs?scope=${scope}`),
  queryKey: ['admin', 'jobs', scope] as const,
});

export const useJobs = (scope: JobsScope) => useQuery(jobsQueryOptions(scope));

/** Read a Word (or PDF, or text) export of a job standard into its sections, for review. */
export const useReadStandard = () =>
  useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append('file', file, file.name);
      return fetchJson<StandardDraft>('/api/admin/standards/read', { body, method: 'POST' });
    },
  });

/** Save a job's standard, entered or reviewed by hand. */
export const useSaveManualStandard = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { standard: StandardDraft; ucJobCode: string }) =>
      fetchJson<unknown>('/api/admin/standards/manual', { body: JSON.stringify(body), method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin', 'jobs'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'standard-matches'] });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
    },
  });
};

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
      void queryClient.invalidateQueries({ queryKey: ['admin', 'corpus'] });
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

/** Standards whose titles match no job code exactly, with suggested codes, and confirmed matches. */
export const standardMatchesQueryOptions = (includeNotActive: boolean) => ({
  queryFn: () =>
    fetchJson<StandardMatchReview>(`/api/admin/standards/matches?includeNotActive=${includeNotActive}`),
  queryKey: ['admin', 'standard-matches', includeNotActive] as const,
});

export const useStandardMatches = (includeNotActive: boolean) =>
  useQuery(standardMatchesQueryOptions(includeNotActive));

/** Everything a decided match changes: the review, coverage, bootstrap lists, standard links. */
const refreshAfterMatch = (queryClient: ReturnType<typeof useQueryClient>) => {
  void queryClient.invalidateQueries({ queryKey: ['admin', 'standard-matches'] });
  void queryClient.invalidateQueries({ queryKey: ['admin', 'jobs'] });
  void queryClient.invalidateQueries({ queryKey: ['classes'] });
};

/** Confirm (`match`) or reject one standard–code pairing. */
export const useDecideStandardMatch = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { match: boolean; standardTitle: string; ucJobCode: string }) =>
      fetchJson<unknown>('/api/admin/standards/matches', { body: JSON.stringify(body), method: 'POST' }),
    onSuccess: () => refreshAfterMatch(queryClient),
  });
};

export const useUndoStandardMatch = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (standardTitle: string) =>
      fetchJson<unknown>('/api/admin/standards/matches/undo', {
        body: JSON.stringify({ standardTitle }),
        method: 'POST',
      }),
    onSuccess: () => refreshAfterMatch(queryClient),
  });
};

/** Starter classes built from blank standards. Read on demand, so a mutation. */
export const useBlankStandardClasses = () =>
  useMutation({
    mutationFn: () =>
      fetchJson<{ classes: BlankStandardClass[] }>('/api/admin/standards/blank-classes'),
  });

/** Remove them; any with saved JDs are kept and reported. */
export const useRemoveBlankStandardClasses = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      fetchJson<BlankStandardCleanup>('/api/admin/standards/blank-classes/remove', { method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'jobs'] });
    },
  });
};

/** Re-read every imported Word document with the layout parser. */
export const useReparseDocuments = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => fetchJson<ReparseSummary>('/api/admin/uploads/documents/reparse', { method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin', 'uploads', 'pending'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'corpus'] });
    },
  });
};

/** Every job description in the corpus, whatever brought it in. */
export const corpusJdsQueryOptions = () => ({
  queryFn: () => fetchJson<{ jds: CorpusJdRow[] }>('/api/admin/corpus/jds'),
  queryKey: ['admin', 'corpus', 'jds'] as const,
});

export const useCorpusJds = () => useQuery(corpusJdsQueryOptions());

/** One job description, whole. */
export const corpusJdQueryOptions = (id: number) => ({
  queryFn: () => fetchJson<CorpusJdDetail>(`/api/admin/corpus/jds/${id}`),
  queryKey: ['admin', 'corpus', 'jds', id] as const,
});

/** Every job standard, with the job code and class each is attached to. */
export const standardsListQueryOptions = () => ({
  queryFn: () => fetchJson<{ standards: StandardRow[] }>('/api/standards'),
  queryKey: ['admin', 'standards', 'list'] as const,
});

export const useStandardsList = () => useQuery(standardsListQueryOptions());

/** One standard, whole, by its title. */
export const useStandardByTitle = (title: string | null) =>
  useQuery({
    enabled: title !== null,
    queryFn: () =>
      fetchJson<ClassStandard>(`/api/standards/by-title?title=${encodeURIComponent(title ?? '')}`),
    queryKey: ['admin', 'standards', 'by-title', title] as const,
  });

