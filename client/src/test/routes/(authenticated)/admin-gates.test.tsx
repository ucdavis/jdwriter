import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { testServer } from '@/test/mswUtils.ts';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

/**
 * An Author who opens an admin page must see AdminOnly's explanation, and the page
 * must not request the admin-only data at all.
 *
 * The failure this guards against: a loader that prefetches an admin-only endpoint for
 * everyone throws the 403 out of the loader, which surfaces the app-wide "not authorized
 * to use this application" page — telling an authorized Author they cannot use the app.
 */
const ADMIN_ONLY = [
  '/api/classes/summary',
  '/api/fit/misfits',
  '/api/classes/:slug/coverage',
  '/api/classes/:slug/jds/*',
];

const pages = [
  { name: 'envelope index', path: '/backend' },
  { name: 'reclassification review', path: '/backend/fit' },
  { name: 'backwards coverage', path: '/backend/jds/009605-lab-ast-1' },
  { name: 'JD review', path: '/backend/jd/009605-lab-ast-1/Sample%20Class/JD-001.HTML' },
];

describe('admin gates', () => {
  setupRouteTest();

  it.each(pages)('$name: an Author sees the access message, and nothing is fetched', async ({ path }) => {
    const requested: string[] = [];
    testServer.use(
      http.get('/api/user/me', () =>
        HttpResponse.json({
          email: 'author@example.edu',
          iamId: null,
          id: 'author',
          name: 'Author Only',
          roles: ['Author'],
        })
      ),
      ...ADMIN_ONLY.map((route) =>
        http.get(route, ({ request }) => {
          requested.push(new URL(request.url).pathname);
          return HttpResponse.json({ message: 'Forbidden' }, { status: 403 });
        })
      )
    );

    renderRoute({ initialPath: path });

    await waitFor(() => {
      expect(screen.getByText('Admin access required')).toBeInTheDocument();
    });
    expect(screen.queryByText('Access unavailable')).not.toBeInTheDocument();
    expect(requested).toEqual([]);
  });
});
