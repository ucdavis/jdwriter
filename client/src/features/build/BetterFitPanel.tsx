import { HealthCenterBadge, isHealthCenterTitle } from '@/shared/ui/HealthCenterNote.tsx';
import { Link } from '@tanstack/react-router';

/**
 * The answer to "is there a better fit?", from a borderline check or from "Look for a better
 * fit". A suggestion is offered, never imposed: the build may stretch its class and still
 * belong in it, and an empty suggestion is an answer — the current class is still best.
 */
export const BetterFitPanel = ({
  currentTitle,
  rationale,
  suggestedClass,
  suggestedSlug,
}: {
  currentTitle: string;
  rationale: string;
  suggestedClass: string;
  suggestedSlug: string;
}) =>
  suggestedClass ? (
    <div className="mt-4 rounded-lg border border-primary/20 bg-primary/10 p-3.5" data-testid="better-fit">
      <div className="text-base">
        This position may fit <span className="font-semibold text-primary">{suggestedClass}</span>{' '}
        better than {currentTitle}.
        {isHealthCenterTitle(suggestedClass) ? (
          <span className="ml-2">
            <HealthCenterBadge />
          </span>
        ) : null}
      </div>
      {rationale ? <div className="mt-1 text-base text-base-content/65">{rationale}</div> : null}
      {suggestedSlug ? (
        <div className="mt-2.5 flex flex-wrap items-center gap-2">
          <Link className="btn btn-primary btn-sm" params={{ slug: suggestedSlug }} to="/class/$slug">
            Look at {suggestedClass} →
          </Link>
          <span className="text-sm text-base-content/50">
            Opens its standard fresh — save a draft first to keep this build.
          </span>
        </div>
      ) : null}
    </div>
  ) : (
    <div className="mt-4 rounded-lg border border-base-300 bg-base-200 p-3.5" data-testid="better-fit">
      <div className="text-base">
        No class fits clearly better — <span className="font-semibold">{currentTitle}</span> is still
        the best match.
      </div>
      {rationale ? <div className="mt-1 text-base text-base-content/65">{rationale}</div> : null}
    </div>
  );
