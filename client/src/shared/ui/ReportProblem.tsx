import { messageOf } from '@/features/browse/NlIntake.tsx';
import type { ReportKind } from '@/lib/contracts.ts';
import { useSendReport } from '@/queries/reports.ts';
import { useDialog } from '@/shared/ui/useDialog.ts';
import { useLocation } from '@tanstack/react-router';
import { useState } from 'react';

const KINDS: Array<{ hint: string; kind: ReportKind; label: string }> = [
  { hint: 'Something didn’t work as it should', kind: 'Bug', label: 'Something’s wrong' },
  { hint: 'A change or a new feature', kind: 'Request', label: 'A request' },
  { hint: 'How to do something', kind: 'Question', label: 'A question' },
];

/**
 * The footer's "Report a problem": a short form that goes to JDWriter's own admins (Back end →
 * Reports), not CRU's ticket system — CRU hosts the app but doesn't take its support. The page it
 * was sent from goes with it, so the report says where; who sent it and their browser are added on
 * the server.
 */
export const ReportProblem = () => {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button className="underline hover:text-primary" onClick={() => setOpen(true)} type="button">
        Report a problem
      </button>
      {open ? <ReportDialog onClose={() => setOpen(false)} /> : null}
    </>
  );
};

const ReportDialog = ({ onClose }: { onClose: () => void }) => {
  const box = useDialog<HTMLDivElement>(onClose);
  const page = useLocation().pathname;
  const send = useSendReport();
  const [kind, setKind] = useState<ReportKind>('Bug');
  const [message, setMessage] = useState('');

  return (
    <div aria-labelledby="report-title" aria-modal className="modal modal-open text-left" role="dialog">
      <div className="modal-box max-w-xl" ref={box}>
        <h2 className="text-xl font-bold" id="report-title">
          Report a problem
        </h2>
        {send.isSuccess ? (
          <>
            <p className="mt-3 text-base" role="status">
              Thanks — your report went to the JDWriter team. They&apos;ll follow up by email if they need more.
            </p>
            <div className="modal-action">
              <button className="btn btn-primary btn-sm" data-autofocus onClick={onClose} type="button">
                Close
              </button>
            </div>
          </>
        ) : (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              send.mutate({ kind, message, page });
            }}
          >
            <p className="mt-1 text-base text-base-content/70">
              It goes to the JDWriter team, with the page you&apos;re on. Please leave out anything you wouldn&apos;t
              put in an email.
            </p>
            <fieldset className="mt-4">
              <legend className="text-base font-semibold">What is it?</legend>
              <div className="mt-1 flex flex-wrap gap-x-5 gap-y-1">
                {KINDS.map((k) => (
                  <label className="flex items-center gap-2 text-base" key={k.kind} title={k.hint}>
                    <input
                      checked={kind === k.kind}
                      className="radio radio-sm"
                      name="report-kind"
                      onChange={() => setKind(k.kind)}
                      type="radio"
                    />
                    {k.label}
                  </label>
                ))}
              </div>
            </fieldset>
            <label className="mt-4 block">
              <span className="text-base font-semibold">What happened, or what do you need?</span>
              <textarea
                aria-required
                className="textarea textarea-bordered mt-1 h-36 w-full text-base"
                data-autofocus
                maxLength={4000}
                onChange={(e) => setMessage(e.target.value)}
                value={message}
              />
            </label>
            <p className="mt-1 text-sm text-base-content/70">
              Sent with it: this page (<span className="font-mono">{page}</span>), your name and your browser.
            </p>
            {send.error ? (
              <p className="mt-2 text-base text-error" role="alert">
                {messageOf(send.error)}
              </p>
            ) : null}
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={onClose} type="button">
                Cancel
              </button>
              <button className="btn btn-primary btn-sm" disabled={send.isPending || !message.trim()} type="submit">
                {send.isPending ? 'Sending…' : 'Send report'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
};
