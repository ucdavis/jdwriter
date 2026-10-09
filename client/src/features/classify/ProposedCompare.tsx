import { Badge } from '@/shared/ui/primitives.tsx';
import type { ProposedAssessment } from '@/lib/contracts.ts';

type Proposed = { code: string; ingested: boolean; title: string };

/**
 * Compares the classification the unit PROPOSED against the one this analysis reached.
 *
 * The model never saw the proposed code — it is withheld from the ranking prompt and
 * argued separately afterwards — so agreement here means two independent reads landed in
 * the same place, rather than one echoing the other. That is the whole value of the
 * section and the reason it is worded this way.
 *
 * THE DISTINCTION THAT MATTERS: a proposed class with no ingested corpus is a CORPUS
 * GAP, not a disagreement. The classifier can only return classes it has ingested, so a
 * class that was never ingested could not possibly have been returned. Calling that a
 * disagreement would tell an analyst their unit is wrong when the truth is that our own
 * data is incomplete.
 */
export const ProposedCompare = ({
  assessment,
  got,
  proposed,
}: {
  assessment?: ProposedAssessment;
  got: string;
  proposed: Proposed;
}) => {
  const label = proposed.title || `job code ${proposed.code}`;
  const agrees = proposed.title.trim().toLowerCase() === got.trim().toLowerCase();
  const corpusGap = !proposed.ingested;

  const tone = agrees ? 'green' : corpusGap ? 'yellow' : 'orange';
  const badge = agrees
    ? 'Agrees with the unit'
    : corpusGap
      ? 'Corpus gap — not a disagreement'
      : 'Differs from the unit';

  return (
    <div className="mt-3 rounded-lg border border-base-300 bg-base-200 px-3.5 py-3">
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone={tone}>{badge}</Badge>
        <span className="text-base text-base-content/65">
          The form proposes <span className="font-semibold text-base-content">{label}</span>
          {proposed.title ? ` (${proposed.code})` : ''}; this analysis says{' '}
          <span className="font-semibold text-base-content">{got}</span>.
        </span>
      </div>

      {corpusGap ? (
        <p className="mt-2 text-base text-base-content/65">
          <span className="font-semibold text-base-content">{label}</span> has no ingested
          JD corpus, so it could not have been returned by the classifier at all. Treat
          this as a gap in our corpus rather than as a disagreement with the unit — ingest
          that class before drawing any conclusion.
        </p>
      ) : null}

      {assessment ? (
        <div className="mt-2.5">
          <p className="text-base">{assessment.summary}</p>
          {assessment.comparedAs ? (
            <p className="mt-1 text-sm text-base-content/50">
              {label} is superseded, so it was assessed as {assessment.comparedAs}.
            </p>
          ) : null}

          {assessment.contradicts.length > 0 ? (
            <div className="mt-3">
              <div className="mb-1.5 text-sm font-semibold text-base-content/50">
                What doesn&apos;t fit {label}
              </div>
              <ul className="space-y-1.5">
                {assessment.contradicts.map((c) => (
                  <li className="text-base text-base-content/65" key={c.point}>
                    <span className="mr-1.5 text-error">•</span>
                    <span className="text-base-content">{c.point}</span>
                    {/* The quoted evidence is what makes this usable in a conversation
                        with the department — a bare assertion is not something an
                        analyst can hand back to a unit. */}
                    <span className="ml-4 mt-0.5 block italic text-base-content/50">
                      “{c.evidence}”
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {assessment.missing.length > 0 ? (
            <div className="mt-3">
              <div className="mb-1.5 text-sm font-semibold text-base-content/50">
                Expected by {label} but absent from the description
              </div>
              <ul className="space-y-1">
                {assessment.missing.map((m) => (
                  <li className="text-base text-base-content/65" key={m}>
                    <span className="mr-1.5 text-warning">•</span>
                    {m}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {assessment.supports.length > 0 ? (
            <div className="mt-3">
              <div className="mb-1.5 text-sm font-semibold text-base-content/50">
                What does fit {label}
              </div>
              <ul className="space-y-1">
                {assessment.supports.map((s) => (
                  <li className="text-base text-base-content/65" key={s}>
                    <span className="mr-1.5 text-success">✓</span>
                    {s}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <p className="mt-3 text-sm text-base-content/50">
            The ranking above was produced without showing the model the proposed code, so
            it couldn&apos;t simply ratify it. This section then assessed {label} on its
            own merits
            {assessment.basis === 'standard'
              ? ', using its official job standard'
              : assessment.basis === 'none'
                ? ', with neither a corpus envelope nor a standard to go on'
                : ''}
            .
          </p>
        </div>
      ) : null}
    </div>
  );
};
