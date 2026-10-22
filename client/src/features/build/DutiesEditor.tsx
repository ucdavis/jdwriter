import { AddRow, Badge, Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import { AddRefusal } from './SectionEditor.tsx';
import { LineEditor } from './LineEditor.tsx';
import { useState } from 'react';
import type { CheckedChange } from '@/lib/contracts.ts';
import type { useBuildState } from './useBuildState.ts';

type BuildState = ReturnType<typeof useBuildState>;

/**
 * The key responsibilities: keep or drop whole functions and single duties, reword a duty in place,
 * add unit-specific duties, and set % time. Edits stay visible and count as customization — see
 * useBuildState.
 */
export const DutiesEditor = ({
  state,
  verdicts,
}: {
  state: BuildState;
  /** The current check's verdict on each added or edited line, by its text. */
  verdicts?: Map<string, CheckedChange>;
}) => {
  const balanced = state.totalPct === 100;
  // Which function's add box refused a duty, and why — shown under that box.
  const [refusal, setRefusal] = useState<{ at: number; text: string } | null>(null);
  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <Eyebrow>Key responsibilities</Eyebrow>
        <Badge tone={balanced ? 'green' : 'yellow'}>{state.totalPct}% of time</Badge>
      </div>
      <div className="space-y-4 pt-3">
        {state.resps.map((r, ri) => (
          <div
            className={`rounded-lg border border-base-300 p-3.5 ${
              r.functionKept ? '' : 'bg-base-200 opacity-60'
            }`}
            key={`${r.functionName}-${ri}`}
          >
            <div className="flex items-center gap-2">
              <input
                aria-label={`Include ${r.functionName}`}
                checked={r.functionKept}
                className="checkbox checkbox-sm"
                onChange={() => state.patchResp(ri, { functionKept: !r.functionKept })}
                type="checkbox"
              />
              <input
                aria-label={`Percent of time for ${r.functionName}`}
                className="input input-bordered input-sm w-20 text-right tnum"
                disabled={!r.functionKept}
                max={100}
                min={0}
                onChange={(e) => state.patchResp(ri, { pctTime: Number(e.target.value) })}
                type="number"
                value={r.pctTime}
              />
              <span className="text-base text-base-content/70">%</span>
              <input
                aria-label="Function name"
                className={`min-w-0 flex-1 rounded-md border border-transparent bg-transparent px-1.5 py-1 text-base font-semibold outline-none hover:border-base-300 focus:border-primary ${
                  r.functionKept ? '' : 'text-base-content/70 line-through'
                }`}
                disabled={!r.functionKept}
                onChange={(e) => state.patchResp(ri, { functionName: e.target.value })}
                value={r.functionName}
              />
            </div>
            {r.functionKept ? (
              <>
                <ul className="mt-2 space-y-1 pl-1">
                  {r.duties.map((d, di) => (
                    <LineEditor
                      item={d}
                      // By position, not text: the text changes as it's edited, and a key that
                      // changes with every keystroke would remount the box and lose the cursor.
                                key={di}
                      label={d.original ?? d.text}
                      onChange={(next) =>
                        state.patchResp(ri, { duties: r.duties.map((x, j) => (j === di ? next : x)) })
                      }
                      onRemove={() => state.patchResp(ri, { duties: r.duties.filter((_, j) => j !== di) })}
                      verdict={d.kept ? verdicts?.get(d.text.trim()) : undefined}
                    >
                      {d.matchedFrom && d.matchedFrom.length > 0 ? (
                        <span className="mt-0.5 block pl-1.5 text-sm text-base-content/70" data-testid="matched-from">
                          {d.matchedFrom.map((m) => (
                            <span className="block" key={m}>
                              ↳ From your description: “{m}”
                            </span>
                          ))}
                        </span>
                      ) : null}
                    </LineEditor>
                  ))}
                </ul>
                {!r.duties.some((d) => d.kept && d.text.trim() !== '') ? (
                  <p className="mt-2 text-base text-warning-ink" data-testid="function-needs-duty" role="status">
                    Keep at least one duty under this responsibility, or uncheck it to drop the whole
                    responsibility.
                  </p>
                ) : null}
                <AddRow
                  onAdd={() => {
                    const v = r.draft.trim();
                    if (!v) {
                      return;
                    }
                    const refused = state.guardAdd(v);
                    setRefusal(refused ? { at: ri, text: refused } : null);
                    if (refused) {
                      return;
                    }
                    state.patchResp(ri, {
                      draft: '',
                      duties: [...r.duties, { added: true, kept: true, text: v }],
                    });
                  }}
                  onChange={(v) => {
                    state.patchResp(ri, { draft: v });
                    if (refusal?.at === ri) {
                      setRefusal(null);
                    }
                  }}
                  placeholder="Add a duty to this function…"
                  value={r.draft}
                />
                {refusal?.at === ri ? <AddRefusal text={refusal.text} /> : null}
              </>
            ) : null}
          </div>
        ))}
      </div>
      {!balanced ? (
        <p className="mt-3 text-base text-warning-ink">
          ⚠ Percent time totals {state.totalPct}% —{' '}
          {state.totalPct < 100
            ? `${100 - state.totalPct}% is unallocated.`
            : `${state.totalPct - 100}% over-allocated.`}{' '}
          Use the allocation panel above to decide where it goes.
        </p>
      ) : null}
    </Card>
  );
};
