import type { SiteInfo, SiteTwin } from '@/lib/contracts.ts';
import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { Badge } from './primitives.tsx';

/**
 * The site a title is only for, read from its marker: SHS is the Student Health Center, VMTH the
 * Veterinary Medical Teaching Hospital, HC the Health Center. The most specific marker wins
 * ("HC Adm SHS Mgr 1", HC as health care, is Student Health).
 * Used where only a title is to hand; classes and matches carry the server's `site` instead.
 */
export const siteOfTitle = (title: string): string | null =>
  /\bshs\b/i.test(title)
    ? 'Student Health Center'
    : /\bvmth\b/i.test(title)
      ? 'Veterinary Medical Teaching Hospital'
      : /\bhc\b/i.test(title)
        ? 'Health Center'
        : null;

/** "Health Center only", "Student Health Center only". */
export const SiteBadge = ({ site }: { site: string }) => <Badge tone="teal">{site} only</Badge>;

/**
 * A class's site story, wherever it is suggested: the badge on a site-only class, and — for each
 * twin — which code to use instead at (or away from) that site.
 */
export const SiteNote = ({
  linked = true,
  site,
  siteTwins,
}: SiteInfo & {
  /** False inside something already clickable, where a nested link is invalid. */
  linked?: boolean;
}) => {
  const twins = siteTwins ?? [];
  if (!site && twins.length === 0) {
    return null;
  }

  const name = (twin: SiteTwin): ReactNode =>
    twin.slug && linked ? (
      <Link className="font-semibold text-primary hover:underline" params={{ slug: twin.slug }} to="/class/$slug">
        {twin.title}
      </Link>
    ) : (
      <span className="font-semibold">{twin.title}</span>
    );

  return (
    <div className="mt-2 flex flex-col gap-1 text-base" data-testid="site-note">
      {site ? (
        <div>
          <SiteBadge site={site} />
        </div>
      ) : null}
      {twins.map((twin) => (
        <span className="text-base-content/75" key={twin.ucJobCode}>
          {twin.site ? `At the ${twin.site}? Use ` : `Not at the ${site ?? 'site'}? Use `}
          {name(twin)} <span className="tnum">({twin.ucJobCode})</span> instead.
        </span>
      ))}
    </div>
  );
};
