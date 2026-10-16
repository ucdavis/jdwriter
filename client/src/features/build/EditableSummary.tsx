import { Badge } from '@/shared/ui/primitives.tsx';
import { useSetSummary } from '@/queries/jds.ts';
import { useState } from 'react';

/** As the server bounds it. */
const MAX_LENGTH = 2000;

/**
 * The JD's summary, which the author may rewrite once it is assembled. The model writes it from
 * the kept duties; the author knows the position better. It is prose about the position, not a
 * duty, so it isn't put through the envelope check — but it is marked as the author's, and
 * assembling again writes a fresh one, since the duties may have changed under it.
 */
export const EditableSummary = ({
  authoredJdId,
  edited,
  summary,
}: {
  /** The saved record; without one there is nothing to save the edit to. */
  authoredJdId: number | null;
  edited: boolean;
  summary: string;
}) => {
  const [shown, setShown] = useState({ edited, summary });
  const [draft, setDraft] = useState<string | null>(null);
  const save = useSetSummary();
  const trimmed = draft?.trim() ?? '';

  if (draft === null || authoredJdId === null) {
    return (
      <>
        <p className="text-base leading-relaxed" data-testid="job-summary">
          {shown.summary}
        </p>
        <div className="mt-2 flex items-center gap-2 print:hidden">
          {shown.edited ? (
            <span data-testid="summary-edited">
              <Badge tone="muted">Written by the author</Badge>
            </span>
          ) : null}
          {authoredJdId === null ? null : (
            <button
              className="btn btn-ghost btn-xs"
              onClick={() => {
                save.reset();
                setDraft(shown.summary);
              }}
              type="button"
            >
              Edit summary
            </button>
          )}
        </div>
      </>
    );
  }

  return (
    <div className="print:hidden">
      <textarea
        aria-label="Job summary"
        className="textarea textarea-bordered w-full text-base leading-relaxed"
        maxLength={MAX_LENGTH}
        onChange={(e) => setDraft(e.target.value)}
        rows={5}
        value={draft}
      />
      <div className="mt-1 text-sm text-base-content/60">
        Describe the position in two or three sentences. It isn’t checked against the class, so keep
        it to the duties above. Assembling the JD again writes a new summary.
      </div>
      {save.error ? (
        <p className="mt-1 text-sm text-error" role="alert">
          {save.error.message}
        </p>
      ) : null}
      <div className="mt-2 flex gap-2">
        <button
          className="btn btn-primary btn-sm"
          disabled={trimmed === '' || save.isPending}
          onClick={() =>
            save.mutate(
              { id: authoredJdId, summary: trimmed },
              {
                onSuccess: (r) => {
                  setShown({ edited: r.summaryEdited, summary: r.jobSummary });
                  setDraft(null);
                },
              }
            )
          }
          type="button"
        >
          {save.isPending ? 'Saving…' : 'Save summary'}
        </button>
        <button className="btn btn-ghost btn-sm" onClick={() => setDraft(null)} type="button">
          Cancel
        </button>
      </div>
    </div>
  );
};
