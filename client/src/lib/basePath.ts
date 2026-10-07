/**
 * Where this app is mounted: '' at the root of its host, '/jdwriter' under CAES People
 * (people.caes.ucdavis.edu/jdwriter).
 *
 * The server writes the mount point into index.html as <base href>, so one build serves any
 * path; the Vite dev server writes none and the app runs at the root. Read from the <base>
 * element rather than document.baseURI, which without one is the current page's URL.
 */
export const basePath = (): string =>
  (document.querySelector('base')?.getAttribute('href') ?? '/').replace(/\/+$/, '');

/**
 * An app-relative path ('/api/x', '/login') as the browser must request it. Anything not
 * starting with '/' — an absolute URL, or one already relative — is returned unchanged.
 */
export const appUrl = (path: string): string =>
  path.startsWith('/') && !path.startsWith('//') ? `${basePath()}${path}` : path;
