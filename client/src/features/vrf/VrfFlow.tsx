import { messageOf } from '@/features/browse/NlIntake.tsx';
import { clearSessionState, getSessionState, setSessionState, useSessionState } from '@/lib/sessionState.ts';
import { classListQueryOptions, classProfileQueryOptions } from '@/queries/classes.ts';
import { useExtractDocument } from '@/queries/authoring.ts';
import { savedJdQueryOptions, useSavedJds } from '@/queries/jds.ts';
import { contextOf, useDownloadVrf } from '@/queries/vrf.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';
import { Card, Eyebrow, Note, SlowNote } from '@/shared/ui/primitives.tsx';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { MagicWand } from './MagicWand.tsx';
import {
  ACTIONS,
  BUDGET_SOURCES,
  emptyVrf,
  FILL_METHODS,
  fromJd,
  isContract,
  missingFields,
  NARRATIVES,
  REASONS,
  replacesSomeone,
  STEPS,
  type StepKey,
  type VrfForm,
} from './vrfModel.ts';

const KEY = 'vrf.';

/** The attachments HR asks for, as the campus form lists them. */
const ATTACHMENTS: Array<[string, string]> = [
  ['Every request', 'the job description and an org chart'],
  ['Internal recruitments', 'the Internal Recruitment Form'],
  ['Waiver of recruitment (direct hire, career and contract appointments)', 'the Waiver of Recruitment Form'],
  ['Rehire retiree requests', 'the Retire Employee Approvals Form'],
  ['Extensions or exceptions to policy', 'the previous or current UBEN 138'],
  ['Contract and limited appointment extensions', 'the extension request form and the previous or current offer letter'],
];

/**
 * The Vacancy Request Form, walked through a step at a time: what HR needs for the campus VRF, in
 * plain questions, showing only what applies to this request. Reached from a finished JD (which
 * fills in the position) or from the tab (pick one of your JDs, drop in a JD document, or type it).
 * Download-only: the answers stay in this browser until it reloads, and the Word file is built on
 * the server and sent straight back.
 */
export const VrfFlow = ({ jdId }: { jdId?: number }) => {
  const user = useUser();
  const initial = { ...emptyVrf(), hiringManagerEmail: user.email, hiringManagerName: user.name };
  const [form] = useSessionState<VrfForm>(`${KEY}form`, initial);
  const [step, setStep] = useSessionState<StepKey>(`${KEY}step`, 'position');
  const [filledFrom, setFilledFrom] = useSessionState<string | null>(`${KEY}from`, null);
  const download = useDownloadVrf();

  // Always from the latest value: a draft that lands after a wait must not undo edits made meanwhile.
  const update = (patch: Partial<VrfForm>) => setSessionState(`${KEY}form`, { ...getSessionState(`${KEY}form`, initial), ...patch });

  // Arriving from a finished JD: fill in what it says about the position, once.
  const fromLink = useQuery({ ...savedJdQueryOptions(jdId ?? 0), enabled: jdId != null });
  useEffect(() => {
    if (fromLink.data && getSessionState<string | null>(`${KEY}from`, null) !== `jd:${fromLink.data.authoredJdId}`) {
      setSessionState(`${KEY}form`, { ...getSessionState(`${KEY}form`, initial), ...fromJd(fromLink.data) });
      setSessionState(`${KEY}from`, `jd:${fromLink.data.authoredJdId}`);
    }
    // `initial` is rebuilt each render; only the JD arriving matters here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fromLink.data]);

  const missing = missingFields(form);
  const index = STEPS.findIndex((s) => s.key === step);
  const missingOn = (key: StepKey) => missing.filter((m) => m.step === key).length;

  return (
    <div className="space-y-5">
      <nav aria-label="Steps" className="flex flex-wrap gap-2">
        {STEPS.map((s, i) => (
          <button
            aria-current={s.key === step ? 'step' : undefined}
            className={`btn btn-sm ${s.key === step ? 'btn-primary' : 'btn-ghost'}`}
            key={s.key}
            onClick={() => setStep(s.key)}
            type="button"
          >
            {i + 1}. {s.label}
            {s.key !== 'review' && missingOn(s.key) > 0 && s.key !== step ? (
              <span className="text-sm font-normal opacity-80">({missingOn(s.key)} to do)</span>
            ) : null}
          </button>
        ))}
      </nav>

      {step === 'position' ? (
        <>
          <StartFrom filledFrom={filledFrom} onFill={(patch, from) => {
            update(patch);
            setFilledFrom(from);
          }} />
          <Card className="p-5">
            <Eyebrow>The position</Eyebrow>
            <Grid>
              <Text form={form} label="Department name" name="department" required update={update} />
              <Text form={form} label="Division/School/College" name="division" required update={update} />
              <Text form={form} hint="As HR's list has it, e.g. LAB AST 1" label="Payroll title" name="payrollTitle" required update={update} />
              <Text form={form} label="Job code" name="jobCode" required update={update} />
              <Text form={form} label="Working title" name="workingTitle" required update={update} />
              <Text form={form} label="Bargaining unit" name="bargainingUnit" required update={update} />
              <Text form={form} hint="Optional — HR can fill this in" label="Salary grade/steps" name="salaryGrade" update={update} />
            </Grid>
          </Card>
        </>
      ) : null}

      {step === 'request' ? (
        <Card className="p-5">
          <Eyebrow>The request</Eyebrow>
          <Grid>
            <Choice form={form} label="Action requested" name="actionRequested" options={ACTIONS} update={update} />
            <Choice form={form} label="How will you fill the role?" name="fillMethod" options={FILL_METHODS} update={update} />
            <Text form={form} hint="Leave blank if there isn't one" label="Position number" name="positionNumber" update={update} />
            {replacesSomeone(form) ? (
              <Text form={form} hint="mm/dd/yyyy" label="Date vacated or expiration date" name="dateVacated" required update={update} />
            ) : null}
            <Text form={form} hint="If there is one" label="Job opening ID" name="jobOpeningId" update={update} />
            <Text form={form} hint="How many of these positions you're requesting" label="Number of vacancies" name="vacancies" required update={update} />
            <Text form={form} hint="Up to 1.0 (full time)" label="FTE" name="fte" required update={update} />
            <Text form={form} hint="If it supervises anyone" label="Direct reports" name="directReports" update={update} />
            <Text form={form} hint="If it supervises anyone" label="Indirect reports" name="indirectReports" update={update} />
          </Grid>
        </Card>
      ) : null}

      {step === 'budget' ? (
        <Card className="p-5">
          <Eyebrow>Budget</Eyebrow>
          <Grid>
            <Text form={form} hint="The primary funding for this request" label="Budget source" list="vrf-budget-sources" name="budgetSource" required update={update} />
            <Text form={form} hint="If more than one — e.g. student fees, a grant" label="Sources of funding" name="fundingSources" update={update} />
            {isContract(form) ? (
              <Choice form={form} label="Is the funding for this contract secured?" name="fundingSecured" optional options={['Yes', 'No']} update={update} />
            ) : null}
          </Grid>
          <datalist id="vrf-budget-sources">
            {BUDGET_SOURCES.map((s) => (
              <option key={s} value={s} />
            ))}
          </datalist>
          <div className="mt-5">
            <Eyebrow>Salary range</Eyebrow>
            <p className="mt-1 text-base text-base-content/70">Optional — HR can fill these in.</p>
            <Grid>
              <Text form={form} label="Minimum (annual)" name="salaryMin" update={update} />
              <Text form={form} label="Midpoint (annual)" name="salaryMid" update={update} />
              <Text form={form} label="Maximum (annual)" name="salaryMax" update={update} />
            </Grid>
          </div>
        </Card>
      ) : null}

      {step === 'why' ? (
        <Card className="p-5">
          <Eyebrow>Why it’s needed</Eyebrow>
          <fieldset className="mt-3">
            <legend className="text-base font-semibold">
              Reason(s) for this request<Req />
            </legend>
            <p className="text-sm text-base-content/70">Tick all that apply.</p>
            <div className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-2">
              {REASONS.map((r) => (
                <label className="flex items-start gap-2 text-base" key={r}>
                  <input
                    checked={form.reasons.includes(r)}
                    className="checkbox checkbox-primary mt-0.5"
                    onChange={() =>
                      update({ reasons: form.reasons.includes(r) ? form.reasons.filter((x) => x !== r) : [...form.reasons, r] })
                    }
                    type="checkbox"
                  />
                  {r}
                </label>
              ))}
            </div>
          </fieldset>
          {form.reasons.includes('Other') ? (
            <div className="mt-3">
              <Text form={form} label="The other reason, explained" name="otherReason" required update={update} />
            </div>
          ) : null}
          {NARRATIVES.map((n) => (
            <div className="mt-5" key={n.key}>
              <label className="block">
                <span className="text-base font-semibold">
                  {n.label}
                  <Req />
                </span>
                <span className="mt-0.5 block text-sm text-base-content/70">{n.question}</span>
                <textarea
                  className="textarea textarea-bordered mt-1 w-full text-base leading-relaxed"
                  onChange={(e) => update({ [n.key]: e.target.value })}
                  rows={5}
                  value={form[n.key]}
                />
              </label>
              <MagicWand
                context={contextOf(form)}
                field={n.key}
                label={n.label}
                onChange={(text) => update({ [n.key]: text })}
                value={form[n.key]}
              />
            </div>
          ))}
        </Card>
      ) : null}

      {step === 'people' ? (
        <Card className="p-5">
          <Eyebrow>People</Eyebrow>
          <Grid>
            <Text form={form} label="Hiring manager name" name="hiringManagerName" required update={update} />
            <Text form={form} label="Hiring manager email" name="hiringManagerEmail" required type="email" update={update} />
            <Text form={form} label="HR business partner name" name="hrbpName" required update={update} />
            <Text form={form} label="HR business partner email" name="hrbpEmail" required type="email" update={update} />
            <Text form={form} hint="e.g. Vice Chancellor, Vice Provost, Dean, or a delegate" label="Executive approver" name="executiveApprover" required update={update} />
            <Text form={form} label="Executive approver email" name="executiveApproverEmail" required type="email" update={update} />
          </Grid>
          <p className="mt-2 text-sm text-base-content/70">
            If a delegate approves, include their written delegation with your ticket.
          </p>
          <label className="mt-4 block">
            <span className="text-base font-semibold">Additional notes</span>
            <span className="mt-0.5 block text-sm text-base-content/70">Optional.</span>
            <textarea
              className="textarea textarea-bordered mt-1 w-full text-base"
              onChange={(e) => update({ notes: e.target.value })}
              rows={3}
              value={form.notes}
            />
          </label>
        </Card>
      ) : null}

      {step === 'review' ? (
        <Card className="p-5">
          <Eyebrow>Review & download</Eyebrow>
          {missing.length > 0 ? (
            <div className="mt-3" data-testid="vrf-missing">
              <Note tone="yellow">
                {missing.length} required answer{missing.length === 1 ? '' : 's'} still to fill in. You can download now —
                the file marks them — but HR will need them.
                <ul className="mt-2 list-disc pl-5">
                  {missing.map((m) => (
                    <li key={m.key}>
                      <button className="underline" onClick={() => setStep(m.step)} type="button">
                        {m.label}
                      </button>
                    </li>
                  ))}
                </ul>
              </Note>
            </div>
          ) : (
            <p className="mt-2 text-base text-success-ink" data-testid="vrf-complete">
              Everything the form requires is filled in.
            </p>
          )}

          <div className="mt-4">
            <div className="text-base font-semibold">What to attach to your AggieService ticket</div>
            <ul className="mt-1 list-disc space-y-1 pl-5 text-base" data-testid="vrf-attachments">
              <li>This Vacancy Request Form (the Word file below)</li>
              {ATTACHMENTS.map(([when, what]) => (
                <li key={when}>
                  <span className="font-semibold">{when}:</span> {what}
                </li>
              ))}
            </ul>
            <p className="mt-2 text-sm text-base-content/70">Submissions missing these are returned.</p>
          </div>

          <div className="mt-5 flex flex-wrap items-center gap-3">
            <button className="btn btn-primary" disabled={download.isPending} onClick={() => download.mutate(form)} type="button">
              {download.isPending ? 'Preparing…' : '⤓ Download the Vacancy Request Form'}
            </button>
            <a className="text-primary underline" href="https://aggieservice.ucdavis.edu/s/" rel="noreferrer" target="_blank">
              Open AggieService
            </a>
          </div>
          {download.error ? (
            <div className="mt-2">
              <Note tone="red">{messageOf(download.error)}</Note>
            </div>
          ) : null}
        </Card>
      ) : null}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex gap-2">
          {index > 0 ? (
            <button className="btn btn-ghost btn-sm" onClick={() => setStep(STEPS[index - 1].key)} type="button">
              ← Back
            </button>
          ) : null}
          {index < STEPS.length - 1 ? (
            <button className="btn btn-primary btn-sm" onClick={() => setStep(STEPS[index + 1].key)} type="button">
              Next: {STEPS[index + 1].label} →
            </button>
          ) : null}
        </div>
        <button
          className="btn btn-ghost btn-sm text-error"
          onClick={() => {
            if (window.confirm('Clear every answer and start over?')) {
              clearSessionState(KEY);
            }
          }}
          type="button"
        >
          Start over
        </button>
      </div>
    </div>
  );
};

/** Where the position details come from: one of the author's JDs, a dropped JD document, or typing. */
const StartFrom = ({ filledFrom, onFill }: { filledFrom: string | null; onFill: (patch: Partial<VrfForm>, from: string) => void }) => {
  const jds = useSavedJds('mine');
  const extract = useExtractDocument();
  const queryClient = useQueryClient();
  const [chosen, setChosen] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const ready = (jds.data?.jds ?? []).filter((j) => j.assembled);

  const fromJdId = async (id: number) => {
    setProblem(null);
    const jd = await queryClient.fetchQuery(savedJdQueryOptions(id));
    onFill(fromJd(jd), `jd:${id}`);
  };

  // A JD document gives its title and code; the class adds its bargaining unit and grade.
  const fromDocument = (file: File) => {
    setProblem(null);
    extract.mutate(file, {
      onSuccess: async (r) => {
        if (!r.proposed) {
          setProblem(`No UC job title or code was found in “${file.name}”. Fill in the position below.`);
          return;
        }

        const patch: Partial<VrfForm> = { jobCode: r.proposed.code, payrollTitle: r.proposed.title };
        const classes = await queryClient.fetchQuery(classListQueryOptions());
        const match = classes.classes.find((c) => c.ucJobCode === r.proposed!.code);
        if (match) {
          const profile = await queryClient.fetchQuery(classProfileQueryOptions(match.slug));
          patch.bargainingUnit = profile.bargainingUnit ?? profile.unionCode.consensus ?? '';
          patch.salaryGrade = profile.salaryGrade.consensus ?? '';
        }

        onFill(patch, `file:${file.name}`);
      },
    });
  };

  return (
    <Card className="p-5">
      <Eyebrow>Start from</Eyebrow>
      <div className="mt-2 grid grid-cols-1 gap-4 md:grid-cols-2">
        <div>
          <label className="block">
            <span className="text-base font-semibold">One of your job descriptions</span>
            <select
              className="select select-bordered mt-1 w-full text-base"
              onChange={(e) => setChosen(e.target.value)}
              value={chosen}
            >
              <option value="">{ready.length > 0 ? 'Choose a JD…' : 'You have no finished JDs yet'}</option>
              {ready.map((j) => (
                <option key={j.id} value={j.id}>
                  {j.workingTitle || j.title} · {j.department}
                </option>
              ))}
            </select>
          </label>
          <button className="btn btn-outline btn-sm mt-2" disabled={chosen === ''} onClick={() => void fromJdId(Number(chosen))} type="button">
            Fill in from this JD
          </button>
        </div>
        <div
          className={`rounded-lg border-2 border-dashed p-4 text-center text-base ${dragging ? 'border-primary bg-primary/5' : 'border-base-300'}`}
          onDragLeave={() => setDragging(false)}
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDrop={(e) => {
            e.preventDefault();
            setDragging(false);
            const file = e.dataTransfer.files[0];
            if (file) {
              fromDocument(file);
            }
          }}
        >
          <p>Drop a JD document here (Word or PDF)</p>
          <label className="btn btn-outline btn-sm mt-2">
            {extract.isPending ? 'Reading…' : 'Or choose a file'}
            <input
              accept=".docx,.pdf,.txt"
              aria-label="Fill in from a JD document"
              className="hidden"
              disabled={extract.isPending}
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) {
                  fromDocument(file);
                }
                e.target.value = '';
              }}
              type="file"
            />
          </label>
          <div className="mt-1">
            <SlowNote show={extract.isPending} />
          </div>
        </div>
      </div>
      <p className="mt-3 text-sm text-base-content/70">Or just fill in the position below.</p>
      <p className="sr-only" role="status">
        {filledFrom ? 'Position filled in. Check it below.' : ''}
      </p>
      {filledFrom ? (
        <p className="mt-2 text-base text-success-ink" data-testid="vrf-filled">
          Filled in from {filledFrom.startsWith('jd:') ? 'your job description' : `“${filledFrom.slice(5)}”`} — check it below.
        </p>
      ) : null}
      {problem ? <p className="mt-2 text-base text-warning-ink">{problem}</p> : null}
      {extract.error ? (
        <div className="mt-2">
          <Note tone="red">{messageOf(extract.error)}</Note>
        </div>
      ) : null}
    </Card>
  );
};

const Req = () => (
  <>
    <span aria-hidden className="text-error">
      {' '}*
    </span>
    <span className="sr-only"> (required)</span>
  </>
);

const Grid = ({ children }: { children: ReactNode }) => (
  <div className="mt-3 grid grid-cols-1 gap-4 md:grid-cols-2">{children}</div>
);

type TextKey = Exclude<keyof VrfForm, 'reasons'>;

const Text = ({
  form,
  hint,
  label,
  list,
  name,
  required = false,
  type = 'text',
  update,
}: {
  form: VrfForm;
  hint?: string;
  label: string;
  list?: string;
  name: TextKey;
  required?: boolean;
  type?: 'email' | 'text';
  update: (patch: Partial<VrfForm>) => void;
}) => (
  <label className="block">
    <span className="text-base font-semibold">
      {label}
      {required ? <Req /> : null}
    </span>
    {hint ? <span className="mt-0.5 block text-sm text-base-content/70">{hint}</span> : null}
    <input
      aria-required={required || undefined}
      className="input input-bordered mt-1 w-full text-base"
      list={list}
      onChange={(e) => update({ [name]: e.target.value })}
      type={type}
      value={form[name]}
    />
  </label>
);

const Choice = ({
  form,
  label,
  name,
  optional = false,
  options,
  update,
}: {
  form: VrfForm;
  label: string;
  name: TextKey;
  optional?: boolean;
  options: string[];
  update: (patch: Partial<VrfForm>) => void;
}) => (
  <label className="block">
    <span className="text-base font-semibold">
      {label}
      {optional ? null : <Req />}
    </span>
    <select
      aria-required={!optional || undefined}
      className="select select-bordered mt-1 w-full text-base"
      onChange={(e) => update({ [name]: e.target.value })}
      value={form[name]}
    >
      <option value="">Choose…</option>
      {options.map((o) => (
        <option key={o}>{o}</option>
      ))}
    </select>
  </label>
);
