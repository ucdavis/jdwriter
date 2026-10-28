import { messageOf } from '@/features/browse/NlIntake.tsx';
import { appUrl, asset } from '@/lib/basePath.ts';
import { hasAccess, meQueryOptions, useRequestAccess } from '@/queries/user.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';
import { usePageTitle } from '@/shared/ui/usePageTitle.ts';
import { useQueryClient } from '@tanstack/react-query';
import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

export const Route = createFileRoute('/(authenticated)/request-access')({
  component: RequestAccessPage,
});

/**
 * What someone signed in but not on the allow-list sees, and all they can do: ask. The request goes
 * to the admins' Reports inbox, where one click approves it. Approval takes effect at once, so
 * "Check again" (or any reload) lets them in — nothing polls.
 */
function RequestAccessPage() {
  usePageTitle('Request access');
  const user = useUser();
  const request = useRequestAccess();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [message, setMessage] = useState('');
  const [checked, setChecked] = useState(false);
  const askedAt = user.accessRequestedAt ? new Date(user.accessRequestedAt) : null;

  const checkAgain = async () => {
    const me = await queryClient.fetchQuery({ ...meQueryOptions(), staleTime: 0 });
    if (hasAccess(me)) {
      await navigate({ to: '/' });
    } else {
      setChecked(true);
    }
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-base-200 px-4 py-12" id="main">
      <section className="w-full max-w-xl rounded-xl bg-base-100 p-6 shadow-sm sm:p-8">
        <img alt="" className="mb-4 h-16 w-16 rounded-xl" src={asset('icon-192.png')} />
        <h1 className="text-3xl font-bold">Request access to JDWriter</h1>
        <p className="mt-3 text-base text-base-content/70">
          JDWriter is open to approved users. You&apos;re signed in as{' '}
          <span className="font-semibold text-base-content">{user.name || user.email}</span>
          {user.name && user.email ? ` (${user.email})` : ''}, which isn&apos;t on the list yet.
        </p>

        {askedAt ? (
          <div className="mt-5 rounded-lg border border-base-300 bg-base-200 px-4 py-3" data-testid="access-asked" role="status">
            <p className="text-base font-semibold">
              You asked on {askedAt.toLocaleDateString(undefined, { dateStyle: 'long' })}.
            </p>
            <p className="mt-1 text-base text-base-content/70">
              An administrator will review it. You can use JDWriter as soon as they approve — come back, or check again.
            </p>
            <button className="btn btn-outline btn-sm mt-3" onClick={() => void checkAgain()} type="button">
              Check again
            </button>
            {checked ? (
              <span className="ml-3 text-sm text-base-content/70">Not yet approved.</span>
            ) : null}
          </div>
        ) : null}

        <form
          className="mt-5"
          onSubmit={(e) => {
            e.preventDefault();
            request.mutate(message, { onSuccess: () => setMessage('') });
          }}
        >
          <label className="block">
            <span className="text-base font-semibold">
              {askedAt ? 'Add to your request (optional)' : 'Who you are and what you’ll use it for (optional)'}
            </span>
            <textarea
              className="textarea textarea-bordered mt-1 h-28 w-full text-base"
              maxLength={1000}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="e.g. Department, your role, the positions you're writing"
              value={message}
            />
          </label>
          {request.error ? (
            <p className="mt-2 text-base text-error" role="alert">
              {messageOf(request.error)}
            </p>
          ) : null}
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <button className="btn btn-primary" disabled={request.isPending} type="submit">
              {request.isPending ? 'Sending…' : askedAt ? 'Update my request' : 'Request access'}
            </button>
            <a className="text-base text-primary underline" href={appUrl('/logout')}>
              Sign out
            </a>
          </div>
        </form>
      </section>
    </main>
  );
}
