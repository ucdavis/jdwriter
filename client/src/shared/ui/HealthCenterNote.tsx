import type { HealthCenterInfo } from '@/lib/contracts.ts';
import { Link } from '@tanstack/react-router';
import { Badge } from './primitives.tsx';

/** Health Center classes say so wherever they appear: a title with an HC token. */
export const isHealthCenterTitle = (title: string) => /\bhc\b/i.test(title);

/** The "Health Center only" badge. */
export const HealthCenterBadge = () => <Badge tone="teal">Health Center only</Badge>;

/**
 * A class's Health Center story, wherever it is suggested: the badge on an HC class, and — when it
 * has a twin — which code to use instead for a position at (or not at) the Health Center.
 */
export const HealthCenterNote = ({
  healthCenterOnly,
  healthCenterTwin: twin,
  linked = true,
}: HealthCenterInfo & {
  /** False inside something already clickable, where a nested link is invalid. */
  linked?: boolean;
}) => {
  if (!twin) {
    return healthCenterOnly ? (
      <div className="mt-2">
        <HealthCenterBadge />
      </div>
    ) : null;
  }

  const name = twin.slug && linked ? (
    <Link className="font-semibold text-primary hover:underline" params={{ slug: twin.slug }} to="/class/$slug">
      {twin.title}
    </Link>
  ) : (
    <span className="font-semibold">{twin.title}</span>
  );

  return (
    <div className="mt-2 flex flex-wrap items-center gap-2 text-base" data-testid="health-center-note">
      {healthCenterOnly ? <HealthCenterBadge /> : null}
      <span className="text-base-content/75">
        {twin.healthCenter ? 'At the Health Center? Use ' : 'Not at the Health Center? Use '}
        {name} <span className="tnum">({twin.ucJobCode})</span> instead.
      </span>
    </div>
  );
};
