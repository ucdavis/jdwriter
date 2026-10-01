import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { useCheckCoverage, useSaveEnvelope } from '@/queries/authoring.ts';
import { useState } from 'react';
import type { JobEnvelope, KeyResponsibility } from '@/lib/contracts.ts';

type ListKey = Exclude<
  keyof JobEnvelope,
  'keyResponsibilities' | 'scopeStatement' | 'summary'
>;

const LIST_SECTIONS: Array<[ListKey, string]> = [
  ['requiredCertifications', 'Licenses & certifications'],
  ['education', 'Education'],
  ['workExperience', 'Work experience'],
  ['minQualifications', 'Minimum knowledge, skills & abilities'],
  ['prefQualifications', 'Preferred qualifications'],
  ['workEnvironment', 'Work environment'],
  ['conditionsOfEmployment', 'Conditions of employment'],
  ['physicalRequirements', 'Physical requirements'],
  ['outOfEnvelope', 'Out-of-envelope signals'],
];

/**
 * Curate the class envelope — the standardized template every JD in the class is built
 * from.
 *
 * Two guards matter here. Percentages must total exactly 100 before saving, because an
 * unbalanced envelope produces an unbalanced JD for every position in the class. And
 * coverage can be checked against the real corpus BEFORE committing, using the envelope
 * currently on screen rather than the saved one — the whole point is to see whether an
 * edit degrades fit while it can still be undone.
 */
export const EnvelopeEditor = ({
  initial,
  slug,
}: {
  initial: JobEnvelope;
  slug: string;
}) => {
  const [envelope, setEnvelope] = useState<JobEnvelope>(initial);
  const [saved, setSaved] = useState(false);
  const save = useSaveEnvelope();
  const coverage = useCheckCoverage();

  const update = <K extends keyof JobEnvelope>(key: K, value: JobEnvelope[K]) => {
    setEnvelope((e) => ({ ...e, [key]: value }));
    setSaved(false);
    // A coverage result computed against the previous text no longer describes what is
    // on screen, so showing it would be actively misleading.
    coverage.reset();
  };

  const totalPct = envelope.keyResponsibilities.reduce(
    (sum, r) => sum + (Number(r.pctTime) || 0),
    0
  );
  const balanced = totalPct === 100;

  const patchResp = (index: number, patch: Partial<KeyResponsibility>) =>
    update(
      'keyResponsibilities',
      envelope.keyResponsibilities.map((r, i) => (i === index ? { ...r, ...patch } : r))
    );

  const result = coverage.data;

  return (
    <div className="space-y-5">
      <div className="sticky top-[60px] z-40 -mx-1 flex items-center justify-between bg-base-200 px-1 py-2">
        <div className="flex items-center gap-2">
          <Eyebrow>Editing envelope</Eyebrow>
          {saved ? (
            <Badge tone="green">Saved</Badge>
          ) : (
            <Badge tone="yellow">Unsaved changes</Badge>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            className="btn btn-outline btn-sm"
            disabled={coverage.isPending}
            onClick={() => coverage.mutate({ envelope, slug })}
            type="button"
          >
            {coverage.isPending ? 'Checking coverage…' : 'Check coverage vs JDs'}
          </button>
          <button
            className="btn btn-primary btn-sm"
            disabled={save.isPending || !balanced}
            onClick={() =>
              save.mutate({ envelope, slug }, { onSuccess: () => setSaved(true) })
            }
            title={balanced ? undefined : 'Key Responsibilities must total 100%'}
            type="button"
          >
            {save.isPending ? 'Saving…' : 'Save envelope'}
          </button>
        </div>
      </div>

      {save.error ? <Note tone="red">{messageOf(save.error)}</Note> : null}
      {coverage.error ? <Note tone="red">{messageOf(coverage.error)}</Note> : null}

      {!balanced ? (
        <Note tone="yellow">
          Key Responsibilities total {totalPct}% — saving is disabled until they total
          exactly 100%.
        </Note>
      ) : null}

      {result ? (
        <Note tone={result.meanCoverage >= 90 ? 'green' : 'red'}>
          <span className="font-semibold">
            Coverage of this edited envelope vs {result.n} JDs: {result.meanCoverage}%
          </span>{' '}
          (mean) · {Math.round(result.wellCoveredPct * 100)}% of JDs ≥90% covered.
          {result.meanCoverage < 90 ? (
            <span>
              {' '}
              ⚠ Below the 90% target — you can still save, but many positions would need
              more than 10% customization.
            </span>
          ) : null}
        </Note>
      ) : null}

      <Card className="p-5">
        <Eyebrow>Job summary</Eyebrow>
        <textarea
          aria-label="Job summary"
          className="textarea textarea-bordered mt-1 w-full resize-none text-[13px]"
          onChange={(e) => update('summary', e.target.value)}
          rows={3}
          value={envelope.summary}
        />
        <div className="mt-3">
          <Eyebrow>Scope statement</Eyebrow>
          <textarea
            aria-label="Scope statement"
            className="textarea textarea-bordered mt-1 w-full resize-none text-[13px]"
            onChange={(e) => update('scopeStatement', e.target.value)}
            rows={3}
            value={envelope.scopeStatement}
          />
        </div>
      </Card>

      <Card className="p-5">
        <div className="flex items-center justify-between">
          <Eyebrow>Key responsibilities</Eyebrow>
          <Badge tone={balanced ? 'green' : 'yellow'}>{totalPct}% of time</Badge>
        </div>
        <div className="mt-3 space-y-3">
          {envelope.keyResponsibilities.map((r, i) => (
            <div className="rounded-lg border border-base-300 p-3" key={`${r.functionName}-${i}`}>
              <div className="flex items-center gap-2">
                <input
                  aria-label="Percent of time"
                  className="input input-bordered input-sm w-16 text-right tnum"
                  max={100}
                  min={0}
                  onChange={(e) => patchResp(i, { pctTime: Number(e.target.value) })}
                  type="number"
                  value={r.pctTime}
                />
                <span className="text-[13px] text-base-content/65">%</span>
                <input
                  aria-label="Function name"
                  className="input input-bordered input-sm flex-1 font-semibold"
                  onChange={(e) => patchResp(i, { functionName: e.target.value })}
                  value={r.functionName}
                />
                <button
                  aria-label="Remove function"
                  className="px-1 text-[13px] text-base-content/50 hover:text-error"
                  onClick={() =>
                    update(
                      'keyResponsibilities',
                      envelope.keyResponsibilities.filter((_, j) => j !== i)
                    )
                  }
                  type="button"
                >
                  ✕
                </button>
              </div>
              <div className="mt-2 pl-2">
                <ListEditor
                  items={r.duties}
                  onChange={(duties) => patchResp(i, { duties })}
                  placeholder="Add a duty…"
                />
              </div>
            </div>
          ))}
        </div>
        <button
          className="btn btn-outline btn-sm mt-3"
          onClick={() =>
            update('keyResponsibilities', [
              ...envelope.keyResponsibilities,
              { duties: [], functionName: 'New function', pctTime: 0 },
            ])
          }
          type="button"
        >
          + Add function
        </button>
      </Card>

      {LIST_SECTIONS.map(([key, title]) => (
        <Card className="p-5" key={key}>
          <Eyebrow>{title}</Eyebrow>
          <div className="mt-2">
            <ListEditor
              items={envelope[key]}
              onChange={(v) => update(key, v)}
              placeholder={`Add to ${title.toLowerCase()}…`}
            />
          </div>
        </Card>
      ))}

      <p className="text-[11.5px] text-base-content/50">
        Saving marks this envelope as <strong>manually edited</strong>; re-running ingest
        will preserve it rather than re-synthesizing it from the corpus.
      </p>
    </div>
  );
};

const ListEditor = ({
  items,
  onChange,
  placeholder,
}: {
  items: string[];
  onChange: (items: string[]) => void;
  placeholder: string;
}) => (
  <div className="space-y-1.5">
    {items.map((it, i) => (
      <div className="flex items-start gap-2" key={i}>
        <span className="mt-2 text-[13px] text-primary">•</span>
        <textarea
          aria-label={`Item ${i + 1}`}
          className="textarea textarea-bordered textarea-sm flex-1 resize-y text-[13px]"
          onChange={(e) => onChange(items.map((x, j) => (j === i ? e.target.value : x)))}
          rows={1}
          value={it}
        />
        <button
          aria-label="Remove item"
          className="mt-1.5 text-[13px] text-base-content/50 hover:text-error"
          onClick={() => onChange(items.filter((_, j) => j !== i))}
          type="button"
        >
          ✕
        </button>
      </div>
    ))}
    <button
      className="text-[12px] font-medium text-primary hover:underline"
      onClick={() => onChange([...items, ''])}
      type="button"
    >
      + {placeholder}
    </button>
  </div>
);
