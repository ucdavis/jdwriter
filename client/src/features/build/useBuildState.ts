import { useMemo, useState } from 'react';
import type { BuildRequest, KeyResponsibility } from '@/lib/contracts.ts';

/**
 * One line. The author keeps or drops what the standard offers, may reword a line, and may add a
 * few unit-specific items. A reworded standard line keeps its original wording and is marked as
 * edited; it isn't counted as something new — the envelope check compares it with the line it
 * replaced, to see whether the rewording changed the duty.
 */
export type Item = {
  added: boolean;
  kept: boolean;
  /**
   * Lines from the author's own document that this standard item covers — set when a JD is
   * started from a description or rewritten to fit — so they can see where their wording went.
   */
  matchedFrom?: string[];
  /** A standard line's wording before the author edited it; absent until the first edit. */
  original?: string;
  text: string;
};

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

/**
 * A standard line whose wording has changed. Case, punctuation and spacing don't count — the server
 * reads a line the same way when it decides what is new.
 */
export const isEdited = (item: Item) =>
  !item.added && item.original !== undefined && norm(item.text) !== norm(item.original);

const live = (item: Item) => item.kept && item.text.trim() !== '';

export type EnvelopeSections = {
  certs: string[];
  education: string[];
  minKSA: string[];
  prefKSA: string[];
  responsibilities: KeyResponsibility[];
  workEnvironment: string[];
  workExperience: string[];
};

/**
 * The whole build screen as plain data: what "Save draft" stores and "Continue editing"
 * restores. Versioned so a future change to the screen can recognise older drafts.
 */
export type DraftState = {
  certs: Item[];
  department: string;
  education: Item[];
  /** Added after version 1 shipped; absent in older drafts, which take the role's defaults. */
  leads?: YesNo;
  minKSA: Item[];
  notes: string;
  prefKSA: Item[];
  resps: RespState[];
  supervises?: YesNo;
  supervisesCount?: string;
  version: 1;
  workEnv: Item[];
  workExp: Item[];
  workingTitle: string;
};

export type YesNo = 'no' | 'yes';

/**
 * What the class implies about supervision. Only a supervisor or manager class that isn't
 * union-represented may supervise; `cannotSupervise` says why not otherwise, and locks it at No.
 * A supervisor or manager class starts at Supervises: Yes; every role starts at Leads: No — a
 * supervisor may also lead, but rarely does — until the author changes it.
 */
export type RoleDefaults = { cannotSupervise: string | null; supervisory: boolean };

/** A saved draft this hook can resume from; anything else starts from the envelope. */
export const asDraftState = (value: unknown): DraftState | null =>
  value && typeof value === 'object' && (value as { version?: unknown }).version === 1
    ? (value as DraftState)
    : null;

export const useBuildState = (
  envelope: EnvelopeSections,
  initial?: DraftState | null,
  role: RoleDefaults = { cannotSupervise: null, supervisory: false }
) => {
  const [supervisesChoice, setSupervises] = useState<YesNo>(
    initial?.supervises ?? (role.supervisory && !role.cannotSupervise ? 'yes' : 'no')
  );
  const [leads, setLeads] = useState<YesNo>(initial?.leads ?? 'no');
  const [supervisesCount, setSupervisesCount] = useState(initial?.supervisesCount ?? '');
  // A class that can't supervise doesn't, whatever a draft or default says.
  const supervises: YesNo = role.cannotSupervise ? 'no' : supervisesChoice;
  const count = Number.parseInt(supervisesCount, 10);
  const supervisionReady =
    supervises === 'no' || (Number.isInteger(count) && count >= 1 && count <= 10_000);

  const [workingTitle, setWorkingTitle] = useState(initial?.workingTitle ?? '');
  const [department, setDepartment] = useState(initial?.department ?? '');
  const [notes, setNotes] = useState(initial?.notes ?? '');

  const [resps, setResps] = useState<RespState[]>(
    () =>
      initial?.resps ??
      envelope.responsibilities.map((r) => ({
        draft: '',
        duties: toItems(r.duties),
        functionKept: true,
        functionName: r.functionName,
        pctTime: r.pctTime,
      }))
  );
  const [certs, setCerts] = useState<Item[]>(() => initial?.certs ?? toItems(envelope.certs));
  const [education, setEducation] = useState<Item[]>(
    () => initial?.education ?? toItems(envelope.education)
  );
  const [workExp, setWorkExp] = useState<Item[]>(
    () => initial?.workExp ?? toItems(envelope.workExperience)
  );
  const [minKSA, setMinKSA] = useState<Item[]>(() => initial?.minKSA ?? toItems(envelope.minKSA));
  const [prefKSA, setPrefKSA] = useState<Item[]>(
    () => initial?.prefKSA ?? toItems(envelope.prefKSA)
  );
  const [workEnv, setWorkEnv] = useState<Item[]>(
    () => initial?.workEnv ?? toItems(envelope.workEnvironment)
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

  /**
   * Why an addition is refused, or null when it may be added. Returned rather than stored, so the
   * editor shows it right under the box the author typed in — not at the top of a long page.
   */
  const guardAdd = (text: string): string | null => {
    const dup = findDuplicate(text);
    return dup
      ? `You added “${text.trim()}”, and it already fits within “${dup}”, so it was not included.`
      : null;
  };

  const patchResp = (index: number, patch: Partial<RespState>) =>
    setResps((rs) => rs.map((r, i) => (i === index ? { ...r, ...patch } : r)));

  /** Kept responsibilities with no duty left under them: each needs at least one, or must be dropped. */
  const emptyFunctions = resps.filter((r) => r.functionKept && !r.duties.some(live)).map((r) => r.functionName);

  const totalPct = resps
    .filter((r) => r.functionKept)
    .reduce((sum, r) => sum + (Number(r.pctTime) || 0), 0);

  /**
   * Customisation meter. Counts ADDITIONS only — removals do not count, because dropping
   * a standard duty is the envelope working as intended, and neither do edits, which the
   * envelope check compares with the line they replaced. The target is ≤10% (≤30% for senior
   * and supervisory classes).
   */
  const addedItems = useMemo(
    () => [
      ...resps
        .filter((r) => r.functionKept)
        .flatMap((r) => r.duties.filter((d) => live(d) && d.added).map((d) => d.text.trim())),
      ...sections.flatMap((s) => s.filter((d) => live(d) && d.added).map((d) => d.text.trim())),
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

  /** Reworded standard lines, each with what it replaced, for the envelope check to compare. */
  const editedItems = useMemo(
    () =>
      [
        ...resps.filter((r) => r.functionKept).flatMap((r) => r.duties),
        ...sections.flat(),
      ]
        .filter((d) => live(d) && isEdited(d))
        .map((d) => ({ original: d.original ?? '', text: d.text.trim() })),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [resps, certs, education, workExp, minKSA, prefKSA, workEnv]
  );

  const customPct = envelopeTotal
    ? Math.round((addedItems.length / envelopeTotal) * 100)
    : 0;

  // A line edited down to nothing is left out.
  const keptOf = (s: Item[]) => s.filter((d) => d.kept && d.text.trim() !== '').map((d) => d.text.trim());

  const buildRequest = (slug: string): BuildRequest => ({
    addedItems,
    department,
    editedItems,
    keptCerts: keptOf(certs),
    keptEducation: keptOf(education),
    keptMinKSA: keptOf(minKSA),
    keptPrefKSA: keptOf(prefKSA),
    keptResponsibilities: resps
      .filter((r) => r.functionKept)
      .map((r) => ({
        duties: keptOf(r.duties),
        functionName: r.functionName,
        pctTime: Number(r.pctTime) || 0,
      }))
      // A function with every duty dropped is not a responsibility any more.
      .filter((r) => r.duties.length > 0),
    keptWorkEnvironment: keptOf(workEnv),
    keptWorkExperience: keptOf(workExp),
    leads: leads === 'yes',
    notes,
    slug,
    supervises: supervises === 'yes',
    supervisesCount: supervises === 'yes' && Number.isInteger(count) ? count : null,
    workingTitle,
  });

  const snapshot = (): DraftState => ({
    certs,
    department,
    education,
    leads,
    minKSA,
    notes,
    prefKSA,
    resps,
    supervises,
    supervisesCount,
    version: 1,
    workEnv,
    workExp,
    workingTitle,
  });

  /**
   * Everything the envelope check reads: the additions, the notes, and the position details. Two
   * builds with the same key get the same verdict, so the requirements step skips a second check
   * when nothing it reads has changed since the duties check.
   */
  const checkKey = JSON.stringify({ addedItems, department, editedItems, notes, workingTitle });

  return {
    addedCount: addedItems.length,
    buildRequest,
    certs,
    checkKey,
    customPct,
    department,
    editedCount: editedItems.length,
    education,
    emptyFunctions,
    guardAdd,
    leads,
    minKSA,
    notes,
    patchResp,
    prefKSA,
    resps,
    setCerts,
    setDepartment,
    setEducation,
    setLeads,
    setMinKSA,
    setNotes,
    setPrefKSA,
    setResps,
    setSupervises,
    setSupervisesCount,
    setWorkEnv,
    setWorkExp,
    setWorkingTitle,
    snapshot,
    supervises,
    supervisesCount,
    supervisionReady,
    totalPct,
    workEnv,
    workExp,
    workingTitle,
  };
};
