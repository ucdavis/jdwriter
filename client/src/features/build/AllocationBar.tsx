import { Badge, Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import type { RespState } from './useBuildState.ts';

/**
 * The % of time allocation, always visible while the author keeps and drops duties.
 *
 * This is the number the author is managing, so it is a panel rather than a footnote.
 * The problem it solves: dropping a standard responsibility carries the remaining
 * percentages through verbatim, so the kept set can total less than 100.
 *
 * Two things this deliberately does NOT do. It does not publish a shortfall silently —
 * the gate below is hard. And it does not rescale automatically on the author's behalf,
 * because silently turning a kept 50% into 71% is an invisible edit to a legal document.
 * Redistribution is offered as an explicit action the author takes and can see the result
 * of.
 */
export const AllocationBar = ({
  onRedistribute,
  resps,
  totalPct,
}: {
  onRedistribute: (next: RespState[]) => void;
  resps: RespState[];
  totalPct: number;
}) => {
  const kept = resps.filter((r) => r.functionKept);
  const delta = 100 - totalPct;
  const balanced = delta === 0;
  const short = delta > 0;

  /**
   * Spread the difference across kept responsibilities as evenly as whole percentages
   * allow, giving any remainder to the first. Whole numbers only — HRTMS will not take a
   * fractional percent, and a "33.33%" that displays as 33% is a lie about the document.
   */
  const distributeEvenly = () => {
    if (kept.length === 0 || balanced) {
      return;
    }

    const base = Math.floor(delta / kept.length);
    let remainder = delta - base * kept.length;

    onRedistribute(
      resps.map((r) => {
        if (!r.functionKept) {
          return r;
        }
        const extra = remainder > 0 ? 1 : remainder < 0 ? -1 : 0;
        remainder -= extra;
        // Never drive a kept responsibility below zero; a negative share is meaningless.
        return { ...r, pctTime: Math.max(0, r.pctTime + base + extra) };
      })
    );
  };

  /** Give all of the freed time to one responsibility. */
  const giveAllTo = (index: number) =>
    onRedistribute(
      resps.map((r, i) =>
        i === index ? { ...r, pctTime: Math.max(0, r.pctTime + delta) } : r
      )
    );

  return (
    <Card
      className={`p-5 ${
        balanced ? '' : short ? 'border-warning/40' : 'border-error/40'
      }`}
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-baseline gap-3">
          <Eyebrow>Percent of time</Eyebrow>
          <span className="text-3xl font-bold tnum" data-testid="allocation-total">
            {totalPct}%
          </span>
          <Badge tone={balanced ? 'green' : short ? 'yellow' : 'red'}>
            {balanced
              ? 'balanced'
              : short
                ? `${delta}% unallocated`
                : `${Math.abs(delta)}% over-allocated`}
          </Badge>
        </div>

        {!balanced && kept.length > 0 ? (
          <div className="flex flex-wrap items-center gap-2">
            <button
              className="btn btn-outline btn-sm"
              onClick={distributeEvenly}
              type="button"
            >
              {short
                ? `Distribute ${delta}% evenly`
                : `Remove ${Math.abs(delta)}% evenly`}
            </button>
            {kept.length > 1 ? (
              <details className="dropdown">
                <summary className="btn btn-outline btn-sm">
                  {short ? 'Give it all to…' : 'Take it all from…'}
                </summary>
                <ul className="menu dropdown-content z-50 mt-1 w-72 rounded-lg border border-base-300 bg-base-100 p-2 shadow">
                  {resps.map((r, i) =>
                    r.functionKept ? (
                      <li key={`${r.functionName}-${i}`}>
                        <button onClick={() => giveAllTo(i)} type="button">
                          <span className="truncate">{r.functionName}</span>
                          <span className="ml-auto tnum text-base-content/50">
                            {r.pctTime}% → {Math.max(0, r.pctTime + delta)}%
                          </span>
                        </button>
                      </li>
                    ) : null
                  )}
                </ul>
              </details>
            ) : null}
          </div>
        ) : null}
      </div>

      {balanced ? null : (
        <p className="mt-2 text-base text-base-content/65">
          {short ? (
            <>
              Dropping a responsibility frees up its share of time. Decide where that{' '}
              {delta}% goes — the percentages are not rescaled for you, because a kept
              responsibility silently growing would be an invisible change to the
              published document.
            </>
          ) : (
            <>
              The kept responsibilities claim more than a full-time position. Reduce them
              by {Math.abs(delta)}% before continuing.
            </>
          )}
        </p>
      )}
    </Card>
  );
};
