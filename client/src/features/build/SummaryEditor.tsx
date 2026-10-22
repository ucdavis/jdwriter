import { Badge, Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import type { CheckedChange } from '@/lib/contracts.ts';
import { LineVerdict } from './LineEditor.tsx';

/**
 * The job summary, as the JD will carry it: the class's own, which the author may rewrite. What is
 * in the box is what the JD says. A rewrite is checked with the other changes and its verdict shown
 * under it; it can always be put back to the class's wording.
 */
export const SummaryEditor = ({
  classSummary,
  edited,
  onChange,
  summary,
  verdict,
}: {
  classSummary: string;
  edited: boolean;
  onChange: (summary: string) => void;
  summary: string;
  /** How the envelope check judged the rewrite, when it was checked and hasn't changed since. */
  verdict?: CheckedChange;
}) => (
  <Card className="p-5">
    <Eyebrow>Job summary</Eyebrow>
    <textarea
      aria-label="Job summary"
      className="textarea textarea-bordered mt-2 w-full text-base leading-relaxed"
      maxLength={2000}
      onChange={(e) => onChange(e.target.value)}
      rows={4}
      value={summary}
    />
    <div className="mt-1 flex flex-wrap items-center gap-2 text-sm text-base-content/70">
      {edited ? (
        <>
          <span title={`The class's summary: ${classSummary}`}>
            <Badge tone="yellow">edited</Badge>
          </span>
          <button className="text-primary hover:underline" onClick={() => onChange(classSummary)} type="button">
            Use the class’s summary
          </button>
        </>
      ) : (
        <span>The class’s summary. Rewrite it to describe this position; your version is checked with your other changes.</span>
      )}
    </div>
    <LineVerdict verdict={verdict} />
    {summary.trim() === '' ? (
      <p className="mt-0.5 text-sm text-warning-ink">A job description needs a summary.</p>
    ) : null}
  </Card>
);
