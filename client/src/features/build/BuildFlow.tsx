import { AddRow, Badge, Card, Eyebrow, Field, Note } from '@/shared/ui/primitives.tsx';
import { AllocationBar } from './AllocationBar.tsx';
import { FinalJd } from './FinalJd.tsx';
import { Link } from '@tanstack/react-router';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { SectionEditor } from './SectionEditor.tsx';
import { useAssembleJd, useEnvelopeCheck } from '@/queries/authoring.ts';
import { useBuildState, type EnvelopeSections } from './useBuildState.ts';
import { useState } from 'react';

type Step = 'check' | 'final' | 'tailor';

const STEPS: Array<[Step, string]> = [
  ['tailor', 'Tailor the standard'],
  ['check', 'Envelope check'],
  ['final', 'Final JD'],
];

const StepBar = ({ step }: { step: Step }) => {
  const current = STEPS.findIndex(([s]) => s === step);
  return (
    <div className="flex items-center gap-2">
      {STEPS.map(([key, label], i) => (
        <div className="flex items-center gap-2" key={key}>
          <span
            className={`inline-flex items-center gap-1.5 text-[12px] font-medium ${
              i <= current ? 'text-primary' : 'text-base-content/50'
            }`}
          >
            <span
              className={`flex h-5 w-5 items-center justify-center rounded-full text-[10px] ${
                i < current
                  ? 'bg-primary text-primary-content'
                  : i === current
                    ? 'border border-primary bg-primary/10 text-primary'
                    : 'bg-base-300 text-base-content/50'
              }`}
            >
              {i < current ? '✓' : i + 1}
            </span>
            {label}
          </span>
          {i < STEPS.length - 1 ? (
            <span className="text-base-content/40">—</span>
          ) : null}
        </div>
      ))}
    </div>
  );
};

/**
 * The guided build: tailor the class standard, check the additions against the envelope,
 * then assemble.
 *
 * The envelope check sits between tailoring and assembly on purpose. It polices ADDITIONS
 * only, and routes work that has outgrown the class to a real neighbouring class rather
 * than just warning about it — which is what keeps the corpus from drifting one JD at a
 * time.
 */
export const BuildFlow = ({
  envelope,
  slug,
  title,
}: {
  envelope: EnvelopeSections;
  slug: string;
  title: string;
}) => {
  const [step, setStep] = useState<Step>('tailor');
  const state = useBuildState(envelope);
  const check = useEnvelopeCheck();
  const assemble = useAssembleJd();
  // Set by the first assemble; sent back on later ones so they revise the same saved JD.
  const [savedId, setSavedId] = useState<number | null>(null);

  const error = check.error ?? assemble.error;
  const busy = check.isPending || assemble.isPending;
  const balanced = state.totalPct === 100;

  const runCheck = () =>
    check.mutate(state.buildRequest(slug), { onSuccess: () => setStep('check') });
  const runAssemble = () =>
    assemble.mutate(
      { ...state.buildRequest(slug), authoredJdId: savedId },
      {
        onSuccess: (result) => {
          setSavedId(result.authoredJdId);
          setStep('final');
        },
      }
    );

  return (
    <div className="space-y-5">
      <StepBar step={step} />
      {error ? <Note tone="red">{messageOf(error)}</Note> : null}

      {step === 'tailor' ? (
        <>
          <Card className="p-5">
            <Eyebrow>Position details</Eyebrow>
            <div className="mt-2 grid grid-cols-1 gap-3 md:grid-cols-2">
              <Field
                label="Working title"
                onChange={state.setWorkingTitle}
                placeholder={title}
                value={state.workingTitle}
              />
              <Field
                label="Department"
                onChange={state.setDepartment}
                placeholder="e.g. Plant Pathology"
                value={state.department}
              />
            </div>
            <div className="mt-3 flex items-center gap-3">
              <Badge tone={state.customPct <= 10 ? 'green' : 'yellow'}>
                Customization {state.customPct}%
              </Badge>
              <span className="text-[12px] text-base-content/65">
                {state.addedCount} added item{state.addedCount === 1 ? '' : 's'} · target
                ≤10% (removals don&apos;t count)
              </span>
            </div>
          </Card>

          {state.dupNote ? <Note tone="yellow">{state.dupNote}</Note> : null}

          <AllocationBar
            onRedistribute={state.setResps}
            resps={state.resps}
            totalPct={state.totalPct}
          />

          <Card className="p-5">
            <div className="flex items-center justify-between">
              <Eyebrow>Key responsibilities</Eyebrow>
              <Badge tone={balanced ? 'green' : 'yellow'}>
                {state.totalPct}% of time
              </Badge>
            </div>
            <p className="mb-3 mt-1 text-[12.5px] text-base-content/65">
              Uncheck a whole function that doesn&apos;t apply, drop individual duties, add
              unit-specific ones, and adjust % time to total 100.
            </p>
            <div className="space-y-4">
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
                      onChange={() =>
                        state.patchResp(ri, { functionKept: !r.functionKept })
                      }
                      type="checkbox"
                    />
                    <input
                      aria-label={`Percent of time for ${r.functionName}`}
                      className="input input-bordered input-sm w-16 text-right tnum"
                      disabled={!r.functionKept}
                      max={100}
                      min={0}
                      onChange={(e) =>
                        state.patchResp(ri, { pctTime: Number(e.target.value) })
                      }
                      type="number"
                      value={r.pctTime}
                    />
                    <span className="text-[13px] text-base-content/65">%</span>
                    <input
                      aria-label="Function name"
                      className={`flex-1 rounded-md border border-transparent bg-transparent px-1.5 py-1 text-[14px] font-semibold outline-none hover:border-base-300 focus:border-primary ${
                        r.functionKept ? '' : 'text-base-content/50 line-through'
                      }`}
                      disabled={!r.functionKept}
                      onChange={(e) =>
                        state.patchResp(ri, { functionName: e.target.value })
                      }
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
                                    duties: r.duties.map((x, j) =>
                                      j === di ? { ...x, kept: !x.kept } : x
                                    ),
                                  })
                                }
                                type="checkbox"
                              />
                              <span
                                className={`text-[13px] ${
                                  d.kept ? '' : 'text-base-content/50 line-through'
                                }`}
                              >
                                {d.added ? <Badge tone="green">+</Badge> : null} {d.text}
                              </span>
                            </label>
                            {d.added ? (
                              <button
                                aria-label="Remove added duty"
                                className="mt-0.5 text-[13px] text-base-content/50 hover:text-error"
                                onClick={() =>
                                  state.patchResp(ri, {
                                    duties: r.duties.filter((_, j) => j !== di),
                                  })
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
                          if (!v || !state.guardAdd(v)) {
                            return;
                          }
                          state.patchResp(ri, {
                            draft: '',
                            duties: [...r.duties, { added: true, kept: true, text: v }],
                          });
                        }}
                        onChange={(v) => state.patchResp(ri, { draft: v })}
                        placeholder="Add a duty to this function…"
                        value={r.draft}
                      />
                    </>
                  ) : null}
                </div>
              ))}
            </div>
            {!balanced ? (
              <p className="mt-3 text-[12px] text-warning">
                ⚠ Percent time totals {state.totalPct}% —{' '}
                {state.totalPct < 100
                  ? `${100 - state.totalPct}% is unallocated.`
                  : `${state.totalPct - 100}% over-allocated.`}{' '}
                Use the allocation panel above to decide where it goes.
              </p>
            ) : null}
          </Card>

          <SectionEditor
            guardAdd={state.guardAdd}
            items={state.certs}
            setItems={state.setCerts}
            title="Licenses & certifications"
          />
          <SectionEditor
            guardAdd={state.guardAdd}
            items={state.education}
            setItems={state.setEducation}
            title="Education"
          />
          <SectionEditor
            guardAdd={state.guardAdd}
            items={state.workExp}
            setItems={state.setWorkExp}
            title="Work experience"
          />
          <SectionEditor
            guardAdd={state.guardAdd}
            items={state.minKSA}
            setItems={state.setMinKSA}
            title="Minimum knowledge, skills & abilities"
          />
          <SectionEditor
            guardAdd={state.guardAdd}
            items={state.prefKSA}
            setItems={state.setPrefKSA}
            title="Preferred qualifications"
          />
          <SectionEditor
            guardAdd={state.guardAdd}
            items={state.workEnv}
            setItems={state.setWorkEnv}
            title="Work environment"
          />

          <Card className="p-5">
            <Eyebrow>Standard (not editable)</Eyebrow>
            <div className="mt-2 text-[12px] text-base-content/65">
              Conditions of Employment and Physical Requirements are standard UC
              boilerplate and are included automatically in the final JD.
            </div>
          </Card>

          <Card className="p-5">
            <Eyebrow>Anything else (optional)</Eyebrow>
            <textarea
              aria-label="Additional context"
              className="textarea textarea-bordered mt-2 w-full resize-none text-sm"
              onChange={(e) => state.setNotes(e.target.value)}
              placeholder="Special context for this position…"
              rows={2}
              value={state.notes}
            />
          </Card>

          <div className="flex items-center gap-3">
            <button
              className="btn btn-primary btn-sm"
              disabled={busy || !balanced}
              onClick={runCheck}
              type="button"
            >
              {busy ? 'Checking…' : 'Review & continue →'}
            </button>
            {!balanced ? (
              <span className="text-[12px] text-warning" data-testid="build-gate-reason">
                {state.totalPct < 100
                  ? `${100 - state.totalPct}% of time is unallocated — assign it before continuing.`
                  : `${state.totalPct - 100}% over-allocated — reduce it before continuing.`}
              </span>
            ) : null}
          </div>
        </>
      ) : null}

      {step === 'check' && check.data ? (
        <Card className="p-5">
          <div className="flex items-center justify-between">
            <Eyebrow>Envelope check</Eyebrow>
            <Badge
              tone={
                check.data.verdict === 'in_envelope'
                  ? 'green'
                  : check.data.verdict === 'borderline'
                    ? 'yellow'
                    : 'red'
              }
            >
              {check.data.verdict.replaceAll('_', ' ')}
            </Badge>
          </div>
          <p className="mt-3 text-[13.5px]">{check.data.rationale}</p>
          {check.data.matchedSignals.length > 0 ? (
            <ul className="mt-3 space-y-1">
              {check.data.matchedSignals.map((s) => (
                <li className="flex gap-2 text-[12.5px] text-base-content/65" key={s}>
                  <span className="text-warning">⚠</span>
                  <span>{s}</span>
                </li>
              ))}
            </ul>
          ) : null}
          {check.data.verdict === 'out_of_envelope' && check.data.suggestedClass ? (
            <div className="mt-4 rounded-lg border border-primary/20 bg-primary/10 p-3.5">
              <div className="text-[12.5px]">
                Your additions look more like{' '}
                <span className="font-semibold text-primary">
                  {check.data.suggestedClass}
                </span>
                .
              </div>
              {check.data.suggestedSlug ? (
                <Link
                  className="btn btn-primary btn-xs mt-2.5"
                  params={{ slug: check.data.suggestedSlug }}
                  to="/class/$slug"
                >
                  Switch to {check.data.suggestedClass} →
                </Link>
              ) : null}
            </div>
          ) : null}
          <div className="mt-4 flex items-center gap-3">
            <button
              className="btn btn-primary btn-sm"
              // Balance is re-checked here, not just on the previous step: the author can
              // navigate back, edit, and return, and the server refuses a non-zero
              // shortfall anyway.
              disabled={busy || !balanced}
              onClick={runAssemble}
              type="button"
            >
              {busy
                ? 'Assembling JD…'
                : check.data.verdict === 'out_of_envelope'
                  ? 'Proceed anyway →'
                  : 'Assemble the JD →'}
            </button>
            <button
              className="btn btn-ghost btn-sm"
              onClick={() => setStep('tailor')}
              type="button"
            >
              ← Back to tailoring
            </button>
            {!balanced ? (
              <span className="text-[12px] text-warning">
                {state.totalPct < 100
                  ? `${100 - state.totalPct}% of time is unallocated — a JD cannot be published until it totals 100%.`
                  : `${state.totalPct - 100}% over-allocated — a JD cannot be published until it totals 100%.`}
              </span>
            ) : null}
          </div>
        </Card>
      ) : null}

      {step === 'final' && assemble.data ? (
        <>
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => setStep('tailor')}
            type="button"
          >
            ← Back to tailoring
          </button>
          {/* Assembling again from here revises this same saved JD rather than adding one. */}
          <FinalJd result={assemble.data} />
        </>
      ) : null}
    </div>
  );
};
