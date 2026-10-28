import { messageOf } from '@/features/browse/NlIntake.tsx';
import type { ProblemReport } from '@/lib/contracts.ts';
import { reportsQueryOptions, useApproveAccess, useSetReportStatus, type ReportFilter } from '@/queries/reports.ts';
import { AdminOnly } from '@/shared/ui/AppShell.tsx';
import { Badge, Card, PageHeader } from '@/shared/ui/primitives.tsx';
import { usePageTitle } from '@/shared/ui/usePageTitle.ts';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useState } from 'react';

export const Route = createFileRoute('/(authenticated)/backend/reports')({
  component: ReportsPage,
});

const FILTERS: Array<{ label: string; value: ReportFilter }> = [
  { label: 'Open', value: 'open' },
  { label: 'Resolved', value: 'resolved' },
  { label: 'All', value: 'all' },
];

const tone = { Access: 'purple', Bug: 'red', Question: 'accent', Request: 'yellow' } as const;
const kindLabel = { Access: 'Access request', Bug: 'Something’s wrong', Question: 'Question', Request: 'Request' } as const;

/**
 * JDWriter's support inbox: what people sent from the footer's "Report a problem". The app's own
 * queue — CRU hosts JDWriter but doesn't take its support — worked here: read, follow up by email,
 * note what was done, resolve.
 */
function ReportsPage() {
  usePageTitle('Reports · Back end');
  const [filter, setFilter] = useState<ReportFilter>('open');
  const reports = useQuery(reportsQueryOptions(filter));
  const rows = reports.data?.reports ?? [];

  return (
    <AdminOnly>
      <PageHeader
        eyebrow="Back end · support"
        sub="Bug reports, requests and questions sent from “Report a problem” at the foot of every page, and requests to use JDWriter. They come here, not to CRU’s help desk."
        title="Reports"
      />
      <div aria-label="Show" className="join mb-4" role="group">
        {FILTERS.map((f) => (
          <button
            aria-pressed={filter === f.value}
            className={`btn join-item btn-sm ${filter === f.value ? 'btn-primary' : 'btn-outline'}`}
            key={f.value}
            onClick={() => setFilter(f.value)}
            type="button"
          >
            {f.label}
            {f.value === 'open' && reports.data ? ` (${reports.data.open})` : ''}
          </button>
        ))}
      </div>
      {reports.error ? (
        <p className="text-base text-error" role="alert">
          {messageOf(reports.error)}
        </p>
      ) : null}
      {reports.isSuccess && rows.length === 0 ? (
        <p className="text-base text-base-content/70" data-testid="reports-empty">
          {filter === 'open' ? 'Nothing open. 🎉' : 'No reports here.'}
        </p>
      ) : null}
      <div className="space-y-3" data-testid="reports">
        {rows.map((r) => (
          <ReportCard key={r.id} report={r} />
        ))}
      </div>
    </AdminOnly>
  );
}

const ReportCard = ({ report }: { report: ProblemReport }) => {
  const [note, setNote] = useState(report.note);
  const status = useSetReportStatus();
  const approve = useApproveAccess();
  const asksAccess = report.kind === 'Access' && !report.resolved;
  const error = status.error ?? approve.error;

  return (
    <Card className="p-4">
      <article aria-label={`Report from ${report.from ?? 'unknown'}`}>
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={tone[report.kind]}>{kindLabel[report.kind]}</Badge>
          {report.resolved ? <Badge tone="green">Resolved</Badge> : null}
          <span className="text-base text-base-content/70">
            {new Date(report.createdAt).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })} ·{' '}
            {report.from ?? 'Unknown'}
            {report.fromEmail ? (
              <>
                {' '}
                (<a className="text-primary underline" href={`mailto:${report.fromEmail}?subject=${encodeURIComponent('Your JDWriter report')}`}>{report.fromEmail}</a>)
              </>
            ) : null}{' '}
            · on <span className="font-mono text-sm">{report.page || '—'}</span>
          </span>
        </div>
        <p className="mt-2 whitespace-pre-wrap text-base">{report.message}</p>
        <p className="mt-1 text-sm text-base-content/70">{report.browser}</p>
        <label className="mt-3 block">
          <span className="text-sm font-semibold">Note</span>
          <textarea
            className="textarea textarea-bordered mt-1 h-16 w-full text-base"
            maxLength={2000}
            onChange={(e) => setNote(e.target.value)}
            placeholder="What was done, or why not"
            value={note}
          />
        </label>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          {asksAccess ? (
            <button
              className="btn btn-primary btn-sm"
              disabled={approve.isPending}
              onClick={() => approve.mutate(report.id)}
              type="button"
            >
              {approve.isPending ? 'Approving…' : 'Approve access'}
            </button>
          ) : null}
          <button
            className={`btn btn-sm ${report.resolved || asksAccess ? 'btn-outline' : 'btn-primary'}`}
            disabled={status.isPending}
            onClick={() => status.mutate({ id: report.id, note, resolved: !report.resolved })}
            type="button"
          >
            {report.resolved ? 'Reopen' : asksAccess ? 'Decline' : 'Mark resolved'}
          </button>
          {report.resolved && note !== report.note ? (
            <button
              className="btn btn-ghost btn-sm"
              disabled={status.isPending}
              onClick={() => status.mutate({ id: report.id, note, resolved: true })}
              type="button"
            >
              Save note
            </button>
          ) : null}
          {report.resolvedAt ? (
            <span className="text-sm text-base-content/70">
              Resolved {new Date(report.resolvedAt).toLocaleDateString()}
            </span>
          ) : null}
          {error ? (
            <span className="text-sm text-error" role="alert">
              {messageOf(error)}
            </span>
          ) : null}
        </div>
      </article>
    </Card>
  );
};
