import { Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { useImportSalaryScale, useSalaryScale } from '@/queries/admin.ts';
import { useState } from 'react';

const dollars = (n: number) => `$${Math.round(n).toLocaleString('en-US')}`;

/**
 * UC Davis's Career Tracks salary ranges, from Compensation's workbook ("Career Tracks Annual Salary
 * Ranges eff …xlsx"). A new workbook replaces the ranges whole. The VRF fills a position's salary
 * range from its grade; represented titles ("STEPS") are on union step scales and aren't covered.
 */
export const SalaryScalePanel = () => {
  const scale = useSalaryScale();
  const importScale = useImportSalaryScale();
  const [open, setOpen] = useState(false);
  const grades = scale.data?.grades ?? [];
  const first = grades[0];
  const last = grades.at(-1);

  return (
    <Card className="mb-5 p-5">
      <Eyebrow>Career Tracks salary ranges</Eyebrow>
      <p className="mt-1 text-base text-base-content/70">
        Compensation&apos;s salary structure workbook (.xlsx): each grade&apos;s minimum, midpoint and maximum. The
        Vacancy Request Form fills a position&apos;s salary range from its grade. Importing a new workbook replaces the
        ranges. Represented titles are on union step scales, which this doesn&apos;t cover.
      </p>
      <p className="mt-1 text-base" data-testid="salary-scale-status">
        {first
          ? `Grades ${last?.grade}–${first.grade}${first.effectiveDate ? `, effective ${new Date(`${first.effectiveDate}T00:00:00`).toLocaleDateString()}` : ''}${scale.data?.importedAt ? `; imported ${new Date(scale.data.importedAt).toLocaleDateString()}` : ''}.`
          : 'No salary ranges imported yet.'}
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <label className="btn btn-outline btn-sm">
          Import salary ranges…
          <input
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            aria-label="Choose a Career Tracks salary ranges workbook"
            className="hidden"
            disabled={importScale.isPending}
            onChange={(e) => {
              const chosen = e.target.files?.[0] ?? null;
              e.target.value = '';
              if (chosen) {
                importScale.mutate(chosen);
              }
            }}
            type="file"
          />
        </label>
        {grades.length > 0 ? (
          <button className="btn btn-ghost btn-sm" onClick={() => setOpen((o) => !o)} type="button">
            {open ? 'Hide the ranges' : 'Show the ranges'}
          </button>
        ) : null}
        {importScale.isPending ? <span className="text-base text-base-content/70">Reading the workbook…</span> : null}
        {importScale.isSuccess ? (
          <span className="text-base text-success-ink" role="status">
            Imported {importScale.data.grades.length} grades.
          </span>
        ) : null}
      </div>
      {importScale.error ? (
        <p className="mt-2 text-base text-error" role="alert">
          {messageOf(importScale.error)}
        </p>
      ) : null}
      {open && grades.length > 0 ? (
        <div aria-label="Career Tracks salary ranges" className="mt-3 overflow-x-auto" role="region" tabIndex={0}>
          <table className="table table-sm" data-testid="salary-scale">
            <thead>
              <tr>
                <th>Grade</th>
                <th className="text-right">Minimum</th>
                <th className="text-right">Midpoint</th>
                <th className="text-right">Maximum</th>
              </tr>
            </thead>
            <tbody>
              {grades.map((g) => (
                <tr key={g.grade}>
                  <td>{g.grade}</td>
                  <td className="text-right tnum">{dollars(g.minimum)}</td>
                  <td className="text-right tnum">{dollars(g.midpoint)}</td>
                  <td className="text-right tnum">{dollars(g.maximum)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </Card>
  );
};
