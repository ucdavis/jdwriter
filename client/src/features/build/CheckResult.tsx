import type { EnvelopeCheckResponse } from '@/lib/contracts.ts';
import { Badge, Card, Note } from '@/shared/ui/primitives.tsx';
import { Link } from '@tanstack/react-router';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { BetterFitPanel } from './BetterFitPanel.tsx';
import type { useBetterFit } from '@/queries/authoring.ts';
import type { ReactNode } from 'react';

const verdictTone = (v: EnvelopeCheckResponse['verdict']) =>
  v === 'in_envelope' ? 'green' : v === 'borderline' ? 'yellow' : 'red';

const verdictLabel = (v: EnvelopeCheckResponse['verdict']) =>
  v === 'in_envelope' ? 'fits this class' : v === 'borderline' ? 'borderline' : 'outside this class';

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
  <Card className={`p-5 ${check.verdict === 'in_envelope' ? 'border-success/40' : 'border-warning/50'}`}>
    <div className="flex items-center justify-between gap-3">
      <h2 className="text-xl font-bold">{heading}</h2>
      <Badge tone={verdictTone(check.verdict)}>{verdictLabel(check.verdict)}</Badge>
    </div>
    <p className="mt-2 text-base">{check.rationale}</p>
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
      {/* Offered whatever the verdict — a build can fit its class and still fit another better —
          unless this check already searched. */}
      {check.betterFitChecked || betterFit.data ? null : (
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
