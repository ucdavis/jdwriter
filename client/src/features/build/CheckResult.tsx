import type { CheckedChange, EnvelopeCheckResponse } from '@/lib/contracts.ts';
import { Badge, Card, Note } from '@/shared/ui/primitives.tsx';
import { Link } from '@tanstack/react-router';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { BetterFitPanel } from './BetterFitPanel.tsx';
import type { useBetterFit } from '@/queries/authoring.ts';
import type { ReactNode } from 'react';

const mark = {
  borderline: { label: 'stretches the class', symbol: '~', tone: 'text-warning' },
  in_envelope: { label: 'fits', symbol: '✓', tone: 'text-success' },
  out_of_envelope: { label: 'outside the class', symbol: '✕', tone: 'text-error' },
} as const;

/**
 * Each addition and edit, in the author's own words, with the check's reason for it: the standard
 * duty or scope it falls within, or what takes it outside. An edit shows what it replaced.
 */
const ChangeList = ({ changes }: { changes: CheckedChange[] }) => (
  <ul className="mt-3 space-y-2" data-testid="checked-changes">
    {changes.map((c, i) => {
      const m = c.verdict ? mark[c.verdict] : null;
      return (
        <li className="flex gap-2 text-base" key={`${c.kind}-${i}`}>
          <span aria-label={m?.label} className={`w-4 shrink-0 font-bold ${m?.tone ?? 'text-base-content/40'}`}>
            {m?.symbol ?? '•'}
          </span>
          <span className="min-w-0">
            <span className="text-base-content/65">{c.kind === 'edited' ? 'Edited: ' : 'Added: '}</span>
            <span className="font-semibold">{c.text}</span>
            {c.original ? (
              <span className="block text-sm text-base-content/55">was: {c.original}</span>
            ) : null}
            {c.reason ? <span className="block text-base-content/75">{c.reason}</span> : null}
          </span>
        </li>
      );
    })}
  </ul>
);

/** Borderline still fits the class — it stretches it — so the author sees one of two answers. */
const fits = (v: EnvelopeCheckResponse['verdict']) => v !== 'out_of_envelope';

/**
 * The result of an envelope check, as a card the author can act on: the verdict and why, any
 * better-fitting class, and — when the work has left the class — a way to switch to the class it
 * now belongs to. Children carry the step's own actions.
 */
export const CheckResult = ({
  betterFit,
  check,
  children,
  currentTitle,
  heading,
  onLookForBetterFit,
}: {
  betterFit: ReturnType<typeof useBetterFit>;
  check: EnvelopeCheckResponse;
  children?: ReactNode;
  currentTitle: string;
  heading: string;
  onLookForBetterFit: () => void;
}) => (
  <Card className={`p-5 ${fits(check.verdict) ? 'border-success/40' : 'border-warning/50'}`}>
    <div className="flex items-center justify-between gap-3">
      <h2 className="text-xl font-bold">{heading}</h2>
      <Badge tone={fits(check.verdict) ? 'green' : 'red'}>
        {fits(check.verdict) ? 'Fits class' : 'Doesn’t fit class'}
      </Badge>
    </div>
    <p className="mt-2 text-base">{check.rationale}</p>
    {check.changes && check.changes.length > 0 ? <ChangeList changes={check.changes} /> : null}
    {check.matchedSignals.length > 0 ? (
      <ul className="mt-3 space-y-1">
        {check.matchedSignals.map((s) => (
          <li className="flex gap-2 text-base text-base-content/65" key={s}>
            <span className="text-warning">⚠</span>
            <span>{s}</span>
          </li>
        ))}
      </ul>
    ) : null}
    {check.verdict === 'borderline' && check.betterFitChecked ? (
      <BetterFitPanel
        currentTitle={currentTitle}
        rationale={check.suggestedRationale}
        suggestedClass={check.suggestedClass}
        suggestedSlug={check.suggestedSlug}
      />
    ) : null}
    {betterFit.data ? (
      <BetterFitPanel
        currentTitle={currentTitle}
        rationale={betterFit.data.rationale}
        suggestedClass={betterFit.data.suggestedClass}
        suggestedSlug={betterFit.data.suggestedSlug}
      />
    ) : null}
    {betterFit.error ? (
      <div className="mt-3">
        <Note tone="red">{messageOf(betterFit.error)}</Note>
      </div>
    ) : null}
    {check.verdict === 'out_of_envelope' && check.suggestedClass ? (
      <div className="mt-4 rounded-lg border border-primary/20 bg-primary/10 p-3.5">
        <div className="text-base">
          Your additions look more like{' '}
          <span className="font-semibold text-primary">{check.suggestedClass}</span>.
        </div>
        {check.suggestedSlug ? (
          <Link
            className="btn btn-primary btn-sm mt-2.5"
            params={{ slug: check.suggestedSlug }}
            to="/class/$slug"
          >
            Switch to {check.suggestedClass} →
          </Link>
        ) : null}
      </div>
    ) : null}
    <div className="mt-4 flex flex-wrap items-center gap-3">
      {children}
      {/* Only when the build doesn't fit and no better class was named: offered on a build that
          fits, it reads as doubt about a passing check. */}
      {fits(check.verdict) || check.suggestedClass || check.betterFitChecked || betterFit.data ? null : (
        <button
          className="btn btn-outline btn-sm"
          disabled={betterFit.isPending}
          onClick={onLookForBetterFit}
          type="button"
        >
          {betterFit.isPending ? 'Looking…' : 'Look for a better fit'}
        </button>
      )}
    </div>
  </Card>
);
