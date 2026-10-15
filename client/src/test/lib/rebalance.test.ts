import { rebalance } from '@/features/build/rebalance.ts';
import { describe, expect, it } from 'vitest';

const sum = (xs: number[]) => xs.reduce((a, b) => a + b, 0);

describe('rebalance', () => {
  it('scales up what remains after a function is dropped, keeping proportions', () => {
    // 40 / 20 / 10 kept after dropping a 30% function: 70 → 100.
    expect(rebalance([40, 20, 10])).toEqual([57, 29, 14]);
  });

  it('scales down an over-allocation, keeping proportions', () => {
    expect(rebalance([60, 40, 20])).toEqual([50, 33, 17]);
  });

  it('always totals exactly 100 in whole numbers', () => {
    for (const xs of [[1, 1, 1], [33, 33, 33], [7, 13, 29, 41, 5], [99, 1], [5]]) {
      const out = rebalance(xs);
      expect(sum(out)).toBe(100);
      expect(out.every(Number.isInteger)).toBe(true);
    }
  });

  it('leaves a balanced allocation as it is', () => {
    expect(rebalance([50, 30, 20])).toEqual([50, 30, 20]);
  });

  it('splits evenly when there are no proportions to keep', () => {
    expect(rebalance([0, 0, 0])).toEqual([34, 33, 33]);
  });

  it('keeps a 0% function at 0 while others have time', () => {
    expect(rebalance([0, 30, 10])).toEqual([0, 75, 25]);
  });
});
