import { handlers } from './handlers.ts';
import { setupWorker } from 'msw/browser';

export const worker = setupWorker(...handlers);
