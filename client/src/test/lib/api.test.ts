import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchJson, HttpError } from '@/lib/api.ts';

afterEach(() => vi.unstubAllGlobals());

describe('fetchJson', () => {
  it.each([
    { 'X-Request-Id': 'example' },
    [['X-Request-Id', 'example']],
    new Headers({ 'X-Request-Id': 'example' }),
  ] satisfies HeadersInit[])(
    'keeps JSON defaults when custom headers are supplied: %j',
    async (headers) => {
      const fetchMock = vi.fn().mockResolvedValue(
        new Response('{}', {
          headers: { 'Content-Type': 'application/json' },
        })
      );
      vi.stubGlobal('fetch', fetchMock);

      await fetchJson('/api/example', { body: '{}', headers, method: 'POST' });

      const request = fetchMock.mock.calls[0][1] as RequestInit;
      const sentHeaders = new Headers(request.headers);
      expect(sentHeaders.get('Accept')).toBe('application/json');
      expect(sentHeaders.get('Content-Type')).toBe('application/json');
      expect(sentHeaders.get('X-Request-Id')).toBe('example');
      expect(request.credentials).toBe('same-origin');
    }
  );

  it('marks every call as coming from JDWriter, which the server requires of state changes', async () => {
    // A cross-site page cannot add this header without a CORS preflight, which is never granted,
    // so its presence is what distinguishes a real call from a forged one.
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('{}', { headers: { 'Content-Type': 'application/json' } })
    );
    vi.stubGlobal('fetch', fetchMock);

    await fetchJson('/api/admin/supersessions/retire', { method: 'POST' });

    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(request.headers).get('X-Requested-With')).toBe('JDWriter');
  });

  it('leaves a FormData body for the browser to label as multipart', async () => {
    // Forcing application/json onto a file upload strips the multipart boundary, and the
    // server then cannot read the file at all.
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('{}', { headers: { 'Content-Type': 'application/json' } })
    );
    vi.stubGlobal('fetch', fetchMock);
    const body = new FormData();
    body.append('file', new Blob(['x']), 'a.html');

    await fetchJson('/api/upload', { body, method: 'POST' });

    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(request.headers).has('Content-Type')).toBe(false);
    expect(request.body).toBe(body);
  });

  it('honors explicit header overrides and preserves cancellation', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    await fetchJson('/api/example', {
      body: 'text',
      headers: { accept: 'text/plain', 'content-type': 'text/plain' },
      method: 'POST',
      signal: controller.signal,
      skipRedirectOn401: true,
    });

    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(request.headers).get('Accept')).toBe('text/plain');
    expect(new Headers(request.headers).get('Content-Type')).toBe('text/plain');
    expect(request.signal).toBe(controller.signal);
    expect(request).not.toHaveProperty('skipRedirectOn401');
  });

  it('throws an HTTP error when a caller opts out of login redirection', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 }))
    );

    await expect(
      fetchJson('/api/example', { skipRedirectOn401: true })
    ).rejects.toBeInstanceOf(HttpError);
  });
});
