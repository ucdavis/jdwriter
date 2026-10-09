import { Link } from '@tanstack/react-router';
import { asset } from '@/lib/basePath.ts';
import { AppFooter } from './AppFooter.tsx';
import { useUser } from '@/shared/auth/UserContext.tsx';
import type { ReactNode } from 'react';

import { ADMIN_ROLE } from '@/queries/user.ts';

export { ADMIN_ROLE };

export const useIsAdmin = () => {
  const user = useUser();
  return user.roles.includes(ADMIN_ROLE);
};

/**
 * The app frame: a sticky brand bar over a centred content column.
 *
 * The bar is `ucd-blue` with a `ucd-gold` mark, which is the POC's navy/gold rendered
 * through gunrock rather than through hand-written hex.
 */
export const AppShell = ({ children }: { children: ReactNode }) => {
  const isAdmin = useIsAdmin();

  return (
    // A full-height column, so the footer sits at the bottom of short pages instead of mid-screen.
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-50 flex h-[60px] items-center justify-between bg-ucd-blue px-8">
        <Link className="flex items-center gap-2.5" to="/">
          <img alt="" className="h-10 w-10 rounded-md bg-white object-cover" src={asset('icon-192.png')} />
          <div className="flex flex-col leading-none">
            <span className="text-base font-bold leading-tight text-ucd-gold">
              JDWriter
            </span>
            <span className="text-sm font-semibold tracking-wide text-white/70">
              UC Davis Job Description Studio
            </span>
          </div>
        </Link>
        <nav className="flex items-center gap-4">
          <Link
            className="text-base font-semibold text-white/75 hover:text-ucd-gold"
            to="/classify"
          >
            Classify
          </Link>
          <Link
            className="text-base font-semibold text-white/75 hover:text-ucd-gold"
            to="/jds"
          >
            My JDs
          </Link>
          {isAdmin ? (
            <Link
              className="text-base font-semibold text-white/75 hover:text-ucd-gold"
              to="/backend"
            >
              Back end
            </Link>
          ) : null}
        </nav>
      </header>
      <main className="mx-auto w-full max-w-[1100px] flex-1 px-8 py-8">
        {children}
      </main>
      <AppFooter />
    </div>
  );
};

/**
 * Wraps a curation surface. The server returns 403 regardless; this exists so an
 * author who follows a stale link gets an explanation instead of a failed request.
 */
export const AdminOnly = ({ children }: { children: ReactNode }) => {
  const isAdmin = useIsAdmin();

  if (!isAdmin) {
    return (
      <div className="rounded-xl border border-base-300 bg-base-100 p-6">
        <h2 className="text-xl font-bold">Admin access required</h2>
        <p className="mt-2 text-base text-base-content/65">
          This is the JDWriter back end — envelopes, reclassification review, ingest and
          settings. It is open to JDWriter admins only; ask an admin if you need access.
        </p>
        <Link className="btn btn-primary btn-sm mt-4" to="/">
          Back to start
        </Link>
      </div>
    );
  }

  return <>{children}</>;
};
