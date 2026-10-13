import { Badge, Card, Eyebrow, Note } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { useCtListingApply, useCtListingPreview, useCtListingStatus } from '@/queries/admin.ts';
import type { CtListingPlan } from '@/lib/contracts.ts';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

/**
 * The UC Career Tracks title code listing: imported from its PDF report, and trusted over what the
 * app has. It adds the codes the reference lacks, takes its newer grades and families, decides who
 * may supervise by its levels, and moves a standard to the code it gives the standard's exact title.
 * Standards on no code are only suggested, in the match review. A preview shows all of it first.
 */
export const CtListingPanel = () => {
  const status = useCtListingStatus();
  const preview = useCtListingPreview();
  const apply = useCtListingApply();
  const [file, setFile] = useState<File | null>(null);
  const plan = apply.data ?? preview.data;
  const busy = preview.isPending || apply.isPending;

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>CT title code listing</Eyebrow>
      <p className="mt-1 text-base text-base-content/65">
        The UC Career Tracks title code listing report (PDF). It supplies each code&apos;s full title, level,
        family, grade and origin, and is trusted over what the app has: it adds codes the reference lacks,
        updates grades and families, decides who may supervise, and moves a standard to the code it gives
        that standard&apos;s exact title. Standards on no code are suggested in the match review.
      </p>
      <p className="mt-1 text-base" data-testid="ct-status">
        {status.data?.importedAt
          ? `${status.data.codes} codes from the listing, imported ${new Date(status.data.importedAt).toLocaleDateString()}.`
          : 'No listing imported yet.'}
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <label className="btn btn-outline btn-sm">
          Import a listing…
          <input
            accept=".pdf,application/pdf"
            aria-label="Choose a CT title code listing"
            className="hidden"
            disabled={busy}
            onChange={(e) => {
              const chosen = e.target.files?.[0] ?? null;
              e.target.value = '';
              if (chosen) {
                setFile(chosen);
                apply.reset();
                preview.mutate(chosen);
              }
            }}
            type="file"
          />
        </label>
        {preview.isPending ? <span className="text-base text-base-content/65">Reading the report…</span> : null}
      </div>

      {preview.error || apply.error ? (
        <div className="mt-3">
          <Note tone="red">{messageOf(apply.error ?? preview.error)}</Note>
        </div>
      ) : null}

      {plan && file ? (
        <ListingPlan
          busy={apply.isPending}
          onApply={() => apply.mutate(file)}
          onCancel={() => {
            setFile(null);
            preview.reset();
          }}
          plan={plan}
        />
      ) : null}
    </Card>
  );
};

const ListingPlan = ({
  busy,
  onApply,
  onCancel,
  plan,
}: {
  busy: boolean;
  onApply: () => void;
  onCancel: () => void;
  plan: CtListingPlan;
}) => {
  const nothing = plan.newToListing === 0 && plan.changed === 0;
  return (
    <div className="mt-4 text-base" data-testid="ct-plan">
      <p>
        {plan.applied ? 'Imported' : 'Read'} {plan.rows} codes: {plan.newToListing} new to the listing, {plan.changed} changed,{' '}
        {plan.unchanged} unchanged.{' '}
        {plan.applied ? 'Done.' : nothing ? 'Nothing has changed since the last import.' : 'Nothing has changed yet.'}
      </p>
      <ul className="mt-2 list-disc space-y-0.5 pl-5">
        <li>{plan.addedCodeCount} codes added to the reference (not active: they aren&apos;t on payroll)</li>
        <li>
          Grades: {plan.gradesFilled} filled in, {plan.gradesChanged.length} updated · families: {plan.familiesFilled} filled in,{' '}
          {plan.familiesChanged} updated
        </li>
        <li>{plan.supervisionChanges.length} active codes whose level changes who may supervise</li>
        <li>{plan.opOnly} Office of the President codes, kept inactive here</li>
        <li>
          {plan.standardMoves.length} standards {plan.applied ? 'moved' : 'to move'} to the code the listing gives their title ·{' '}
          {plan.standardSuggestions} suggested in the match review
        </li>
      </ul>

      <Details items={plan.standardMoves.map((m) => `${m.standard}: ${m.fromCode} ${m.fromTitle} → ${m.toCode}`)} label="Standards moved" />
      <Details items={plan.supervisionChanges} label="Who may supervise" />
      <Details items={plan.gradesChanged} label="Grades updated" />
      <Details items={plan.addedCodes} label="Codes added" total={plan.addedCodeCount} />

      {plan.affectedClasses.length > 0 ? (
        <div className="mt-3" data-testid="ct-affected">
          <Note tone="yellow">
            {plan.applied ? 'These classes' : 'After importing, these classes'} sit on either side of a moved standard. Rebuild
            or rematch them:
          </Note>
          <ul className="mt-2 space-y-1">
            {plan.affectedClasses.map((c) => (
              <li key={c.slug}>
                <Link className="font-semibold text-primary hover:underline" params={{ slug: c.slug }} to="/backend/edit/$slug">
                  {c.title}
                </Link>{' '}
                <span className="text-sm text-base-content/65">— {c.why}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {!plan.applied && !nothing ? (
        <div className="mt-3 flex items-center gap-2">
          <button className="btn btn-primary btn-sm" disabled={busy} onClick={onApply} type="button">
            {busy ? 'Importing…' : 'Import the listing'}
          </button>
          <button className="btn btn-ghost btn-sm" disabled={busy} onClick={onCancel} type="button">
            Cancel
          </button>
        </div>
      ) : null}
    </div>
  );
};

const Details = ({ items, label, total }: { items: string[]; label: string; total?: number }) =>
  items.length === 0 ? null : (
    <details className="mt-2">
      <summary className="cursor-pointer">
        {label} <Badge tone="muted">{total ?? items.length}</Badge>
      </summary>
      <ul className="mt-1 max-h-48 list-disc overflow-y-auto pl-5 text-sm">
        {items.map((i) => (
          <li key={i}>{i}</li>
        ))}
      </ul>
      {total !== undefined && total > items.length ? (
        <p className="text-sm text-base-content/50">and {total - items.length} more</p>
      ) : null}
    </details>
  );
