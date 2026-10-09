import { Badge, Card, Eyebrow, Field, Note } from '@/shared/ui/primitives.tsx';
import { AllocationBar } from './AllocationBar.tsx';
import { CheckResult } from './CheckResult.tsx';
import { DutiesEditor } from './DutiesEditor.tsx';
import { FinalJd } from './FinalJd.tsx';
import { Link } from '@tanstack/react-router';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { SectionEditor } from './SectionEditor.tsx';
import {
  useAssembleJd,
  useBetterFit,
  useEnvelopeCheck,
  useSaveDraft,
} from '@/queries/authoring.ts';
import type { EnvelopeCheckResponse } from '@/lib/contracts.ts';
import { type DraftState, type EnvelopeSections, useBuildState } from './useBuildState.ts';
import { type ReactNode, useEffect, useRef, useState } from 'react';

type Step = 'duties' | 'final' | 'requirements';

const STEPS: Array<[Step, string]> = [
  ['duties', 'Duties'],
  ['requirements', 'Requirements'],
  ['final', 'Your JD'],
];

/**
 * Where the author is, as a progress bar with named steps. Earlier steps are links back; later
 * ones are reached only by passing the check, so they are not.
 */
const Progress = ({ onGo, step }: { onGo: (step: Step) => void; step: Step }) => {
  const current = STEPS.findIndex(([s]) => s === step);
  const done = step === 'final' ? STEPS.length : current;
  return (
    <div aria-label="Progress" data-testid="build-progress">
      <progress
        aria-label={`Step ${current + 1} of ${STEPS.length}`}
        className="progress progress-primary h-2 w-full"
        max={STEPS.length}
        value={done}
      />
      <ol className="mt-2 flex justify-between">
        {STEPS.map(([key, label], i) => (
          <li
            aria-current={i === current ? 'step' : undefined}
            className={`flex items-center gap-1.5 text-base font-semibold ${
              i <= current ? 'text-primary' : 'text-base-content/50'
            }`}
            key={key}
          >
            <span
              className={`flex h-6 w-6 items-center justify-center rounded-full text-sm ${
                i < done
                  ? 'bg-primary text-primary-content'
                  : i === current
                    ? 'border border-primary bg-primary/10'
                    : 'bg-base-300'
              }`}
            >
              {i < done ? '✓' : i + 1}
            </span>
            {i < current ? (
              <button
                className="font-semibold underline-offset-4 hover:underline"
                onClick={() => onGo(key)}
                title={`Back to ${label.toLowerCase()}`}
                type="button"
              >
                {label}
              </button>
            ) : (
              label
            )}
          </li>
        ))}
      </ol>
    </div>
  );
};

/**
 * The guided build, in three steps a new author can follow without scrolling past the envelope:
 *
 * 1. Duties — tailor the key responsibilities to 100% time, then check them against the class.
 * 2. Requirements — the check's result, then qualifications, work environment and the rest. The
 *    final check runs when they assemble; if nothing it reads changed since the duties check, it
 *    is not asked twice.
 * 3. Your JD — it passed, what to do next, downloads, and a way back to make changes.
 *
 * The envelope check polices ADDITIONS only, and routes work that has outgrown the class to a real
 * neighbouring class rather than just warning about it — which is what keeps the corpus from
 * drifting one JD at a time.
 */
export const BuildFlow = ({
  draft,
  envelope,
  overview,
  slug,
  title,
}: {
  /** A saved JD to continue: its id and the build screen as it was left. */
  draft?: { id: number; state: DraftState | null } | null;
  envelope: EnvelopeSections;
  /** The class's envelope at a glance, shown above the duties step only. */
  overview?: ReactNode;
  slug: string;
  title: string;
}) => {
  const [step, setStep] = useState<Step>('duties');
  const state = useBuildState(envelope, draft?.state);
  const saveDraft = useSaveDraft();
  const check = useEnvelopeCheck();
  const betterFit = useBetterFit();
  const assemble = useAssembleJd();
  // Set by the first assemble; sent back on later ones so they revise the same saved JD.
  const [savedId, setSavedId] = useState<number | null>(draft?.id ?? null);
  const [dutiesCheck, setDutiesCheck] = useState<{ key: string; result: EnvelopeCheckResponse } | null>(null);
  const [finalCheck, setFinalCheck] = useState<{ key: string; result: EnvelopeCheckResponse } | null>(null);
  const firstRender = useRef(true);

  // Each step reads as a new page: start it at the very top, title included. Not on first load,
  // which the browser already shows from the top.
  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    document.documentElement.scrollTop = 0;
    document.body.scrollTop = 0;
  }, [step]);

  const error = check.error ?? assemble.error;
  const busy = check.isPending || assemble.isPending;
  const balanced = state.totalPct === 100;
  const hasDepartment = state.department.trim().length > 0;
  const ready = balanced && hasDepartment;

  const go = (next: Step) => {
    // A step change answers afresh; an earlier search described a different build.
    betterFit.reset();
    setFinalCheck(null);
    setStep(next);
  };

  const checkDuties = () =>
    check.mutate(state.buildRequest(slug), {
      onSuccess: (result) => {
        setDutiesCheck({ key: state.checkKey, result });
        go('requirements');
      },
    });

  const runAssemble = () =>
    assemble.mutate(
      { ...state.buildRequest(slug), authoredJdId: savedId, draftState: state.snapshot() },
      {
        onSuccess: (result) => {
          setSavedId(result.authoredJdId);
          go('final');
        },
      }
    );

  // The final check: skipped when nothing it reads changed since the duties check, assembled
  // straight away when it fits, and shown for a decision when it doesn't. Asked again on an
  // unchanged build, it is the author's decision to go ahead.
  const accepted = finalCheck !== null && finalCheck.key === state.checkKey;
  const checkAndAssemble = () => {
    if (accepted || (dutiesCheck && dutiesCheck.key === state.checkKey)) {
      runAssemble();
      return;
    }
    const key = state.checkKey;
    check.mutate(state.buildRequest(slug), {
      onSuccess: (result) => {
        if (result.verdict === 'in_envelope') {
          runAssemble();
        } else {
          setFinalCheck({ key, result });
        }
      },
    });
  };

  // Why the author can't continue yet, most fundamental first.
  const gateReason = !hasDepartment
    ? 'Enter the department before continuing.'
    : balanced
      ? null
      : state.totalPct < 100
        ? `${100 - state.totalPct}% of time is unallocated — assign it before continuing.`
        : `${state.totalPct - 100}% over-allocated — reduce it before continuing.`;

  const draftButton = (
    <>
      <button
        className="btn btn-ghost btn-sm"
        disabled={saveDraft.isPending}
        onClick={() =>
          saveDraft.mutate(
            { ...state.buildRequest(slug), authoredJdId: savedId, draftState: state.snapshot() },
            { onSuccess: (r) => setSavedId(r.authoredJdId) }
          )
        }
        type="button"
      >
        {saveDraft.isPending ? 'Saving…' : 'Save draft'}
      </button>
      {saveDraft.isSuccess && !saveDraft.isPending ? (
        <span className="text-base text-success" data-testid="draft-saved">
          Draft saved ·{' '}
          <Link className="underline" to="/jds">
            My JDs
          </Link>
        </span>
      ) : null}
      {saveDraft.error ? <span className="text-base text-error">{messageOf(saveDraft.error)}</span> : null}
    </>
  );

  return (
    <div className="space-y-5">
      {step === 'duties' ? overview : null}
      <Progress onGo={go} step={step} />
      {error ? <Note tone="red">{messageOf(error)}</Note> : null}

      {step === 'duties' ? (
        <>
          <div>
          <Eyebrow>Build your job description</Eyebrow>
          <p className="mt-1 text-base leading-relaxed" data-testid="duties-intro">
            Here is where you build your custom job description (JD) based on the standard
            envelope. Below are the standard duties for the job. Select which items you&apos;d
            like to remove, or add the items you want in each section. Then make sure the total
            balances to 100% time. When you&apos;re done, we&apos;ll check it all against the
            standard and let you know if we need to make any changes.
          </p>
          </div>

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
                required
                value={state.department}
              />
            </div>
          </Card>

          <AllocationBar onRedistribute={state.setResps} resps={state.resps} totalPct={state.totalPct} />
          <DutiesEditor state={state} />

          <div className="flex flex-wrap items-center gap-3">
            <button
              className="btn btn-primary btn-sm"
              disabled={busy || !ready}
              onClick={checkDuties}
              type="button"
            >
              {busy ? 'Checking…' : 'Check my duties →'}
            </button>
            {draftButton}
            <CustomizationNote state={state} />
            {gateReason ? (
              <span className="text-base text-warning" data-testid="build-gate-reason">
                {gateReason}
              </span>
            ) : null}
          </div>
        </>
      ) : null}

      {step === 'requirements' ? (
        <>
          {dutiesCheck ? (
            <CheckResult
              betterFit={betterFit}
              check={dutiesCheck.result}
              currentTitle={title}
              heading={dutiesCheck.result.verdict === 'out_of_envelope' ? 'Check completed — take a look' : 'Check completed!'}
              onLookForBetterFit={() => betterFit.mutate(state.buildRequest(slug))}
            />
          ) : null}

          <div>
          <Eyebrow>Finish the requirements</Eyebrow>
          <p className="mt-1 text-base leading-relaxed" data-testid="requirements-intro">
            Now, let&apos;s finish the requirements for the job. Uncheck what you&apos;d like to
            remove, or add your own in the sections below. Once you&apos;re done, we&apos;ll do a
            final check and let you know if we need to change anything.
          </p>
          </div>

          <SectionEditor guardAdd={state.guardAdd} items={state.certs} setItems={state.setCerts} title="Licenses & certifications" />
          <SectionEditor guardAdd={state.guardAdd} items={state.education} setItems={state.setEducation} title="Education" />
          <SectionEditor guardAdd={state.guardAdd} items={state.workExp} setItems={state.setWorkExp} title="Work experience" />
          <SectionEditor
            guardAdd={state.guardAdd}
            items={state.minKSA}
            setItems={state.setMinKSA}
            title="Minimum knowledge, skills & abilities"
          />
          <SectionEditor guardAdd={state.guardAdd} items={state.prefKSA} setItems={state.setPrefKSA} title="Preferred qualifications" />
          <SectionEditor guardAdd={state.guardAdd} items={state.workEnv} setItems={state.setWorkEnv} title="Work environment" />

          <Card className="p-5">
            <Eyebrow>Included automatically</Eyebrow>
            <p className="mt-2 text-base text-base-content/65">
              Conditions of Employment and Physical Requirements are standard UC language, added
              to the final JD for you.
            </p>
          </Card>

          <Card className="p-5">
            <Eyebrow>Anything else (optional)</Eyebrow>
            <textarea
              aria-label="Additional context"
              className="textarea textarea-bordered mt-2 w-full resize-none text-base"
              onChange={(e) => state.setNotes(e.target.value)}
              placeholder="Special context for this position…"
              rows={2}
              value={state.notes}
            />
          </Card>

          {finalCheck ? (
            <CheckResult
              betterFit={betterFit}
              check={finalCheck.result}
              currentTitle={title}
              heading="Final check — a few things to look at"
              onLookForBetterFit={() => betterFit.mutate(state.buildRequest(slug))}
            />
          ) : null}

          <div className="flex flex-wrap items-center gap-3">
            <button
              className="btn btn-primary btn-sm"
              // Balance is re-checked here, not just on the previous step: the author can go back,
              // edit, and return, and the server refuses a non-zero shortfall anyway.
              disabled={busy || !ready}
              onClick={checkAndAssemble}
              type="button"
            >
              {busy ? 'Checking and assembling…' : accepted ? 'Assemble anyway →' : 'Assemble the JD →'}
            </button>
            {draftButton}
            {gateReason ? (
              <span className="text-base text-warning" data-testid="build-gate-reason">
                {gateReason}
              </span>
            ) : null}
          </div>
        </>
      ) : null}

      {step === 'final' && assemble.data ? (
        // Assembling again after "Make changes" revises this same saved JD rather than adding one.
        <FinalJd onEdit={() => go('duties')} result={assemble.data} />
      ) : null}
    </div>
  );
};

/** The customization meter: additions only, against a target of ≤10%. */
const CustomizationNote = ({ state }: { state: ReturnType<typeof useBuildState> }) =>
  state.addedCount > 0 ? (
    <span className="flex items-center gap-2 text-base text-base-content/65">
      <Badge tone={state.customPct <= 10 ? 'green' : 'yellow'}>Customization {state.customPct}%</Badge>
      {state.addedCount} added · target ≤10%
    </span>
  ) : null;
