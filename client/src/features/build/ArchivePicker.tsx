import { Badge } from '@/shared/ui/primitives.tsx';
import type { ArchivedJd } from '@/lib/contracts.ts';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { useArchive, useStartFromArchive } from '@/queries/classes.ts';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

const originLabel: Record<ArchivedJd['origin'], string> = {
  authored: 'Written in JDWriter',
  classify: 'From Classify',
  export: 'HRTMS',
  imported: 'Imported',
};

/**
 * "Start from an archived job description": a line at the top of the build that opens the class's
 * own JDs, to search, preview and work from. Working from one is the goodness-of-fit rewrite that
 * keeps a unit's specifics — its duties placed under the class's functions, its summary kept — saved
 * as a draft that opens here, to be checked like any other build.
 */
export const ArchivePicker = ({ slug }: { slug: string }) => {
  const archive = useArchive(slug);
  const [open, setOpen] = useState(false);
  const jds = archive.data?.jds ?? [];

  if (jds.length === 0) {
    return null;
  }

  return (
    <>
      <p className="text-base">
        <button className="text-primary hover:underline" onClick={() => setOpen(true)} type="button">
          Or start from an archived job description ({jds.length})
        </button>
      </p>
      {open ? <ArchiveDialog jds={jds} onClose={() => setOpen(false)} slug={slug} /> : null}
    </>
  );
};

const matches = (jd: ArchivedJd, query: string) => {
  const q = query.trim().toLowerCase();
  return (
    q === '' ||
    [jd.workingTitle, jd.department, jd.summary, ...jd.responsibilities.flatMap((r) => [r.functionName, ...r.duties])]
      .join('\n')
      .toLowerCase()
      .includes(q)
  );
};

const ArchiveDialog = ({ jds, onClose, slug }: { jds: ArchivedJd[]; onClose: () => void; slug: string }) => {
  const [query, setQuery] = useState('');
  const [chosen, setChosen] = useState<ArchivedJd | null>(null);
  const start = useStartFromArchive(slug);
  const navigate = useNavigate();
  const shown = jds.filter((jd) => matches(jd, query));

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const workFrom = (jd: ArchivedJd) =>
    start.mutate(jd.sourceFile, {
      onSuccess: (r) => {
        onClose();
        void navigate({ params: { slug: r.slug }, search: { draft: r.authoredJdId }, to: '/class/$slug' });
      },
    });

  return (
    <div aria-labelledby="archive-dialog-title" aria-modal className="modal modal-open" role="dialog">
      <div className="modal-box max-w-3xl">
        {chosen ? (
          <>
            <button className="text-base text-primary hover:underline" onClick={() => setChosen(null)} type="button">
              ← All archived job descriptions
            </button>
            <h2 className="mt-2 text-2xl font-bold" id="archive-dialog-title">
              {chosen.workingTitle}
            </h2>
            <div className="mt-1 flex flex-wrap items-center gap-2 text-base text-base-content/65">
              {chosen.department}
              <Badge tone="muted">{originLabel[chosen.origin]}</Badge>
            </div>
            <div className="mt-4 max-h-[50vh] overflow-y-auto pr-1" data-testid="archive-preview">
              {chosen.summary ? <p className="text-base leading-relaxed">{chosen.summary}</p> : null}
              {chosen.responsibilities.map((r, i) => (
                // Archived JDs can repeat a function name or a duty, so its place identifies it.
                <div className="mt-4" key={i}>
                  <div className="text-base font-semibold">
                    {r.pct != null ? <span className="tnum text-primary">{r.pct}% </span> : null}
                    {r.functionName}
                  </div>
                  <ul className="mt-1 list-disc space-y-1 pl-5 text-base">
                    {r.duties.map((d, j) => (
                      <li key={j}>{d}</li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
            <p className="mt-4 text-sm text-base-content/65">
              Its summary and duties come into the builder under this class&apos;s responsibilities, replacing
              anything you&apos;ve changed so far. The class&apos;s qualifications stay, and work outside the
              class is left out. You&apos;ll check it before moving on, as usual.
            </p>
            {start.error ? (
              <p className="mt-2 text-sm text-error" role="alert">
                {messageOf(start.error)}
              </p>
            ) : null}
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={onClose} type="button">
                Close
              </button>
              <button className="btn btn-primary" disabled={start.isPending} onClick={() => workFrom(chosen)} type="button">
                {start.isPending ? 'Placing its duties…' : 'Work from this JD'}
              </button>
            </div>
          </>
        ) : (
          <>
            <div className="flex items-start justify-between gap-3">
              <h2 className="text-2xl font-bold" id="archive-dialog-title">
                Archived job descriptions
              </h2>
              <button className="btn btn-ghost btn-sm" onClick={onClose} type="button">
                Close
              </button>
            </div>
            <input
              aria-label="Search archived job descriptions"
              className="input input-bordered mt-3 w-full"
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search by title, department or wording"
              type="search"
              value={query}
            />
            <ul className="mt-3 max-h-[55vh] divide-y divide-base-300 overflow-y-auto" data-testid="archive-list">
              {shown.map((jd) => (
                <li key={jd.sourceFile}>
                  <button
                    className="w-full px-1 py-2.5 text-left hover:bg-base-200"
                    onClick={() => {
                      start.reset();
                      setChosen(jd);
                    }}
                    type="button"
                  >
                    <div className="text-base font-semibold">{jd.workingTitle}</div>
                    <div className="mt-0.5 flex flex-wrap items-center gap-2 text-sm text-base-content/65">
                      {jd.department}
                      <Badge tone="muted">{originLabel[jd.origin]}</Badge>
                      <span>
                        {jd.responsibilities.length} responsibilit{jd.responsibilities.length === 1 ? 'y' : 'ies'}
                      </span>
                    </div>
                  </button>
                </li>
              ))}
              {shown.length === 0 ? <li className="py-3 text-base text-base-content/65">No match.</li> : null}
            </ul>
          </>
        )}
      </div>
      <button aria-label="Close dialog" className="modal-backdrop" onClick={onClose} type="button" />
    </div>
  );
};
