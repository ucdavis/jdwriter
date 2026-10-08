import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { isAdmin } from '@/queries/user.ts';
import { Badge, Card, Eyebrow, Note, PageHeader } from '@/shared/ui/primitives.tsx';
import { createFileRoute, Link } from '@tanstack/react-router';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { misfitsQueryOptions, useMisfits } from '@/queries/classes.ts';
import { useRewriteToFit, useSuggestClass } from '@/queries/authoring.ts';
import type { FitRewriteResponse, Misfit } from '@/lib/contracts.ts';
import type { RouterContext } from '@/main.tsx';

const CAP = 150;

export const Route = createFileRoute('/(authenticated)/backend/fit')({
  component: FitPage,
  loader: async ({ context }: { context: RouterContext }) => {
    if (await isAdmin(context.queryClient)) {
      await context.queryClient.ensureQueryData(misfitsQueryOptions(90));
    }
  },
});

/**
 * The reverse comparison: which real JDs fit their assigned class poorly?
 *
 * These are reclassification candidates. Low coverage is not automatically a wrong
 * class — genuinely idiosyncratic work looks the same from here — which is why each row
 * offers a suggestion rather than a verdict.
 */
function FitPage() {
  return (
    <AdminOnly>
      <FitContent />
    </AdminOnly>
  );
}

// Inside the gate so the admin-only query never fires for an Author.
function FitContent() {
  const { data } = useMisfits(90);
  const misfits = data?.misfits ?? [];
  const shown = misfits.slice(0, CAP);
  const totalJds = data?.totalJds ?? 0;
  const threshold = data?.threshold ?? 90;

  return (
    <>
      <PageHeader
        eyebrow="Back end · goodness of fit"
        sub={`Real JDs whose responsibilities fall below ${threshold}% coverage of their assigned class standard. These are the positions most likely mis-slotted — check which class actually fits.`}
        title="Reclassification review"
      />

      <div className="space-y-4">
        <Card className="p-5">
          <Eyebrow>Summary</Eyebrow>
          <div className="mt-2 flex items-baseline gap-3">
            <span className="text-3xl font-bold tnum">{misfits.length}</span>
            <span className="text-sm text-base-content/65">
              of {totalJds} JDs fit their assigned class below {threshold}% ·{' '}
              {Math.round((1 - misfits.length / (totalJds || 1)) * 100)}% fit well
            </span>
          </div>
          {misfits.length > CAP ? (
            <p className="mt-1 text-xs text-base-content/50">
              Showing the {CAP} worst-fitting.
            </p>
          ) : null}
        </Card>

        <div className="space-y-2">
          {shown.map((m) => (
            <MisfitRow key={`${m.slug}-${m.sourceFile}`} misfit={m} />
          ))}
          {misfits.length === 0 ? (
            <Card className="p-6">
              <p className="text-sm text-base-content/65">
                Every JD fits its class at ≥{threshold}%. No reclassification candidates.
              </p>
            </Card>
          ) : null}
        </div>
      </div>
    </>
  );
}

const MisfitRow = ({ misfit }: { misfit: Misfit }) => {
  const suggest = useSuggestClass();
  const rewrite = useRewriteToFit();
  const matches = suggest.data?.matches;
  const best = matches?.[0];
  const bestIsCurrent = best?.slug === misfit.slug;

  const tone =
    misfit.coveredPct >= 80 ? 'yellow' : misfit.coveredPct >= 60 ? 'orange' : 'red';

  return (
    <Card className="p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <Badge tone={tone}>{misfit.coveredPct}% fit</Badge>
            <Link
              className="truncate text-sm font-medium text-primary hover:underline"
              params={{ _splat: misfit.sourceFile, slug: misfit.slug }}
              to="/backend/jd/$slug/$"
            >
              {misfit.sourceFile.replace(/\.html$/i, '')}
            </Link>
          </div>
          <div className="mt-1 text-sm text-base-content/65">
            Currently: <span className="font-medium text-base-content">{misfit.classTitle}</span>{' '}
            (code <span className="tnum">{misfit.ucJobCode}</span>)
          </div>
          {misfit.idiosyncratic.length > 0 ? (
            <div className="mt-1 text-xs text-base-content/65">
              Idiosyncratic work:{' '}
              {misfit.idiosyncratic.map((u) => `${u.name} (${u.pct}%)`).join(', ')}
            </div>
          ) : null}
        </div>
        <div className="flex shrink-0 gap-1.5">
          <button
            className="btn btn-outline btn-xs"
            disabled={suggest.isPending}
            onClick={() =>
              suggest.mutate({ slug: misfit.slug, sourceFile: misfit.sourceFile })
            }
            type="button"
          >
            {suggest.isPending ? 'Matching…' : 'Suggest class'}
          </button>
          <button
            className="btn btn-outline btn-xs"
            disabled={rewrite.isPending}
            onClick={() =>
              rewrite.mutate({ slug: misfit.slug, sourceFile: misfit.sourceFile })
            }
            title={`Draft a new JD for ${misfit.classTitle} from this one`}
            type="button"
          >
            {rewrite.isPending && !rewrite.variables?.targetSlug
              ? 'Rewriting…'
              : 'Rewrite to fit'}
          </button>
        </div>
      </div>

      {suggest.error ? (
        <div className="mt-2">
          <Note tone="red">{messageOf(suggest.error)}</Note>
        </div>
      ) : null}
      {rewrite.error ? (
        <div className="mt-2">
          <Note tone="red">{messageOf(rewrite.error)}</Note>
        </div>
      ) : null}
      {rewrite.data ? <RewriteResult result={rewrite.data} /> : null}

      {matches ? (
        <div className="mt-3 rounded-lg border border-base-300 bg-base-200 p-3">
          {bestIsCurrent ? (
            <div className="text-sm text-base-content/65">
              Best fit is still{' '}
              <span className="font-medium text-base-content">{misfit.classTitle}</span> —
              the low coverage is genuinely idiosyncratic work, not a wrong class.
            </div>
          ) : best ? (
            <div className="text-sm">
              Better match:{' '}
              <Link
                className="font-semibold text-primary hover:underline"
                params={{ slug: best.slug }}
                to="/class/$slug"
              >
                {best.title}
              </Link>{' '}
              <Badge tone="green">{best.confidence}%</Badge>
              <button
                className="btn btn-primary btn-xs ml-2"
                disabled={rewrite.isPending}
                onClick={() =>
                  rewrite.mutate({
                    slug: misfit.slug,
                    sourceFile: misfit.sourceFile,
                    targetSlug: best.slug,
                  })
                }
                type="button"
              >
                {rewrite.isPending && rewrite.variables?.targetSlug === best.slug
                  ? 'Rewriting…'
                  : `Rewrite to fit ${best.title}`}
              </button>
              <div className="mt-1 text-sm text-base-content/65">{best.rationale}</div>
            </div>
          ) : null}
          {matches.length > 1 ? (
            <div className="mt-2 text-xs text-base-content/50">
              Also:{' '}
              {matches
                .slice(1, 3)
                .map((x) => `${x.title} (${x.confidence}%)`)
                .join(' · ')}
            </div>
          ) : null}
        </div>
      ) : null}
    </Card>
  );
};

/**
 * A rewrite is a draft, not a JD: the analyst opens it in the build screen, where the
 * carried-over duties go through the envelope check and assembly like any other.
 */
const RewriteResult = ({ result }: { result: FitRewriteResponse }) => (
  <div className="mt-3 rounded-lg border border-success/30 bg-success/5 p-3 text-sm">
    <div>
      Draft written for <span className="font-semibold">{result.title}</span>: {result.keptFunctions}{' '}
      function{result.keptFunctions === 1 ? '' : 's'} kept, {result.carriedDuties} of the
      incumbent&apos;s duties carried over, {result.matchedDuties} matched to standard duties.{' '}
      <Link
        className="font-semibold text-primary hover:underline"
        params={{ slug: result.slug }}
        search={{ draft: result.authoredJdId }}
        to="/class/$slug"
      >
        Open draft →
      </Link>
    </div>
    {result.outside.length > 0 ? (
      <div className="mt-2 text-sm text-base-content/65">
        Left out — outside this class:
        <ul className="mt-1 list-disc pl-5">
          {result.outside.map((o) => (
            <li key={o.text}>
              {o.text}
              {o.pct === null ? '' : ` (${o.pct}%)`} — {o.reason}
            </li>
          ))}
        </ul>
      </div>
    ) : null}
  </div>
);
