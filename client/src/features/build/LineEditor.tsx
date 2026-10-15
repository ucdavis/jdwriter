import { Badge } from '@/shared/ui/primitives.tsx';
import { useLayoutEffect, useRef, type ReactNode } from 'react';
import { isEdited, type Item } from './useBuildState.ts';
import type { CheckedChange } from '@/lib/contracts.ts';

const verdictLine = {
  borderline: { label: 'Stretches the class', symbol: '~', tone: 'text-warning' },
  in_envelope: { label: 'Fits the class', symbol: '✓', tone: 'text-success' },
  out_of_envelope: { label: 'Outside the class', symbol: '✕', tone: 'text-error' },
} as const;

/**
 * One line of a JD being built: kept or dropped with its checkbox, and its wording edited in place.
 *
 * The checkbox is about the whole line — unchecked, it is struck through and left out. The text is
 * edited where it stands. A standard line that has been reworded says so, keeps its original wording
 * (shown on hover) and can be put back; the envelope check compares the edit with that original, to
 * see whether the rewording changed the duty. Only lines the author added can be deleted outright: a
 * standard line is dropped, never removed, so the author can always see what the class expects.
 */
export const LineEditor = ({
  children,
  item,
  label,
  onChange,
  onRemove,
  verdict,
}: {
  /** Shown under the line (e.g. which of the author's own lines it covers). */
  children?: ReactNode;
  item: Item;
  /** What the line is, for the checkbox's and text box's accessible names. */
  label: string;
  onChange: (next: Item) => void;
  /** Delete an added line outright. */
  onRemove?: () => void;
  /** How the envelope check judged this line, when it was checked and hasn't changed since. */
  verdict?: CheckedChange;
}) => {
  const edited = isEdited(item);
  return (
    <li className="flex items-start gap-2.5">
      <input
        aria-label={`Include: ${label}`}
        checked={item.kept}
        className="checkbox checkbox-sm mt-1.5"
        onChange={() => onChange({ ...item, kept: !item.kept })}
        type="checkbox"
      />
      <div className="min-w-0 flex-1">
        <div className="flex items-start gap-1.5">
          {item.added ? (
            <span className="mt-1.5">
              <Badge tone="green">+</Badge>
            </span>
          ) : null}
          <GrowingText
            disabled={!item.kept}
            label={`Wording: ${label}`}
            onChange={(text) =>
              onChange({
                ...item,
                // The first edit to a standard line remembers what it said.
                original: item.added ? undefined : (item.original ?? item.text),
                text,
              })
            }
            struck={!item.kept}
            value={item.text}
          />
        </div>
        {edited ? (
          <div className="mt-0.5 flex flex-wrap items-center gap-2 pl-1.5 text-sm text-base-content/60">
            <span title={`Standard wording: ${item.original}`}>
              <Badge tone="yellow">edited</Badge>
            </span>
            <button
              className="text-primary hover:underline"
              onClick={() => onChange({ ...item, original: undefined, text: item.original ?? item.text })}
              type="button"
            >
              Undo edit
            </button>
          </div>
        ) : null}
        {verdict?.verdict ? (
          <p
            className={`mt-0.5 pl-1.5 text-sm ${verdictLine[verdict.verdict].tone}`}
            data-testid="line-verdict"
          >
            <span className="font-semibold">
              {verdictLine[verdict.verdict].symbol} {verdictLine[verdict.verdict].label}
            </span>
            {verdict.reason ? <span className="text-base-content/70"> — {verdict.reason}</span> : null}
          </p>
        ) : null}
        {item.kept && item.text.trim() === '' ? (
          <p className="mt-0.5 pl-1.5 text-sm text-warning">Empty — this line will be left out.</p>
        ) : null}
        {children}
      </div>
      {item.added && onRemove ? (
        <button
          aria-label={`Remove: ${label}`}
          className="mt-1.5 text-base text-base-content/50 hover:text-error"
          onClick={onRemove}
          type="button"
        >
          ✕
        </button>
      ) : null}
    </li>
  );
};

/** A text box that reads like a line of text and grows with what's typed into it. */
const GrowingText = ({
  disabled,
  label,
  onChange,
  struck,
  value,
}: {
  disabled: boolean;
  label: string;
  onChange: (text: string) => void;
  struck: boolean;
  value: string;
}) => {
  const ref = useRef<HTMLTextAreaElement>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (el) {
      el.style.height = 'auto';
      el.style.height = `${el.scrollHeight}px`;
    }
  }, [value]);

  return (
    <textarea
      aria-label={label}
      className={`w-full resize-none overflow-hidden rounded-md border border-transparent bg-transparent px-1.5 py-0.5 text-base leading-snug outline-none hover:border-base-300 focus:border-primary disabled:cursor-default disabled:hover:border-transparent ${
        struck ? 'text-base-content/50 line-through' : ''
      }`}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
      ref={ref}
      rows={1}
      value={value}
    />
  );
};
