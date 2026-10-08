import type { AssembledJd } from '@/lib/contracts.ts';
import { appUrl } from '@/lib/basePath.ts';
import { useLinks } from '@/queries/links.ts';
import { Eyebrow } from '@/shared/ui/primitives.tsx';
import type { ReactNode } from 'react';

/**
 * What happens after a JD is finished. The process is JDWriter → workforce management (WFM) →
 * HR: the JD is handed to the WFM tool, where the author completes the workforce management
 * justification and submits it, the JD and anything else required to HR as one package.
 *
 * The hand-off is a link, not a copy: the button opens WFM with the URL of this JD's handoff
 * document (jdwriter.jd v1, docs/WFM-HANDOFF.md), which WFM fetches from the same host. The JD
 * stays authoritative here, and nothing large or sensitive travels in the address bar.
 */
export const NextSteps = ({ result }: { result: AssembledJd }) => {
  const links = useLinks();
  const wfmUrl = links.data?.wfmUrl ?? null;
  const id = result.authoredJdId;
  if (id === null) {
    return null;
  }

  const handoff = new URL(appUrl(`/api/jds/${id}/handoff`), window.location.origin).href;

  return (
    <section
      aria-labelledby="next-steps-heading"
      className="rounded-xl border border-primary/20 bg-primary/5 p-5"
    >
      <Eyebrow>Next steps</Eyebrow>
      <h2 className="mt-1 text-[16px] font-bold" id="next-steps-heading">
        Take this job description to the workforce management justification
      </h2>
      <ol className="mt-3 space-y-3 text-[13px]">
        <Step done n={1} title="Job description">
          Finished and saved. Download it as Word or PDF for your records.
        </Step>
        <Step n={2} title="Workforce management justification">
          Opens the workforce management tool with this job description attached. There you
          complete the justification and the request for the position.
          <div className="mt-2">
            {wfmUrl ? (
              <a className="btn btn-primary btn-sm" href={startUrl(wfmUrl, handoff)}>
                Start the workforce management justification →
              </a>
            ) : (
              <span className="text-[12px] text-base-content/65">
                The workforce management tool is not available yet. Until it is, keep the
                downloads below with your request:{' '}
                <a className="text-primary hover:underline" download href={appUrl(`/api/jds/${id}/markdown`)}>
                  Markdown
                </a>
                {' · '}
                <a className="text-primary hover:underline" download href={appUrl(`/api/jds/${id}/handoff`)}>
                  JSON
                </a>
              </span>
            )}
          </div>
        </Step>
        <Step n={3} title="Submit to HR">
          In the workforce management tool, add this job description and anything else HR
          requires — an org chart, for example — and submit it all as one complete package.
        </Step>
      </ol>
    </section>
  );
};

/** WFM's start page, told where the JD comes from and where to fetch it. */
const startUrl = (wfmUrl: string, handoff: string) => {
  const url = new URL(wfmUrl);
  url.searchParams.set('source', 'jdwriter');
  url.searchParams.set('jd', handoff);
  return url.href;
};

const Step = ({
  children,
  done = false,
  n,
  title,
}: {
  children: ReactNode;
  done?: boolean;
  n: number;
  title: string;
}) => (
  <li className="flex gap-3">
    <span
      aria-hidden
      className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-[12px] font-bold ${
        done ? 'bg-success text-success-content' : 'bg-primary/15 text-primary'
      }`}
    >
      {done ? '✓' : n}
    </span>
    <div>
      <div className="font-semibold">
        {title}
        {done ? <span className="sr-only"> (done)</span> : null}
      </div>
      <div className="mt-0.5 text-base-content/75">{children}</div>
    </div>
  </li>
);
