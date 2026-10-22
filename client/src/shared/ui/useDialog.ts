import { useEffect, useRef } from 'react';

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * What a modal pop-up owes keyboard and screen-reader users (WCAG 2.1.2, 2.4.3): focus moves into
 * it when it opens — to the element marked `data-autofocus`, else its first control — Tab and
 * Shift-Tab stay inside it, Escape closes it, and closing returns focus to whatever opened it.
 * Attach the returned ref to the pop-up's box.
 */
export const useDialog = <T extends HTMLElement>(onClose: () => void) => {
  const ref = useRef<T>(null);
  const close = useRef(onClose);

  useEffect(() => {
    close.current = onClose;
  }, [onClose]);

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    const controls = () => [...(ref.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])];
    const first = ref.current?.querySelector<HTMLElement>('[data-autofocus]') ?? controls()[0] ?? ref.current;
    first?.focus();

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        close.current();
        return;
      }
      if (e.key !== 'Tab') {
        return;
      }
      const all = controls();
      if (all.length === 0) {
        e.preventDefault();
        return;
      }
      const inside = ref.current?.contains(document.activeElement) ?? false;
      if (e.shiftKey && (!inside || document.activeElement === all[0])) {
        e.preventDefault();
        all.at(-1)?.focus();
      } else if (!e.shiftKey && (!inside || document.activeElement === all.at(-1))) {
        e.preventDefault();
        all[0].focus();
      }
    };

    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
      opener?.focus();
    };
  }, []);

  return ref;
};
