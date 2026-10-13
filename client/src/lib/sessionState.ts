import { useCallback, useSyncExternalStore } from 'react';

/**
 * Page state that outlives the page: a search, a request, its results. Kept in memory for as long as
 * the app is open, so leaving a page to look at a suggestion and coming back finds everything as it
 * was. A reload starts fresh — nothing is written to the browser — and each page can clear its own.
 */
const values = new Map<string, unknown>();
const listeners = new Set<() => void>();

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
};

const emit = () => {
  for (const listener of listeners) {
    listener();
  }
};

/** Set a value from anywhere — including after the page that asked for it has gone. */
export const setSessionState = <T,>(key: string, value: T) => {
  values.set(key, value);
  emit();
};

/** Forget every value whose key starts with the prefix: a page's "Clear". */
export const clearSessionState = (prefix: string) => {
  for (const key of values.keys()) {
    if (key.startsWith(prefix)) {
      values.delete(key);
    }
  }

  emit();
};

/** Like useState, but kept across pages for the life of the app. */
export const useSessionState = <T,>(key: string, initial: T): [T, (value: T) => void] => {
  const value = useSyncExternalStore(
    subscribe,
    () => (values.has(key) ? (values.get(key) as T) : initial),
    () => initial
  );
  const set = useCallback((next: T) => setSessionState(key, next), [key]);
  return [value, set];
};
