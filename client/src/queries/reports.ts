import { fetchJson } from '../lib/api.ts';
import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import type { ProblemReport, ReportKind } from '../lib/contracts.ts';

export type ReportFilter = 'all' | 'open' | 'resolved';

/** Send a bug report, request or question to JDWriter's admins. */
export const useSendReport = () =>
  useMutation({
    mutationFn: (body: { kind: ReportKind; message: string; page: string }) =>
      fetchJson<{ sent: boolean }>('/api/reports', { body: JSON.stringify(body), method: 'POST' }),
  });

/** The admins' inbox: reports newest first, with how many are open. */
export const reportsQueryOptions = (status: ReportFilter) =>
  queryOptions({
    queryFn: () =>
      fetchJson<{ open: number; reports: ProblemReport[] }>(
        status === 'all' ? '/api/admin/reports' : `/api/admin/reports?status=${status}`
      ),
    queryKey: ['admin', 'reports', status] as const,
  });

/** How many reports are open: the back end's badge. */
export const openReportsQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<{ open: number }>('/api/admin/reports/open-count'),
    queryKey: ['admin', 'reports', 'open-count'] as const,
  });

/** Resolve or reopen a report, with a note. */
export const useSetReportStatus = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note, resolved }: { id: number; note: string; resolved: boolean }) =>
      fetchJson(`/api/admin/reports/${id}/status`, { body: JSON.stringify({ note, resolved }), method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'reports'] }),
  });
};
