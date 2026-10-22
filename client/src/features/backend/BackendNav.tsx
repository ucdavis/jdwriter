import { useIsAdmin } from '@/shared/ui/AppShell.tsx';
import { Link } from '@tanstack/react-router';

const tabs = [
  { exact: true, label: 'Envelopes', to: '/backend' },
  { exact: false, label: 'Codes', to: '/backend/codes' },
  { exact: false, label: 'Standards', to: '/backend/standards' },
  { exact: false, label: 'Descriptions', to: '/backend/descriptions' },
  { exact: false, label: 'Reclassification', to: '/backend/fit' },
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
    // Scrolls sideways on a narrow screen, never up and down. The rule under the tabs is an inset
    // shadow, not a border, so the active tab's underline can cover it without hanging 1px below
    // the bar — an overhang that made the bar scroll vertically.
    <nav
      aria-label="Back end"
      className="-mx-1 mb-6 flex gap-1 overflow-x-auto overflow-y-hidden shadow-[inset_0_-1px_0_var(--color-base-300)]"
    >
      {tabs.map((t) => (
        <Link
          activeOptions={{ exact: t.exact }}
          activeProps={{
            'aria-current': 'page',
            className: 'border-primary text-primary',
          }}
          className="whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-base font-semibold text-base-content/70 hover:text-base-content"
          key={t.to}
          to={t.to}
        >
          {t.label}
        </Link>
      ))}
    </nav>
  );
};
