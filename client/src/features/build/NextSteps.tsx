import type { AssembledJd } from '@/lib/contracts.ts';
import { useLinks } from '@/queries/links.ts';
import { Eyebrow } from '@/shared/ui/primitives.tsx';
import { useState } from 'react';

/**
 * What to do with a finished JD: request the position in the workforce management tool
 * (CTHULHU, Module 2 "Requisition & Justification").
 *
 * CTHULHU cannot import a JD file. Its requisition starts from its own Module 1, which accepts
 * pasted JD text, so the steps say exactly that and the copy button does the work. Button and
 * step names are CTHULHU's own, so the instructions match what people will see there.
 */
export const NextSteps = ({ result }: { result: AssembledJd }) => {
  const links = useLinks();
  const wfmUrl = links.data?.wfmUrl ?? null;
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(jdText(result));
    setCopied(true);
  };

  return (
    <section
      aria-labelledby="next-steps-heading"
      className="rounded-xl border border-primary/20 bg-primary/5 p-5"
    >
      <Eyebrow>Next steps</Eyebrow>
      <h2 className="mt-1 text-[16px] font-bold" id="next-steps-heading">
        Request the position in the workforce management tool
      </h2>
      <p className="mt-1 text-[12.5px] text-base-content/65">
        The requisition is built in CTHULHU. It takes this job description as text — it does not
        import files yet.
      </p>
      <ol className="mt-3 list-decimal space-y-2 pl-5 text-[13px]">
        <li>
          Download this JD for your records and routing — Word to edit, PDF to attach.
        </li>
        <li>
          Open{' '}
          {wfmUrl ? (
            <a className="font-semibold text-primary hover:underline" href={wfmUrl} rel="noreferrer" target="_blank">
              CTHULHU
            </a>
          ) : (
            <span className="font-semibold">CTHULHU</span>
          )}{' '}
          and start <span className="font-medium">Describe the Role</span>. Paste this JD there
          and pick <span className="font-medium">{result.title}</span> (UC Job Code{' '}
          <span className="tnum">{result.ucJobCode}</span>).{' '}
          <button className="btn btn-outline btn-xs ml-1 align-baseline" onClick={() => void copy()} type="button">
            {copied ? 'Copied ✓' : 'Copy JD text'}
          </button>
        </li>
        <li>
          Continue through <span className="font-medium">Review Matches</span> and{' '}
          <span className="font-medium">Finalize Job Description</span>, then{' '}
          <span className="font-medium">Approve &amp; Submit to HR</span>.
        </li>
        <li>
          On the confirmation page choose{' '}
          <span className="font-medium">Build Requisition (WMR + VRF)</span>. Have ready:
          <ul className="mt-1 list-disc space-y-0.5 pl-5 text-[12.5px] text-base-content/75">
            <li>
              <span className="font-medium">Workforce Management Review:</span> funding source,
              why the role is critical, alternatives you explored, and the impact of leaving it
              vacant.
            </li>
            <li>
              <span className="font-medium">Vacancy Request Form:</span> working title,
              department, % time, number of vacancies, budget source, and an org chart (PDF or
              image).
            </li>
          </ul>
        </li>
      </ol>
    </section>
  );
};

const list = (title: string, items: string[]) =>
  items.length > 0 ? [title, ...items.map((i) => `• ${i}`), ''] : [];

/** The JD as plain text, in the same order as the page, the PDF and the Word file. */
export const jdText = (r: AssembledJd): string => {
  const { jd } = r;
  const total = jd.keyResponsibilities.reduce((s, x) => s + x.pctTime, 0);
  return [
    r.workingTitle,
    [r.department, `UC Job Code ${r.ucJobCode}`, r.title !== r.workingTitle ? r.title : '']
      .filter(Boolean)
      .join(' · '),
    '',
    'Job Summary',
    jd.jobSummary,
    '',
    `Key Responsibilities — Total ${total}%`,
    ...jd.keyResponsibilities.flatMap((x) => [
      `${x.pctTime}% ${x.functionName}`,
      ...x.duties.map((d) => `• ${d}`),
    ]),
    '',
    'Qualifications',
    ...list('Licenses & Certifications', jd.licensesCertifications),
    ...list('Education', jd.education),
    ...list('Work Experience', jd.workExperience),
    ...list('Minimum Knowledge, Skills & Abilities', jd.minKSA),
    ...list('Preferred Knowledge, Skills & Abilities', jd.prefKSA),
    ...list('Conditions of Employment', jd.conditionsOfEmployment),
    ...list('Work Environment', jd.workEnvironment),
    ...list('Physical Requirements', jd.physicalRequirements),
  ]
    .join('\n')
    .trim();
};
