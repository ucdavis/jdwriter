import { fetchJson } from '../lib/api.ts';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import type {
  AssembledJd,
  BetterFitResponse,
  BuildRequest,
  ClassifyRequest,
  ClassifyResponse,
  CoverageCheckRequest,
  CoverageReport,
  EnvelopeCheckResponse,
  EnvelopeSaveRequest,
  EnvelopeSaveResponse,
  ExtractResponse,
  FitRewriteRequest,
  FitRewriteResponse,
  FitSuggestRequest,
  FitSuggestResponse,
  IntakeRequest,
  IntakeResponse,
} from '../lib/contracts.ts';

const post = <TBody, TResult>(url: string) => (body: TBody) =>
  fetchJson<TResult>(url, { body: JSON.stringify(body), method: 'POST' });

/** Free-text role description to ranked classes. */
export const useIntakeMatch = () =>
  useMutation({
    mutationFn: post<IntakeRequest, IntakeResponse>('/api/intake/match'),
  });

/**
 * Upload a document and get its text back WITHOUT classifying it. Deliberate: it lets
 * the user see a mangled PDF and fix it before spending a classification call.
 */
export const useExtractDocument = () =>
  useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append('file', file);
      // No Content-Type header — the browser must set the multipart boundary itself.
      return fetchJson<ExtractResponse>('/api/classify/extract', {
        body,
        method: 'POST',
      });
    },
  });

export const useClassify = () =>
  useMutation({
    mutationFn: post<ClassifyRequest, ClassifyResponse>('/api/classify'),
  });

export const useEnvelopeCheck = () =>
  useMutation({
    mutationFn: post<BuildRequest, EnvelopeCheckResponse>('/api/build/check'),
  });

/** Is there a class that fits this build better than its own? One model call, any verdict. */
export const useBetterFit = () =>
  useMutation({
    mutationFn: post<BuildRequest, BetterFitResponse>('/api/build/better-fit'),
  });

export const useAssembleJd = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: post<BuildRequest, AssembledJd>('/api/build/assemble'),
    // Every assembly is saved, so the saved-JD lists are stale afterwards.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jds'] }),
  });
};

/** Save the build as a draft: no model, never in the corpus, reopened with Continue editing. */
export const useSaveDraft = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: post<BuildRequest, { authoredJdId: number }>('/api/build/draft'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jds'] }),
  });
};

export const useSaveEnvelope = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: post<EnvelopeSaveRequest, EnvelopeSaveResponse>('/api/envelope/save'),
    onSuccess: (_result, variables) => {
      // The saved envelope drives the authoring flow, so every view of this class is
      // now stale — including the list, where the source badge changes to edited.
      void queryClient.invalidateQueries({ queryKey: ['classes', variables.slug] });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
    },
  });
};

/** Checks the envelope currently on screen, not the saved one. */
export const useCheckCoverage = () =>
  useMutation({
    mutationFn: post<CoverageCheckRequest, CoverageReport>('/api/envelope/coverage'),
  });

export const useSuggestClass = () =>
  useMutation({
    mutationFn: post<FitSuggestRequest, FitSuggestResponse>('/api/fit/suggest'),
  });

/**
 * Rewrite a misfitting JD to fit a class, saved as a draft for review. One model call that
 * only maps the incumbent's work onto the class's functions; the text and % time are built
 * from the envelope and the incumbent's own JD.
 */
export const useRewriteToFit = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: post<FitRewriteRequest, FitRewriteResponse>('/api/fit/rewrite'),
    // The draft appears in the saved-JD lists.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['jds'] }),
  });
};
