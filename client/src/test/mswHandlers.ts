import { handlers } from '@/mocks/handlers.ts';
import { testServer } from './mswUtils.ts';

/**
 * Installs the same handlers the dev app uses, so a test exercises the contract rather
 * than a second, divergent set of fakes. Individual tests still override with
 * `testServer.use(...)` where they need a specific response.
 */
export const useContractHandlers = () => {
  testServer.use(...handlers);
};
