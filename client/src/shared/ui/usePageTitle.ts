import { useEffect } from 'react';

const APP = 'JDWriter';

/**
 * The browser tab's title for the page in view (WCAG 2.4.2): what the page is, then the app.
 * It is the first thing a screen reader says on arriving at a page. Null gives the home title.
 */
export const usePageTitle = (title: string | null) => {
  useEffect(() => {
    document.title = title ? `${title} · ${APP}` : `${APP} — UC Davis Job Descriptions`;
  }, [title]);
};
