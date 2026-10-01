import { useMemo, useState } from 'react';
import type { BuildRequest, KeyResponsibility } from '@/lib/contracts.ts';

/**
 * One selectable line. The editor is AFFIRMATIVE/SUBTRACTIVE by design: the author keeps
 * or drops what the standard offers and may add a few unit-specific items. It is not
 * free-text editing, because the point of the envelope is that most positions in a class
 * need under 10% customisation — and free text makes drift invisible.
 */
export type Item = { added: boolean; kept: boolean; text: string };

export type RespState = {
  draft: string;
  duties: Item[];
  functionKept: boolean;
  functionName: string;
  pctTime: number;
};

const toItems = (xs: string[]): Item[] =>
  xs.map((text) => ({ added: false, kept: true, text }));

const norm = (s: string) =>
  s.toLowerCase().replaceAll(/[^\d a-z]/g, '').replaceAll(/\s+/g, ' ').trim();

export type EnvelopeSections = {
  certs: string[];
  education: string[];
  minKSA: string[];
  prefKSA: string[];
  responsibilities: KeyResponsibility[];
  workEnvironment: string[];
  workExperience: string[];
};

export const useBuildState = (envelope: EnvelopeSections) => {
  const [workingTitle, setWorkingTitle] = useState('');
  const [department, setDepartment] = useState('');
  const [notes, setNotes] = useState('');
  const [dupNote, setDupNote] = useState<string | null>(null);

  const [resps, setResps] = useState<RespState[]>(() =>
    envelope.responsibilities.map((r) => ({
      draft: '',
      duties: toItems(r.duties),
      functionKept: true,
      functionName: r.functionName,
      pctTime: r.pctTime,
    }))
  );
  const [certs, setCerts] = useState<Item[]>(() => toItems(envelope.certs));
  const [education, setEducation] = useState<Item[]>(() => toItems(envelope.education));
  const [workExp, setWorkExp] = useState<Item[]>(() => toItems(envelope.workExperience));
  const [minKSA, setMinKSA] = useState<Item[]>(() => toItems(envelope.minKSA));
  const [prefKSA, setPrefKSA] = useState<Item[]>(() => toItems(envelope.prefKSA));
  const [workEnv, setWorkEnv] = useState<Item[]>(() =>
    toItems(envelope.workEnvironment)
  );

  const sections = [certs, education, workExp, minKSA, prefKSA, workEnv];

  /**
   * Redundancy guard. Duplicate requirements across sections are the most common way a
   * tailored JD becomes bloated, and they are hard to spot by eye once a section is long,
   * so an addition that restates an existing item is refused with the conflict named.
   */
  const findDuplicate = (text: string): string | null => {
    const n = norm(text);
    if (n.length < 6) {
      return null;
    }
    const existing = [
      ...resps.flatMap((r) => r.duties.map((d) => d.text)),
      ...sections.flatMap((s) => s.map((i) => i.text)),
    ];
    for (const t of existing) {
      const m = norm(t);
      if (m === n || (m.length >= 6 && (m.includes(n) || n.includes(m)))) {
        return t;
      }
    }
    return null;
  };

  const guardAdd = (text: string): boolean => {
    const dup = findDuplicate(text);
    if (dup) {
      setDupNote(
        `"${text.trim()}" is already covered by: "${dup}". Redundant items are removed.`
      );
      return false;
    }
    setDupNote(null);
    return true;
  };

  const patchResp = (index: number, patch: Partial<RespState>) =>
    setResps((rs) => rs.map((r, i) => (i === index ? { ...r, ...patch } : r)));

  const totalPct = resps
    .filter((r) => r.functionKept)
    .reduce((sum, r) => sum + (Number(r.pctTime) || 0), 0);

  /**
   * Customisation meter. Counts ADDITIONS only — removals do not count, because dropping
   * a standard duty is the envelope working as intended, while adding one is drift away
   * from it. The target is ≤10%.
   */
  const addedItems = useMemo(
    () => [
      ...resps
        .filter((r) => r.functionKept)
        .flatMap((r) => r.duties.filter((d) => d.kept && d.added).map((d) => d.text)),
      ...sections.flatMap((s) =>
        s.filter((d) => d.kept && d.added).map((d) => d.text)
      ),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [resps, certs, education, workExp, minKSA, prefKSA, workEnv]
  );

  const envelopeTotal =
    envelope.responsibilities.reduce((sum, r) => sum + r.duties.length, 0) +
    envelope.certs.length +
    envelope.education.length +
    envelope.workExperience.length +
    envelope.minKSA.length +
    envelope.prefKSA.length +
    envelope.workEnvironment.length;

  const customPct = envelopeTotal
    ? Math.round((addedItems.length / envelopeTotal) * 100)
    : 0;

  const keptOf = (s: Item[]) => s.filter((d) => d.kept).map((d) => d.text);

  const buildRequest = (slug: string): BuildRequest => ({
    addedItems,
    department,
    keptCerts: keptOf(certs),
    keptEducation: keptOf(education),
    keptMinKSA: keptOf(minKSA),
    keptPrefKSA: keptOf(prefKSA),
    keptResponsibilities: resps
      .filter((r) => r.functionKept)
      .map((r) => ({
        duties: r.duties.filter((d) => d.kept).map((d) => d.text),
        functionName: r.functionName,
        pctTime: Number(r.pctTime) || 0,
      }))
      // A function with every duty dropped is not a responsibility any more.
      .filter((r) => r.duties.length > 0),
    keptWorkEnvironment: keptOf(workEnv),
    keptWorkExperience: keptOf(workExp),
    notes,
    slug,
    workingTitle,
  });

  return {
    addedCount: addedItems.length,
    buildRequest,
    certs,
    customPct,
    department,
    dupNote,
    education,
    guardAdd,
    minKSA,
    notes,
    patchResp,
    prefKSA,
    resps,
    setCerts,
    setDepartment,
    setEducation,
    setMinKSA,
    setNotes,
    setPrefKSA,
    setResps,
    setWorkEnv,
    setWorkExp,
    setWorkingTitle,
    totalPct,
    workEnv,
    workExp,
    workingTitle,
  };
};
