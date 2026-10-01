import { Badge, Card, PageHeader } from '@/shared/ui/primitives.tsx';
import { useIsAdmin } from '@/shared/ui/AppShell.tsx';
import { type JdScope, savedJdsQueryOptions, useSavedJds } from '@/queries/jds.ts';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useState } from 'react';
import type { RouterContext } from '@/main.tsx';

export const Route = createFileRoute('/(authenticated)/jds/')({
  component: SavedJdsPage,
  loader: ({ context }: { context: RouterContext }) =>
    context.queryClient.ensureQueryData(savedJdsQueryOptions('mine')),
});

const when = (iso: string) => new Date(iso).toLocaleDateString();

function SavedJdsPage() {
  const isAdmin = useIsAdmin();
  const [scope, setScope] = useState<JdScope>('mine');
  const { data, isPending } = useSavedJds(scope);
  const jds = data?.jds ?? [];

  return (
    <>
      <PageHeader
        back={{ label: 'Back to start', to: '/' }}
        eyebrow="Saved job descriptions"
        sub="Every job description you assemble is saved here — as a Draft until its time adds up to 100%, then Ready."
        title={scope === 'all' ? 'All JDs' : 'My JDs'}
      />

      {isAdmin ? (
        <div aria-label="Whose JDs" className="mb-4 flex gap-2" role="group">
          {(['mine', 'all'] as const).map((s) => (
            <button
              className={`btn btn-sm ${scope === s ? 'btn-primary' : 'btn-ghost'}`}
              key={s}
              onClick={() => setScope(s)}
              type="button"
            >
              {s === 'mine' ? 'Mine' : 'Everyone’s'}
            </button>
          ))}
        </div>
      ) : null}

      {!isPending && jds.length === 0 ? (
        <Card className="p-6">
          <p className="text-[13px] text-base-content/65">
            Nothing saved yet. Pick a class on the start page and build a job description — it
            is saved as soon as you assemble it.
          </p>
        </Card>
      ) : (
        <Card className="divide-y divide-base-300">
          {jds.map((j) => (
            <Link
              className="flex items-center justify-between gap-4 px-5 py-3 hover:bg-base-200"
              key={j.id}
              params={{ id: String(j.id) }}
              to="/jds/$id"
            >
              <div className="min-w-0">
                <div className="truncate text-[14px] font-semibold">
                  {j.workingTitle || j.title}
                </div>
                <div className="text-[12px] text-base-content/65">
                  {j.title} · code <span className="tnum">{j.ucJobCode}</span>
                  {j.department ? ` · ${j.department}` : ''}
                  {scope === 'all' && j.createdBy ? ` · by ${j.createdBy}` : ''}
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-3">
                <span className="text-[12px] text-base-content/50">{when(j.updatedAt)}</span>
                <Badge tone={j.status === 'ready' ? 'green' : 'yellow'}>
                  {j.status === 'ready' ? 'Ready' : `Draft · ${j.unallocatedPct}% unallocated`}
                </Badge>
              </div>
            </Link>
          ))}
        </Card>
      )}
    </>
  );
}
