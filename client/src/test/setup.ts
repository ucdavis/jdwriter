import '@testing-library/jest-dom/vitest';

import { afterEach } from 'vitest';
import { clearSessionState } from '@/lib/sessionState.ts';

// Page state lives for the life of the app, which in tests is the whole file: each test starts clean.
afterEach(() => clearSessionState(''));
