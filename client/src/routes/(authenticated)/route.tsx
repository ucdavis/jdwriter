import {
  createFileRoute,
  Outlet,
  redirect,
  type ErrorComponentProps,
} from '@tanstack/react-router';
import { HttpError } from '../../lib/api.ts';
import { RouterContext } from '../../main.tsx';
import { hasAccess, meQueryOptions } from '../../queries/user.ts';
import { AppShell } from '@/shared/ui/AppShell.tsx';
import { UserProvider, useUser } from '@/shared/auth/UserContext.tsx';
import { appUrl, asset } from '@/lib/basePath.ts';

export const Route = createFileRoute('/(authenticated)')({
  // Not on the allow-list: only the page to ask for access, before any page's data is asked for
  // (and refused). On it, someone who has access is sent on to the app.
  beforeLoad: async ({ context, location }: { context: RouterContext; location: { pathname: string } }) => {
    const me = await context.queryClient.ensureQueryData(meQueryOptions());
    const asking = location.pathname === '/request-access';
    if (!hasAccess(me) && !asking) {
      throw redirect({ to: '/request-access' });
    }
    if (hasAccess(me) && asking) {
      throw redirect({ to: '/' });
    }
  },
  component: () => (
    <UserProvider>
      <Frame />
    </UserProvider>
  ),
  errorComponent: AuthenticatedRouteError,
});

/** The app's frame for those with access; the bare request page for those without. */
function Frame() {
  const user = useUser();
  if (!hasAccess(user)) {
    return <Outlet />;
  }

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}

function AuthenticatedRouteError({ error }: ErrorComponentProps<unknown>) {
  if (error instanceof HttpError && error.status === 403) {
    return (
      <main className="min-h-screen flex items-center justify-center px-4 py-12">
        <section className="max-w-lg text-center">
          <img alt="" className="mx-auto mb-5 h-20 w-20 rounded-xl" src={asset('icon-192.png')} />
          <h1 className="text-3xl font-bold text-gray-900">
            Access unavailable
          </h1>
          <p className="mt-4 text-gray-600">
            You are signed in, but your account is not authorized to use this
            application.
          </p>
          <a className="btn btn-primary mt-6" href={appUrl('/login')}>
            Sign in with a different account
          </a>
        </section>
      </main>
    );
  }

  return (
    <main className="min-h-screen flex items-center justify-center px-4 py-12">
      <section className="max-w-lg text-center">
        <img alt="" className="mx-auto mb-5 h-20 w-20 rounded-xl" src={asset('icon-192.png')} />
        <h1 className="text-3xl font-bold text-gray-900">
          We could not load this page
        </h1>
        <p className="mt-4 text-gray-600">
          Refresh the page or try again later.
        </p>
      </section>
    </main>
  );
}
