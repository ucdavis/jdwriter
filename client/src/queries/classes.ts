import { fetchJson } from '../lib/api.ts';
import { useMutation, useQuery } from '@tanstack/react-query';
import type {
  ArchiveResponse,
  ClassListResponse,
  ClassProfileResponse,
  ClassSummaryResponse,
  CoverageReport,
  FitRewriteResponse,
  JdDetailResponse,
  MisfitsResponse,
} from '../lib/contracts.ts';

export const classListQueryOptions = () => ({
  queryFn: () => fetchJson<ClassListResponse>('/api/classes'),
  queryKey: ['classes'] as const,
  // The in-use title list is ~1,200 entries and changes only on ingest.
  staleTime: 5 * 60_000,
});

export const useClassList = () => useQuery(classListQueryOptions());

// Kept apart from the class list on purpose: that one is on every author's hot path,
// this one carries coverage statistics only analysts need.
export const classSummaryQueryOptions = () => ({
  queryFn: () => fetchJson<ClassSummaryResponse>('/api/classes/summary'),
  queryKey: ['classes', 'summary'] as const,
});

export const useClassSummary = () => useQuery(classSummaryQueryOptions());

export const classProfileQueryOptions = (slug: string) => ({
  queryFn: () => fetchJson<ClassProfileResponse>(`/api/classes/${slug}`),
  queryKey: ['classes', slug] as const,
});

export const useClassProfile = (slug: string) =>
  useQuery(classProfileQueryOptions(slug));

export const coverageQueryOptions = (slug: string) => ({
  queryFn: () => fetchJson<CoverageReport | null>(`/api/classes/${slug}/coverage`),
  queryKey: ['classes', slug, 'coverage'] as const,
});

export const jdDetailQueryOptions = (slug: string, sourceFile: string) => ({
  queryFn: () =>
    fetchJson<JdDetailResponse>(
      // The path is a nested file path, so each segment is encoded separately —
      // encoding the whole string would escape the separators too.
      `/api/classes/${slug}/jds/${sourceFile.split('/').map(encodeURIComponent).join('/')}`
    ),
  queryKey: ['classes', slug, 'jds', sourceFile] as const,
});

export const misfitsQueryOptions = (threshold = 90) => ({
  queryFn: () => fetchJson<MisfitsResponse>(`/api/fit/misfits?threshold=${threshold}`),
  queryKey: ['fit', 'misfits', threshold] as const,
});

export const useMisfits = (threshold = 90) => useQuery(misfitsQueryOptions(threshold));

/** Every JD archived under a class, to start a new one from. */
export const archiveQueryOptions = (slug: string) => ({
  queryFn: () => fetchJson<ArchiveResponse>(`/api/classes/${encodeURIComponent(slug)}/archive`),
  queryKey: ['classes', slug, 'archive'] as const,
});

export const useArchive = (slug: string) => useQuery(archiveQueryOptions(slug));

/** Start a draft from an archived JD: its work placed in the class's functions, its specifics kept. */
export const useStartFromArchive = (slug: string) =>
  useMutation({
    mutationFn: (sourceFile: string) =>
      fetchJson<FitRewriteResponse>(`/api/classes/${encodeURIComponent(slug)}/archive/start`, {
        body: JSON.stringify({ sourceFile }),
        method: 'POST',
      }),
  });
