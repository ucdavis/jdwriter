/**
 * Abbreviation-aware UC title matching, for the class search box.
 *
 * Ported from the POC's `src/lib/titles.ts` (which is explicitly client- and
 * server-safe) so filtering ~1,200 in-use titles stays instant as the user types. A
 * server round trip per keystroke would make the list feel broken.
 *
 * DUPLICATION WORTH KNOWING ABOUT: this normalisation now exists three times — here,
 * in the POC, and in C# as `TitleNormalizer`. Only the C# copy is authoritative for
 * resolving job codes; this one exists purely for search. That is why only the LOOSE
 * key is ported: the strict key (which keeps bargaining-unit suffixes and is the one
 * that must never drift) has no client-side use, and leaving it out removes any chance
 * of the client resolving a code itself.
 */

const TITLE_ABBR: Record<string, string> = {
  acad: 'academic', adm: 'administrator', admin: 'administrative', adv: 'advanced',
  anl: 'analyst', anml: 'animal', assoc: 'associate', asst: 'assistant',
  ast: 'assistant', ath: 'athletic', beh: 'behavioral', bus: 'business',
  chf: 'chief', clin: 'clinical', cmty: 'community', cnslr: 'counselor',
  comm: 'communications', coord: 'coordinator', crd: 'coordinator', ctr: 'center',
  dev: 'development', educ: 'education', eng: 'engineer', engr: 'engineer',
  exec: 'executive', ext: 'external', fac: 'facilities', fin: 'financial',
  grad: 'graduate', hosp: 'hospital', hr: 'human resources', info: 'information',
  it: 'information technology', lab: 'laboratory', ld: 'lead', mach: 'machinery',
  mech: 'mechanic', med: 'medical', mgmt: 'management', mgr: 'manager',
  mgt: 'management', mktg: 'marketing', mus: 'museum', ofcr: 'officer',
  opr: 'operator', pat: 'patient', pd: 'per diem', plnr: 'planner',
  pract: 'practitioner', prg: 'program', prgm: 'program', prn: 'principal',
  profl: 'professional', progr: 'programmer', proj: 'project',
  recrmt: 'recruitment', rel: 'relations', repr: 'representative',
  resc: 'resource', rsch: 'research', sci: 'scientist', scrty: 'security',
  secr: 'secretary', spec: 'specialist', sr: 'senior',
  sra: 'staff research associate', stdt: 'student', supp: 'support',
  supv: 'supervisor', svc: 'services', svcs: 'services', sys: 'systems',
  tchl: 'technical', tchn: 'technician', tech: 'technician',
  undergrad: 'undergraduate',
};

/** Bargaining-unit and variant suffixes, dropped when asking "same class?". */
const TITLE_DROP = new Set([
  '99', 'cx', 'ex', 'gf', 'hc', 'me', 'nex', 'pd', 'px', 'rp', 'rx', 'sv', 'sx', 'tx',
]);

const TITLE_CONNECT = new Set(['a', 'an', 'and', 'for', 'in', 'of', 'or', 'the', 'to']);

/** Word families UC spells inconsistently between standards and title codes. */
const TITLE_CANON: Record<string, string> = {
  administration: 'administrator',
  administrative: 'administrator',
  analyses: 'analyst',
  analysis: 'analyst',
  management: 'manager',
  programming: 'programmer',
  supervision: 'supervisor',
};

/** Strip a trailing -s except where it belongs to the stem ("business", "status"). */
const singular = (w: string) =>
  w.length >= 4 && w.endsWith('s') && !/(is|ss|us)$/.test(w) ? w.slice(0, -1) : w;

export const titleWords = (s: string): string[] =>
  s
    .toLowerCase()
    .replaceAll('&', ' and ')
    .replaceAll(/\([^)]*\)/g, ' ')
    .replaceAll(/[^\d a-z]/g, ' ')
    .split(/\s+/)
    .filter(Boolean)
    .filter((w) => !TITLE_CONNECT.has(w))
    .filter((w) => !TITLE_DROP.has(w))
    .flatMap((w) => (TITLE_ABBR[w] ?? w).split(' '))
    .map((w) => TITLE_CANON[w] ?? w)
    .map(singular);

export const titleKey = (s: string): string => titleWords(s).join(' ').trim();

/** Every expanded query word must appear in the expanded title. */
export const titleMatches = (query: string, title: string): boolean => {
  const key = titleKey(title);
  return titleWords(query).every((w) => key.includes(w));
};
