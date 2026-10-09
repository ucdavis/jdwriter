import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';

/**
 * Walter's card: a white surface on a light ground, hairline border, soft shadow.
 * Expressed with DaisyUI roles rather than the POC's raw hex so gunrock owns the palette.
 */
export const Card = ({
  children,
  className = '',
}: {
  children: ReactNode;
  className?: string;
}) => (
  <div
    className={`jd-surface rounded-xl border border-base-300 bg-base-100 shadow-[0_1px_3px_rgba(0,0,0,0.04)] ${className}`}
  >
    {children}
  </div>
);

/**
 * Status tones. Each maps to a DaisyUI semantic role so a palette change lands in one
 * place, and each is a foreground/soft-background pair — the signature Walter accent.
 */
export type Tone =
  | 'accent'
  | 'green'
  | 'yellow'
  | 'orange'
  | 'red'
  | 'purple'
  | 'teal'
  | 'muted';

const toneClass: Record<Tone, string> = {
  accent: 'text-primary bg-primary/10',
  green: 'text-success bg-success/10',
  // DaisyUI has one warning role; the POC distinguished yellow from orange by
  // severity, so orange borrows error at low opacity to stay visibly hotter.
  muted: 'text-base-content/60 bg-base-300/60',
  orange: 'text-error bg-error/10',
  purple: 'text-secondary bg-secondary/15',
  red: 'text-error bg-error/10',
  teal: 'text-info bg-info/10',
  yellow: 'text-warning bg-warning/10',
};

export const Badge = ({
  children,
  tone = 'muted',
}: {
  children: ReactNode;
  tone?: Tone;
}) => (
  <span
    className={`inline-flex items-center rounded-full px-2.5 py-[3px] text-sm font-semibold ${toneClass[tone]}`}
  >
    {children}
  </span>
);

export const Eyebrow = ({ children }: { children: ReactNode }) => (
  <div className="eyebrow">{children}</div>
);

/** An inline message. Errors and warnings are the only two the POC used. */
export const Note = ({
  children,
  tone,
}: {
  children: ReactNode;
  tone: 'red' | 'yellow' | 'green';
}) => {
  const cls =
    tone === 'red'
      ? 'bg-error/10 border-error/20 text-error'
      : tone === 'green'
        ? 'bg-success/10 border-success/20 text-success'
        : 'bg-warning/10 border-warning/20 text-warning';
  return (
    <div className={`rounded-lg border px-3.5 py-2.5 text-base ${cls}`}>
      {children}
    </div>
  );
};

/**
 * Eyebrow + bold title page header, with an optional back link. Sits below the brand
 * bar rather than inside it, as in the POC.
 */
export const PageHeader = ({
  back,
  eyebrow,
  sub,
  title,
}: {
  back?: { label: string; params?: Record<string, string>; to: string; };
  eyebrow?: string;
  sub?: string;
  title: string;
}) => (
  <div className="mb-7">
    {back ? (
      <Link
        className="text-base font-semibold text-primary hover:underline"
        params={back.params}
        to={back.to}
      >
        ← {back.label}
      </Link>
    ) : null}
    {eyebrow ? <div className="eyebrow mt-2">{eyebrow}</div> : null}
    <h1 className="mt-1 text-3xl font-bold tracking-tight">{title}</h1>
    {sub ? (
      <p className="mt-1.5 max-w-2xl text-base text-base-content/65">{sub}</p>
    ) : null}
  </div>
);

/** A labelled statistic. Values are tabular so columns of them line up. */
export const Stat = ({ label, value }: { label: string; value: string }) => (
  <div>
    <Eyebrow>{label}</Eyebrow>
    <div className="mt-0.5 text-base font-semibold tnum">{value}</div>
  </div>
);

/**
 * A consensus fact: a value plus how much of the corpus agreed on it. The agreement
 * figure is shown only when it is less than total, because "100% agreement" is noise.
 */
export const Fact = ({
  agreement,
  label,
  value,
}: {
  agreement: number;
  label: string;
  value: string;
}) => (
  <div>
    <Eyebrow>{label}</Eyebrow>
    <div className="mt-0.5 text-base font-semibold">{value}</div>
    {agreement < 1 ? (
      <div className="mt-0.5 text-sm text-base-content/50 tnum">
        {Math.round(agreement * 100)}% agreement
      </div>
    ) : null}
  </div>
);

/** A labelled text input. */
export const Field = ({
  label,
  onChange,
  placeholder,
  required = false,
  value,
}: {
  label: string;
  onChange: (v: string) => void;
  placeholder?: string;
  /** Marked in the label and on the input; the caller decides what it blocks. */
  required?: boolean;
  value: string;
}) => (
  <label className="block">
    <span className="eyebrow">
      {label}
      {required ? (
        <span aria-hidden className="text-error">
          {' '}*
        </span>
      ) : null}
    </span>
    <input
      aria-required={required || undefined}
      className="input input-bordered mt-1 w-full text-base"
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      value={value}
    />
  </label>
);

/**
 * Add-an-item row. Enter submits, because these are used in long checklists where
 * reaching for the button every time is friction.
 */
export const AddRow = ({
  onAdd,
  onChange,
  placeholder,
  value,
}: {
  onAdd: () => void;
  onChange: (v: string) => void;
  placeholder: string;
  value: string;
}) => (
  <div className="mt-2 flex gap-2">
    <input
      className="input input-bordered input-sm flex-1 text-base"
      onChange={(e) => onChange(e.target.value)}
      onKeyDown={(e) => {
        if (e.key === 'Enter') {
          e.preventDefault();
          onAdd();
        }
      }}
      placeholder={placeholder}
      value={value}
    />
    <button className="btn btn-outline btn-sm" onClick={onAdd} type="button">
      Add
    </button>
  </div>
);

/** A horizontal progress meter. Tone shifts with the value, as in the POC. */
export const Meter = ({ pct }: { pct: number }) => {
  const clamped = Math.min(100, Math.max(0, pct));
  const bar =
    clamped >= 80 ? 'bg-success' : clamped >= 50 ? 'bg-warning' : 'bg-error';
  return (
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-base-300">
      <div className={`h-full rounded-full ${bar}`} style={{ width: `${clamped}%` }} />
    </div>
  );
};
