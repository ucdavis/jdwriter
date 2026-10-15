/**
 * Scale percentages to total exactly 100 in whole numbers, keeping their proportions: a 50/25 split
 * stays two to one. Rounding uses largest remainders, so the parts always add to 100 and none moves
 * further than rounding requires; ties go to the earlier part. When everything is 0 there are no
 * proportions to keep, so it splits 100 evenly.
 */
export const rebalance = (pcts: number[]): number[] => {
  if (pcts.length === 0) {
    return [];
  }

  const clean = pcts.map((p) => (Number.isFinite(p) && p > 0 ? p : 0));
  const total = clean.reduce((sum, p) => sum + p, 0);
  const exact = total === 0 ? clean.map(() => 100 / clean.length) : clean.map((p) => (p * 100) / total);
  const result = exact.map(Math.floor);
  let left = 100 - result.reduce((sum, p) => sum + p, 0);

  const byRemainder = exact
    .map((e, i) => ({ i, remainder: e - Math.floor(e) }))
    .toSorted((a, b) => b.remainder - a.remainder || a.i - b.i);
  for (const { i } of byRemainder) {
    if (left <= 0) {
      break;
    }

    result[i] += 1;
    left -= 1;
  }

  return result;
};
