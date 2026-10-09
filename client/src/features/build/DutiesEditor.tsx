import { AddRow, Badge, Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import { AddRefusal } from './SectionEditor.tsx';
import { useState } from 'react';
import type { useBuildState } from './useBuildState.ts';

type BuildState = ReturnType<typeof useBuildState>;

/**
 * The key responsibilities: keep or drop whole functions and single duties, add unit-specific
 * duties, and set % time. Affirmative and subtractive by design — see useBuildState.
 */
export const DutiesEditor = ({ state }: { state: BuildState }) => {
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
              <span className="text-base text-base-content/65">%</span>
              <input
                aria-label="Function name"
                className={`flex-1 rounded-md border border-transparent bg-transparent px-1.5 py-1 text-base font-semibold outline-none hover:border-base-300 focus:border-primary ${
                  r.functionKept ? '' : 'text-base-content/50 line-through'
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
                    <li className="flex items-start gap-2.5" key={`${d.text}-${di}`}>
                      <label className="flex flex-1 cursor-pointer items-start gap-2.5">
                        <input
                          checked={d.kept}
                          className="checkbox checkbox-sm mt-0.5"
                          onChange={() =>
                            state.patchResp(ri, {
                              duties: r.duties.map((x, j) => (j === di ? { ...x, kept: !x.kept } : x)),
                            })
                          }
                          type="checkbox"
                        />
                        <span className={`text-base ${d.kept ? '' : 'text-base-content/50 line-through'}`}>
                          {d.added ? <Badge tone="green">+</Badge> : null} {d.text}
                          {d.matchedFrom && d.matchedFrom.length > 0 ? (
                            <span className="mt-0.5 block text-sm text-base-content/55" data-testid="matched-from">
                              {d.matchedFrom.map((m) => (
                                <span className="block" key={m}>
                                  ↳ From your description: “{m}”
                                </span>
                              ))}
                            </span>
                          ) : null}
                        </span>
                      </label>
                      {d.added ? (
                        <button
                          aria-label="Remove added duty"
                          className="mt-0.5 text-base text-base-content/50 hover:text-error"
                          onClick={() =>
                            state.patchResp(ri, { duties: r.duties.filter((_, j) => j !== di) })
                          }
                          type="button"
                        >
                          ✕
                        </button>
                      ) : null}
                    </li>
                  ))}
                </ul>
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
        <p className="mt-3 text-base text-warning">
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
