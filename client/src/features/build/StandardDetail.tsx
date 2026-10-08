import { Eyebrow } from '@/shared/ui/primitives.tsx';
import type { ClassStandard } from '@/lib/contracts.ts';
import type { ReactNode } from 'react';

/**
 * The official UC job standard, shown in full.
 *
 * Standards COMPLEMENT the corpus-derived envelope rather than replacing it: the corpus
 * drives the % time responsibilities (reflecting what UC Davis JDs actually say), while
 * the standard supplies authoritative qualifications and scope. The footnote says so
 * because it is the single most misunderstood thing about this screen.
 */
export const StandardDetail = ({
  className = '',
  standard,
}: {
  className?: string;
  standard: ClassStandard;
}) => {
  const meta = [
    standard.grade && (/grade/i.test(standard.grade) ? standard.grade : `Grade ${standard.grade}`),
    standard.flsa,
    standard.persProg,
    standard.union && (/unit/i.test(standard.union) ? standard.union : `Unit ${standard.union}`),
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <div
      className={`space-y-3 rounded-lg border border-info/20 bg-info/5 px-3.5 py-3 ${className}`}
    >
      {meta ? <div className="text-xs text-base-content/65">{meta}</div> : null}
      {standard.genericScope || standard.customScope ? (
        <Block label="Scope">
          <p className="text-sm leading-relaxed">
            {standard.genericScope || standard.customScope}
          </p>
        </Block>
      ) : null}
      <ListBlock items={standard.keyResponsibilities} label="Key responsibilities (standard)" />
      <ListBlock items={standard.ksa} label="Knowledge, skills & abilities" />
      <ListBlock items={standard.education} label="Education" />
      <ListBlock items={standard.licenses} label="Licenses & certifications" />
      <ListBlock items={standard.specialConditions} label="Special conditions" />
      <p className="pt-1 text-xs text-base-content/50">
        Authoritative baseline for qualifications and scope. The corpus still drives the
        % time responsibilities — re-ingest this class to blend the standard into its
        synthesized envelope.
      </p>
    </div>
  );
};

/** A collapsed disclosure, for use on a list card where the standard is secondary. */
export const StandardBlock = ({ standard }: { standard: ClassStandard }) => (
  <details className="mt-3">
    <summary className="cursor-pointer text-sm font-medium text-info">
      Official standard linked · {standard.longTitle}
    </summary>
    <StandardDetail className="mt-2" standard={standard} />
  </details>
);

const Block = ({ children, label }: { children: ReactNode; label: string }) => (
  <div>
    <div className="mb-1 text-xs font-semibold uppercase tracking-wide text-base-content/50">
      {label}
    </div>
    {children}
  </div>
);

const ListBlock = ({ items, label }: { items: string[]; label: string }) => {
  if (!items?.length) {
    return null;
  }
  return (
    <Block label={label}>
      <ul className="space-y-1">
        {items.map((it) => (
          <li className="flex gap-1.5 text-sm leading-snug" key={it}>
            <span className="text-info">·</span>
            <span>{it}</span>
          </li>
        ))}
      </ul>
    </Block>
  );
};

export { Eyebrow };
