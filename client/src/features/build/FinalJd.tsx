import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import type { AssembledJd } from '@/lib/contracts.ts';
import { Link } from '@tanstack/react-router';
import { appUrl } from '@/lib/basePath.ts';
import { NextSteps } from './NextSteps.tsx';
import type { ReactNode } from 'react';

/**
 * The finished job description — the artifact a supervisor hands to HR — plus the
 * compliance audit trail.
 *
 * The audit trail is not decoration. Every rewrite records what changed and why, so an
 * analyst can see that a duty was reworded and on whose authority. A silent rewrite of a
 * legal document would be the worst failure this app could have.
 */
export const FinalJd = ({
  onEdit,
  result,
}: {
  /**
   * Set when the JD was just built: the screen opens with the outcome — passed, what's next,
   * downloads, and a way back to make changes. Without it (a saved JD reopened), a plain header.
   */
  onEdit?: () => void;
  result: AssembledJd;
}) => {
  const { jd } = result;
  const totalPct = jd.keyResponsibilities.reduce((s, r) => s + r.pctTime, 0);
  // The server reports the shortfall and refuses to call such a JD publishable. If one
  // reaches this screen anyway, say so rather than offering a download that looks final.
  const publishable = result.unallocatedPct === 0;
  // As the author stated them, matching the Word and Markdown files.
  const supervision = [
    result.supervises == null
      ? null
      : result.supervises && result.supervisesCount != null
        ? `Supervises: Yes (${result.supervisesCount} ${result.supervisesCount === 1 ? 'person' : 'people'})`
        : `Supervises: ${result.supervises ? 'Yes' : 'No'}`,
    result.leads == null ? null : `Leads: ${result.leads ? 'Yes' : 'No'}`,
  ].filter((x): x is string => x !== null);
  const savedStatus = result.authoredJdId ? (
    <span className="text-base text-base-content/65" data-testid="saved-status">
      Saved as{' '}
      <Badge tone={result.status === 'ready' ? 'green' : 'yellow'}>
        {result.status === 'ready' ? 'Ready' : 'Draft'}
      </Badge>{' '}
      ·{' '}
      <Link className="text-primary hover:underline" to="/jds">
        My JDs
      </Link>
    </span>
  ) : null;
  const downloads = (
    <div className="flex gap-2">
      {/* Word comes from the saved record, so it needs one; every assembly is saved. */}
      {publishable && result.authoredJdId ? (
        <a
          className="btn btn-outline btn-sm"
          download
          href={appUrl(`/api/jds/${result.authoredJdId}/docx`)}
        >
          ⤓ Download Word
        </a>
      ) : null}
      <button
        className="btn btn-primary btn-sm"
        disabled={!publishable}
        onClick={() => window.print()}
        title={publishable ? undefined : 'Percent of time must total 100%'}
        type="button"
      >
        ⤓ Download PDF
      </button>
    </div>
  );

  return (
    <div className="space-y-4">
      {onEdit ? (
        <Outcome
          downloads={downloads}
          onEdit={onEdit}
          passed={publishable && result.status === 'ready'}
          savedStatus={savedStatus}
        />
      ) : null}
      {publishable ? null : (
        <Note tone="red">
          This job description accounts for only {100 - result.unallocatedPct}% of time —{' '}
          {result.unallocatedPct}% is unallocated, so it is not publishable. Go back to
          tailoring and assign the remaining time.
        </Note>
      )}

      {result.fromEnvelope ? (
        <p className="text-base text-base-content/65" data-testid="from-envelope">
          Assembled straight from the class envelope — nothing was changed, so no AI review was
          needed.
        </p>
      ) : null}

      {result.authoredJdId && result.corpusNote ? (
        result.inCorpus ? (
          <Note tone="green">
            <span data-testid="corpus-note">{result.corpusNote}</span>
          </Note>
        ) : (
          <p className="text-base text-base-content/65" data-testid="corpus-note">
            {result.corpusNote}
          </p>
        )
      ) : null}

      {onEdit ? null : (
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Eyebrow>Finished job description</Eyebrow>
            {savedStatus}
          </div>
          {downloads}
        </div>
      )}

      {publishable && result.status === 'ready' ? <NextSteps result={result} /> : null}

      <div id="jd-print">
        <Card className="p-6">
          <div className="flex items-center justify-between gap-3 border-b border-base-300 pb-3">
            <div>
              <h2 className="text-2xl font-bold">{result.workingTitle}</h2>
              <div className="mt-0.5 text-base text-base-content/65">
                {result.department ? `${result.department} · ` : ''}UC Job Code{' '}
                <span className="tnum">{result.ucJobCode}</span>
              </div>
            </div>
            <div className="flex gap-2">
              {result.salaryGrade ? <Badge tone="accent">{result.salaryGrade}</Badge> : null}
              {result.flsaStatus ? <Badge tone="muted">{result.flsaStatus}</Badge> : null}
            </div>
          </div>
          {result.bargainingUnit || supervision.length > 0 ? (
            <div className="mt-2 text-sm text-base-content/50" data-testid="jd-facts">
              {[result.bargainingUnit ? `Bargaining Unit: ${result.bargainingUnit}` : null, ...supervision]
                .filter(Boolean)
                .join(' · ')}
            </div>
          ) : null}

          <Section title="Job Summary">
            <p className="text-base leading-relaxed">{jd.jobSummary}</p>
          </Section>

          {/* The total is displayed because it is a compliance property of the document,
              not an implementation detail — HRTMS rejects a JD that does not sum to 100. */}
          <Section title={`Key Responsibilities — Total ${totalPct}%`}>
            <div className="space-y-3">
              {jd.keyResponsibilities.map((r) => (
                <div key={r.functionName}>
                  <div className="flex items-baseline gap-2">
                    <span className="w-10 text-base font-bold text-primary tnum">
                      {r.pctTime}%
                    </span>
                    <span className="text-base font-semibold">{r.functionName}</span>
                  </div>
                  <ul className="ml-12 mt-1 space-y-1">
                    {r.duties.map((d) => (
                      <li className="flex gap-2 text-base" key={d}>
                        <span className="text-primary">•</span>
                        <span>{d}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          </Section>

          <div className="mt-5 border-t border-base-300 pt-4">
            <div className="eyebrow mb-2">Qualifications</div>
            <SubList items={jd.licensesCertifications} label="Licenses & Certifications" />
            <SubList items={jd.education} label="Education" />
            <SubList items={jd.workExperience} label="Work Experience" />
            <SubList items={jd.minKSA} label="Minimum Knowledge, Skills & Abilities" />
            <SubList items={jd.prefKSA} label="Preferred Knowledge, Skills & Abilities" />
          </div>

          <BulletSection items={jd.conditionsOfEmployment} title="Conditions of Employment" />
          <BulletSection items={jd.workEnvironment} title="Work Environment" />
          <BulletSection items={jd.physicalRequirements} title="Physical Requirements" />
        </Card>
      </div>

      <Card className="p-5">
        <div className="flex items-center justify-between">
          <Eyebrow>Compliance audit trail</Eyebrow>
          <Badge tone={result.complianceEdits.length ? 'green' : 'muted'}>
            {result.complianceEdits.length} edit
            {result.complianceEdits.length === 1 ? '' : 's'}
          </Badge>
        </div>
        {result.complianceEdits.length === 0 ? (
          <p className="mt-2 text-base text-base-content/65">
            No compliance issues found.
          </p>
        ) : (
          <ul className="mt-3 space-y-3">
            {result.complianceEdits.map((e) => (
              <li className="text-base" key={e.section}>
                <div className="flex items-center gap-2">
                  <Badge tone={e.source === 'rule' ? 'muted' : 'purple'}>{e.source}</Badge>
                  <span className="text-base-content/50 tnum">{e.section}</span>
                </div>
                <div className="mt-1 text-base-content/65">{e.reason}</div>
                <div className="mt-1 flex flex-col gap-0.5">
                  <span className="text-error line-through">{e.before}</span>
                  <span className="text-success">{e.after}</span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
};

/** The outcome of a build: passed (or not yet), downloads, and how to make changes. */
const Outcome = ({
  downloads,
  onEdit,
  passed,
  savedStatus,
}: {
  downloads: ReactNode;
  onEdit: () => void;
  passed: boolean;
  savedStatus: ReactNode;
}) => (
  <Card className={`p-5 ${passed ? 'border-success/40' : 'border-warning/50'}`}>
    <h2 className="text-2xl font-bold" data-testid="build-outcome">
      {passed ? '✓ Great! Your JD passed.' : 'Your JD isn’t ready yet.'}
    </h2>
    <p className="mt-2 text-base">
      {passed
        ? 'You can move on to the next steps below, or download your JD here:'
        : 'It’s saved as a draft. Make the changes noted below, then assemble it again.'}
    </p>
    <div className="mt-3 flex flex-wrap items-center gap-3">
      {downloads}
      {savedStatus}
    </div>
    <div className="mt-4 border-t border-base-300 pt-3">
      <p className="text-base text-base-content/65">Need to make changes?</p>
      <button className="btn btn-outline btn-sm mt-2" onClick={onEdit} type="button">
        Make changes
      </button>
    </div>
  </Card>
);

const Section = ({ children, title }: { children: ReactNode; title: string }) => (
  <div className="mt-5 border-t border-base-300 pt-4">
    <div className="eyebrow mb-2">{title}</div>
    {children}
  </div>
);

const SubList = ({ items, label }: { items: string[]; label: string }) => {
  if (!items?.length) {
    return null;
  }
  return (
    <div className="mt-3">
      <div className="text-base font-semibold">{label}</div>
      <ul className="mt-1 space-y-1">
        {items.map((it) => (
          <li className="flex gap-2 text-base" key={it}>
            <span className="text-primary">•</span>
            <span>{it}</span>
          </li>
        ))}
      </ul>
    </div>
  );
};

const BulletSection = ({ items, title }: { items: string[]; title: string }) => {
  if (!items?.length) {
    return null;
  }
  return (
    <Section title={title}>
      <ul className="space-y-1.5">
        {items.map((it) => (
          <li className="flex gap-2 text-base" key={it}>
            <span className="text-primary">•</span>
            <span>{it}</span>
          </li>
        ))}
      </ul>
    </Section>
  );
};
