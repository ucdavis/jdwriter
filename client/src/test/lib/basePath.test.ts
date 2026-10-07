import { afterEach, describe, expect, it, vi } from 'vitest';
import { appUrl, basePath } from '@/lib/basePath.ts';
import { fetchJson } from '@/lib/api.ts';

/** Mount the app as the server does under CAES People: a <base> written into index.html. */
const mountAt = (href: string) => {
  const base = document.createElement('base');
  base.setAttribute('href', href);
  document.head.prepend(base);
};

afterEach(() => {
  document.querySelectorAll('base').forEach((b) => b.remove());
  vi.unstubAllGlobals();
});

describe('mount point', () => {
  it('is the root when no base element is written, as under the Vite dev server', () => {
    expect(basePath()).toBe('');
    expect(appUrl('/api/user/me')).toBe('/api/user/me');
  });

  it('prefixes app paths with the mount point the server wrote', () => {
    mountAt('/jdwriter/');

    expect(basePath()).toBe('/jdwriter');
    expect(appUrl('/api/user/me')).toBe('/jdwriter/api/user/me');
    expect(appUrl('/login?returnUrl=%2Fjdwriter%2Fjds')).toBe(
      '/jdwriter/login?returnUrl=%2Fjdwriter%2Fjds'
    );
  });

  it('leaves absolute and protocol-relative URLs alone', () => {
    mountAt('/jdwriter/');

    expect(appUrl('https://www.ucdavis.edu/')).toBe('https://www.ucdavis.edu/');
    expect(appUrl('//cdn.example.org/x.js')).toBe('//cdn.example.org/x.js');
  });

  it('sends every API call under the mount point', async () => {
    mountAt('/jdwriter/');
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('{}', { headers: { 'Content-Type': 'application/json' } })
    );
    vi.stubGlobal('fetch', fetchMock);

    await fetchJson('/api/classes');

    expect(fetchMock.mock.calls[0][0]).toBe('/jdwriter/api/classes');
  });
});
