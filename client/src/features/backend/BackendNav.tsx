import { useIsAdmin } from '@/shared/ui/AppShell.tsx';
import { Link } from '@tanstack/react-router';

const tabs = [
  { exact: true, label: 'Envelopes', to: '/backend' },
  { exact: false, label: 'Corpus & standards', to: '/backend/corpus' },
  { exact: false, label: 'Reclassification review', to: '/backend/fit' },
  { exact: false, label: 'Analytics', to: '/backend/analytics' },
  { exact: false, label: 'Settings', to: '/backend/settings' },
] as const;

/**
 * The back end's sections, on every back-end page. Admins only: an Author who follows a
 * stale link gets AdminOnly's explanation from the page, not a menu of places they
 * cannot go.
 */
export const BackendNav = () => {
  const isAdmin = useIsAdmin();
  if (!isAdmin) {
    return null;
  }

  return (
    <nav
      aria-label="Back end"
      className="-mx-1 mb-6 flex gap-1 overflow-x-auto border-b border-base-300"
    >
      {tabs.map((t) => (
        <Link
          activeOptions={{ exact: t.exact }}
          activeProps={{
            'aria-current': 'page',
            className: 'border-primary text-primary',
          }}
          className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-[13px] font-medium text-base-content/65 hover:text-base-content"
          key={t.to}
          to={t.to}
        >
          {t.label}
        </Link>
      ))}
    </nav>
  );
};
