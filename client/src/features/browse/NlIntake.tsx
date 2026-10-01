import { Badge, Note } from '@/shared/ui/primitives.tsx';
import { HttpError } from '@/lib/api.ts';
import { useIntakeMatch } from '@/queries/authoring.ts';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import type { ApiError } from '@/lib/contracts.ts';

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

/**
 * Semantic intake: the free-text request is matched against each class's envelope.
 * Two model calls happen server-side (a cheap catalog shortlist, then a detailed rank);
 * the client sees only the ranked result.
 */
export const NlIntake = () => {
  const [text, setText] = useState('');
  const navigate = useNavigate();
  const match = useIntakeMatch();

  const matches = match.data?.matches;

  return (
    <div>
      <textarea
        aria-label="Describe the role"
        className="textarea textarea-bordered w-full resize-none text-sm"
        onChange={(e) => setText(e.target.value)}
        placeholder="Describe the role in your own words — e.g. “I need someone to run lab samples, log results, and help set up experiments.”"
        rows={3}
        value={text}
      />
      <div className="mt-2 flex items-center gap-3">
        <button
          className="btn btn-primary btn-sm"
          disabled={match.isPending || !text.trim()}
          onClick={() => match.mutate({ request: text })}
          type="button"
        >
          {match.isPending ? 'Matching…' : 'Find matching class'}
        </button>
        <span className="text-[11.5px] text-base-content/50">
          Your description is matched against every ingested job class.
        </span>
      </div>

      {match.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(match.error)}</Note>
        </div>
      ) : null}

      {matches ? (
        <div className="mt-4">
          <div className="eyebrow mb-2">Suggested classes</div>
          {matches.length === 0 ? (
            <p className="text-[13px] text-base-content/65">
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
                    <span className="text-[13.5px] font-medium">{m.title}</span>
                    <Badge tone={toneFor(m.confidence)}>{m.confidence}% match</Badge>
                  </div>
                  <div className="mt-1 text-[12px] text-base-content/65">
                    {m.rationale}
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      ) : null}
    </div>
  );
};
