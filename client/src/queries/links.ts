import { fetchJson } from '../lib/api.ts';
import { useQuery } from '@tanstack/react-query';

export type Links = {
  /** The workforce management tool (CTHULHU); null until it has a production address. */
  wfmUrl: string | null;
};

export const useLinks = () =>
  useQuery({
    queryFn: () => fetchJson<Links>('/api/links'),
    queryKey: ['links'] as const,
    // Deployment configuration: it does not change while the page is open.
    staleTime: Infinity,
  });
