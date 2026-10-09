import { Badge } from '@/shared/ui/primitives.tsx';
import { titleMatches } from '@/lib/titles.ts';
import { useMemo, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import type { ClassListItem } from '@/lib/contracts.ts';

/** With ~1,200 in-use titles, bound the DOM until the user narrows the list. */
const CAP = 200;

export const ClassPicker = ({ classes }: { classes: ClassListItem[] }) => {
  const [query, setQuery] = useState('');
  const navigate = useNavigate();

  const filtered = useMemo(() => {
    const q = query.trim();
    if (!q) {
      return classes;
    }
    // Abbreviation-aware in both directions: "acad" finds "Academic", and vice versa.
    return classes.filter((c) => titleMatches(q, c.title) || c.ucJobCode.includes(q));
  }, [query, classes]);

  const shown = filtered.slice(0, CAP);
  const hidden = filtered.length - shown.length;

  return (
    <div>
      <input
        aria-label="Search job title or code"
        className="input input-bordered w-full text-base"
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search job title or code…"
        value={query}
      />
      <div className="mt-2 max-h-[320px] divide-y divide-base-300 overflow-y-auto rounded-lg border border-base-300">
        {shown.map((c) => (
          <button
            className={`flex w-full items-center justify-between gap-3 px-3 py-2.5 text-left ${
              c.ready
                ? 'cursor-pointer hover:bg-base-200'
                : 'cursor-not-allowed opacity-55'
            }`}
            disabled={!c.ready}
            key={c.slug}
            onClick={() =>
              c.ready && navigate({ params: { slug: c.slug }, to: '/class/$slug' })
            }
            type="button"
          >
            <span className="flex min-w-0 flex-col">
              <span className="truncate text-base font-semibold">{c.title}</span>
              <span className="text-sm text-base-content/65 tnum">
                Code {c.ucJobCode}
                {c.ready
                  ? c.bargainingUnit
                    ? ` · ${c.bargainingUnit}`
                    : ''
                  : c.family
                    ? ` · ${c.family}`
                    : ''}
              </span>
            </span>
            {c.ready ? (
              <Badge tone="green">{c.corpusSize} JDs · ready</Badge>
            ) : (
              <Badge tone="muted">not yet ingested</Badge>
            )}
          </button>
        ))}
        {hidden > 0 ? (
          <div className="px-3 py-2.5 text-center text-sm text-base-content/50">
            +{hidden.toLocaleString()} more in-use titles — refine your search to narrow
            the list.
          </div>
        ) : null}
        {filtered.length === 0 ? (
          <div className="px-3 py-6 text-center text-base text-base-content/65">
            No matching classes.
          </div>
        ) : null}
      </div>
    </div>
  );
};
