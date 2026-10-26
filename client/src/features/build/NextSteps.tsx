import type { AssembledJd } from '@/lib/contracts.ts';
import { appUrl } from '@/lib/basePath.ts';
import { Link } from '@tanstack/react-router';
import { Eyebrow } from '@/shared/ui/primitives.tsx';
import type { ReactNode } from 'react';

const AGGIESERVICE = 'https://aggieservice.ucdavis.edu/s/';

/**
 * What happens after a JD is finished, as the process runs today. JDWriter can't file the request
 * itself — there's no ticket integration yet — so the author downloads the JD and attaches it to
 * an AggieService ticket for the Service Channel. A vacancy request comes first, then classification
 * by Compensation, then recruitment; the Service Channel representative follows up in AggieService.
 */
export const NextSteps = ({ result }: { result: AssembledJd }) => {
  const id = result.authoredJdId;
  if (id === null) {
    return null;
  }

  return (
    <section
      aria-labelledby="next-steps-heading"
      className="rounded-xl border border-primary/20 bg-primary/5 p-5"
    >
      <Eyebrow>Next steps</Eyebrow>
      <h2 className="mt-1 text-base font-bold" id="next-steps-heading">
        Send this job description to the Service Channel
      </h2>
      <ol className="mt-3 space-y-3 text-base">
        <Step n={1} title="Download it">
          <a className="btn btn-primary btn-sm mt-1" download href={appUrl(`/api/jds/${id}/docx`)}>
            ⤓ Download the Word document
          </a>
        </Step>
        <Step n={2} title="Fill in the Vacancy Request Form">
          It starts with this position filled in; the rest takes a few minutes.
          <div>
            <Link className="btn btn-outline btn-sm mt-1" search={{ jd: id }} to="/vrf">
              Start the Vacancy Request Form →
            </Link>
          </div>
        </Step>
        <Step n={3} title="Include them in your AggieService ticket">
          Attach the job description, the Vacancy Request Form and an org chart when you create your ticket in{' '}
          <a className="text-primary underline" href={AGGIESERVICE} rel="noreferrer" target="_blank">
            AggieService
          </a>
          .
        </Step>
        <Step n={4} title="What happens next">
          The process today is a vacancy request form, then classification by Compensation, then
          recruitment. Your Service Channel representative will reach out through AggieService with any
          questions.
        </Step>
      </ol>
    </section>
  );
};

const Step = ({ children, n, title }: { children: ReactNode; n: number; title: string }) => (
  <li className="flex gap-3">
    <span
      aria-hidden
      className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary/15 text-base font-bold text-primary"
    >
      {n}
    </span>
    <div>
      <div className="font-semibold">{title}</div>
      <div className="mt-0.5 text-base-content/75">{children}</div>
    </div>
  </li>
);
