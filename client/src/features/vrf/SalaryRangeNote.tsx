import { getSessionState, setSessionState } from '@/lib/sessionState.ts';
import { salaryRangeQueryOptions } from '@/queries/vrf.ts';
import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import type { VrfForm } from './vrfModel.ts';

/** The grade the range was last filled for, with the form: a range the author cleared stays cleared. */
const FILLED_FOR = 'vrf.salaryFilledFor';

const dollars = (n: number) => `$${Math.round(n).toLocaleString('en-US')}`;

const effective = (date: string | null) =>
  date ? `, effective ${new Date(`${date}T00:00:00`).toLocaleDateString('en-US', { dateStyle: 'long' })}` : '';

/** Represented titles are graded "STEPS": their pay is a union step scale, not a Career Tracks range. */
const isSteps = (grade: string) => /steps/i.test(grade);

/**
 * What the salary range is based on. A Career Tracks grade fills the range in from Compensation's
 * structure, once, while the fields are blank — the author can change or clear it, and HR confirms
 * it. A represented title is on its union's step scale, which HR fills in.
 */
export const SalaryRangeNote = ({ form, update }: { form: VrfForm; update: (patch: Partial<VrfForm>) => void }) => {
  const grade = form.salaryGrade;
  const { data } = useQuery(salaryRangeQueryOptions(isSteps(grade) ? '' : grade));
  const range = data?.range ?? null;
  const blank = !form.salaryMin.trim() && !form.salaryMid.trim() && !form.salaryMax.trim();

  useEffect(() => {
    if (range && blank && getSessionState<string | null>(FILLED_FOR, null) !== grade) {
      setSessionState(FILLED_FOR, grade);
      update({ salaryMax: dollars(range.maximum), salaryMid: dollars(range.midpoint), salaryMin: dollars(range.minimum) });
    }
  }, [range, grade, blank, update]);

  if (isSteps(grade)) {
    return (
      <p className="mt-1 text-base text-base-content/70" data-testid="salary-note">
        Represented titles are paid on their union’s step scale — HR fills this in.
      </p>
    );
  }

  if (range) {
    return (
      <p className="mt-1 text-base text-base-content/70" data-testid="salary-note">
        From the Career Tracks ranges for Grade {range.grade}
        {effective(range.effectiveDate)}
        {range.note ? ` (${range.note.replace(/ for non-represented staff$/i, '')})` : ''}. HR confirms the final range.
      </p>
    );
  }

  return (
    <p className="mt-1 text-base text-base-content/70" data-testid="salary-note">
      Optional — HR can fill these in.
    </p>
  );
};
