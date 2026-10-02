import { Badge, Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import { useSavedJds } from '@/queries/jds.ts';
import { Link } from '@tanstack/react-router';

/** The signed-in user's three most recently created JDs, for picking up where they left off. */
export const RecentJds = () => {
  const { data, isPending } = useSavedJds('mine');
  const recent = [...(data?.jds ?? [])]
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    .slice(0, 3);

  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <Eyebrow>Your recent JDs</Eyebrow>
        {recent.length > 0 ? (
          <Link className="text-[12px] font-medium text-primary hover:underline" to="/jds">
            All my JDs →
          </Link>
        ) : null}
      </div>
      {isPending ? null : recent.length === 0 ? (
        <p className="mt-2 text-[13px] text-base-content/65">
          Job descriptions you build will appear here.
        </p>
      ) : (
        <ul className="mt-2 divide-y divide-base-300">
          {recent.map((j) => (
            <li key={j.id}>
              <Link
                className="flex items-center justify-between gap-3 py-2 hover:text-primary"
                params={{ id: String(j.id) }}
                to="/jds/$id"
              >
                <span className="min-w-0">
                  <span className="block truncate text-[13.5px] font-medium">
                    {j.workingTitle || j.title}
                  </span>
                  <span className="text-[11.5px] text-base-content/65">
                    {j.title} · {new Date(j.createdAt).toLocaleDateString()}
                  </span>
                </span>
                <Badge tone={j.status === 'ready' ? 'green' : 'yellow'}>
                  {j.status === 'ready' ? 'Ready' : 'Draft'}
                </Badge>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
};
