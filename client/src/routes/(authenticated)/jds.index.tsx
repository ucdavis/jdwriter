import { Badge, Card, PageHeader } from '@/shared/ui/primitives.tsx';
import { useIsAdmin } from '@/shared/ui/AppShell.tsx';
import { type JdScope, savedJdsQueryOptions, useDeleteJd, useSavedJds } from '@/queries/jds.ts';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useState } from 'react';
import type { RouterContext } from '@/main.tsx';
import { asset } from '@/lib/basePath.ts';

export const Route = createFileRoute('/(authenticated)/jds/')({
  component: SavedJdsPage,
  loader: ({ context }: { context: RouterContext }) =>
    context.queryClient.ensureQueryData(savedJdsQueryOptions('mine')),
});

const when = (iso: string) => new Date(iso).toLocaleDateString();

function SavedJdsPage() {
  const isAdmin = useIsAdmin();
  const [scope, setScope] = useState<JdScope>('mine');
  const [order, setOrder] = useState<'newest' | 'oldest'>('newest');
  const { data, isPending } = useSavedJds(scope);
  const remove = useDeleteJd();
  const jds = [...(data?.jds ?? [])].sort((a, b) =>
    order === 'newest'
      ? b.createdAt.localeCompare(a.createdAt)
      : a.createdAt.localeCompare(b.createdAt)
  );

  const confirmDelete = (id: number, name: string) => {
    if (window.confirm(`Delete “${name}”? This can’t be undone.`)) {
      remove.mutate(id);
    }
  };

  return (
    <>
      <PageHeader
        back={{ label: 'Back to start', to: '/' }}
        eyebrow="Saved job descriptions"
        sub="Every job description you assemble is saved here — as a Draft until its time adds up to 100%, then Ready."
        title={scope === 'all' ? 'All JDs' : 'My JDs'}
      />

      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        {isAdmin ? (
          <div aria-label="Whose JDs" className="flex gap-2" role="group">
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
        ) : (
          <span />
        )}
        <label className="flex items-center gap-2 text-sm">
          Created
          <select
            aria-label="Sort by date created"
            className="select select-bordered select-sm"
            onChange={(e) => setOrder(e.target.value === 'oldest' ? 'oldest' : 'newest')}
            value={order}
          >
            <option value="newest">Newest first</option>
            <option value="oldest">Oldest first</option>
          </select>
        </label>
      </div>

      {remove.error ? (
        <p className="mb-3 text-sm text-error">{remove.error.message}</p>
      ) : null}

      {!isPending && jds.length === 0 ? (
        <Card className="flex flex-col items-center p-8 text-center">
          <img alt="" className="mb-4 h-16 w-16 rounded-xl" src={asset('icon-192.png')} />
          <p className="max-w-md text-sm text-base-content/65">
            Nothing saved yet. Pick a class on the start page and build a job description — it
            is saved as soon as you assemble it.
          </p>
        </Card>
      ) : (
        <Card className="divide-y divide-base-300">
          {jds.map((j) => {
            const name = j.workingTitle || j.title;
            return (
              <div className="flex items-center gap-2 pr-3 hover:bg-base-200" key={j.id}>
                <Link
                  className="flex min-w-0 flex-1 items-center justify-between gap-4 px-5 py-3"
                  params={{ id: String(j.id) }}
                  to="/jds/$id"
                >
                  <div className="min-w-0">
                    <div className="truncate text-sm font-semibold">{name}</div>
                    <div className="text-sm text-base-content/65">
                      {j.title} · code <span className="tnum">{j.ucJobCode}</span>
                      {j.department ? ` · ${j.department}` : ''}
                      {scope === 'all' && j.createdBy ? ` · by ${j.createdBy}` : ''}
                    </div>
                  </div>
                  <div className="flex shrink-0 items-center gap-3">
                    <span className="text-sm text-base-content/50">
                      Created {when(j.createdAt)}
                    </span>
                    <Badge tone={j.status === 'ready' ? 'green' : 'yellow'}>
                      {j.status === 'ready'
                        ? 'Ready'
                        : j.assembled
                          ? `Draft · ${j.unallocatedPct}% unallocated`
                          : 'Draft · not assembled'}
                    </Badge>
                  </div>
                </Link>
                <button
                  aria-label={`Delete ${name}`}
                  className="btn btn-ghost btn-xs text-error"
                  disabled={remove.isPending}
                  onClick={() => confirmDelete(j.id, name)}
                  type="button"
                >
                  Delete
                </button>
              </div>
            );
          })}
        </Card>
      )}
    </>
  );
}
