import { Link } from '@tanstack/react-router';
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
    <>
      <header className="sticky top-0 z-50 flex h-[60px] items-center justify-between bg-ucd-blue px-8">
        <Link className="flex items-center gap-2.5" to="/">
          <div className="flex h-10 w-10 items-center justify-center rounded-md bg-ucd-gold text-lg font-bold text-ucd-blue">
            JD
          </div>
          <div className="flex flex-col leading-none">
            <span className="text-base font-bold leading-tight text-ucd-gold">
              JDWriter
            </span>
            <span className="text-[9.5px] font-medium tracking-wide text-white/70">
              UC Davis Job Description Studio
            </span>
          </div>
        </Link>
        <nav className="flex items-center gap-4">
          <Link
            className="text-[12px] font-medium text-white/75 hover:text-ucd-gold"
            to="/classify"
          >
            Classify
          </Link>
          <Link
            className="text-[12px] font-medium text-white/75 hover:text-ucd-gold"
            to="/jds"
          >
            My JDs
          </Link>
          {isAdmin ? (
            <Link
              className="text-[12px] font-medium text-white/75 hover:text-ucd-gold"
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
    </>
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
        <h2 className="text-[17px] font-bold">Admin access required</h2>
        <p className="mt-2 text-[13px] text-base-content/65">
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
