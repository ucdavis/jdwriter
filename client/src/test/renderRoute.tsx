import { cleanup } from '@testing-library/react';
import { handlers } from '@/mocks/handlers.ts';
import { renderRoute as baseRenderRoute, type RenderRouteOptions } from './routerUtils.tsx';
import { testServer } from './mswUtils.ts';
import { afterEach, beforeEach } from 'vitest';

/**
 * Renders a route with the contract handlers installed and guaranteed teardown.
 *
 * Two things this fixes.
 *
 * Testing Library's automatic cleanup only runs when Vitest globals are enabled. This
 * project imports `describe`/`it` explicitly, so it is NOT running — which meant a
 * rendered tree survived into the next test. That is worse than a plain failure: a query
 * can match an element left over from a previous test and the suite passes for the wrong
 * reason. Cleanup is registered here so no test has to remember it, and so it still runs
 * when an assertion throws mid-test.
 *
 * And handlers come from the same module the dev app uses, so tests exercise the real
 * contract rather than a second, quietly divergent set of fakes.
 */
export const setupRouteTest = () => {
  beforeEach(() => {
    testServer.use(...handlers);
  });

  afterEach(() => {
    cleanup();
  });
};

export const renderRoute = (options: RenderRouteOptions = {}) =>
  baseRenderRoute(options);
