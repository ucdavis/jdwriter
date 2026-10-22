import { SiteNote } from '@/shared/ui/SiteNote.tsx';
import { Badge, Note } from '@/shared/ui/primitives.tsx';
import { HttpError } from '@/lib/api.ts';
import { useIntakeMatch } from '@/queries/authoring.ts';
import { clearSessionState, setSessionState, useSessionState } from '@/lib/sessionState.ts';
import { useNavigate } from '@tanstack/react-router';
import type { ApiError, IntakeResponse } from '@/lib/contracts.ts';

/** 4xx messages are written for the user, so they are shown verbatim. */
export const messageOf = (error: unknown): string => {
  if (error instanceof HttpError) {
    const body = error.body as ApiError | string | undefined;
    if (body && typeof body === 'object' && 'message' in body) {
      return body.message;
    }
    return error.status >= 500
      ? 'Something went wrong on our side. Try again.'
      : error.message;
  }
  return error instanceof Error ? error.message : 'Something went wrong.';
};

const toneFor = (confidence: number) =>
  confidence >= 70 ? 'green' : confidence >= 40 ? 'yellow' : 'muted';

const KEY = 'home.intake.';

/**
 * Semantic intake: the free-text request is matched against each class's envelope.
 * Two model calls happen server-side (a cheap catalog shortlist, then a detailed rank);
 * the client sees only the ranked result.
 *
 * The request and its suggestions are kept across pages (useSessionState): opening a suggested
 * class and coming back finds them as they were. A result arriving after the page was left still
 * lands, because it is stored from the request's own promise rather than this component.
 */
export const NlIntake = () => {
  const [text, setText] = useSessionState(`${KEY}text`, '');
  const [result] = useSessionState<IntakeResponse | null>(`${KEY}result`, null);
  const navigate = useNavigate();
  const match = useIntakeMatch();

  const matches = result?.matches;
  const find = () => {
    void match
      .mutateAsync({ request: text })
      .then((r) => setSessionState(`${KEY}result`, r))
      .catch(() => {
        // Shown from the mutation's own error while the page is open.
      });
  };

  return (
    <div>
      <textarea
        aria-label="Describe the role"
        className="textarea textarea-bordered w-full resize-none text-base"
        onChange={(e) => setText(e.target.value)}
        placeholder="Describe the role in your own words — e.g. “I need someone to run lab samples, log results, and help set up experiments.”"
        rows={3}
        value={text}
      />
      <div className="mt-2 flex items-center gap-3">
        <button
          className="btn btn-primary btn-sm"
          disabled={match.isPending || !text.trim()}
          onClick={find}
          type="button"
        >
          {match.isPending ? 'Matching…' : 'Find matching class'}
        </button>
        {text || result ? (
          <button
            className="btn btn-ghost btn-sm"
            disabled={match.isPending}
            onClick={() => {
              clearSessionState(KEY);
              match.reset();
            }}
            type="button"
          >
            Clear
          </button>
        ) : null}
        <span className="text-sm text-base-content/70">
          Your description is matched against every ingested job class.
        </span>
      </div>

      {/* Announced as matching starts and when its suggestions land (WCAG 4.1.3). */}
      <p className="sr-only" role="status">
        {match.isPending
          ? 'Matching…'
          : matches
            ? matches.length === 0
              ? 'No close match.'
              : `${matches.length} suggested class${matches.length === 1 ? '' : 'es'}.`
            : ''}
      </p>

      {match.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(match.error)}</Note>
        </div>
      ) : null}

      {matches ? (
        <div className="mt-4">
          <div className="eyebrow mb-2">Suggested classes</div>
          {matches.length === 0 ? (
            <p className="text-base text-base-content/70">
              No close match. Try the search on the right.
            </p>
          ) : (
            <div className="space-y-2">
              {matches.map((m) => (
                <button
                  className="w-full rounded-lg border border-base-300 px-3 py-2.5 text-left hover:bg-base-200"
                  key={m.slug}
                  onClick={() =>
                    navigate({ params: { slug: m.slug }, to: '/class/$slug' })
                  }
                  type="button"
                >
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-base font-semibold">{m.title}</span>
                    <Badge tone={toneFor(m.confidence)}>{m.confidence}% match</Badge>
                  </div>
                  <div className="mt-1 text-base text-base-content/70">
                    {m.rationale}
                  </div>
                  <SiteNote linked={false} site={m.site} siteTwins={m.siteTwins} />
                </button>
              ))}
            </div>
          )}
        </div>
      ) : null}
    </div>
  );
};
