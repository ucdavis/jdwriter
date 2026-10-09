/**
 * Wire types for every endpoint in docs/API-CONTRACT.md.
 *
 * Kept in one file and imported by both the query hooks and the MSW handlers, so a
 * mock that drifts from the contract fails to compile rather than passing quietly.
 *
 * Three shapes here deliberately DIVERGE from that document because the POC's UI
 * cannot be driven by what it specifies. Each is marked CONTRACT GAP with the reason;
 * they are reported upward rather than silently absorbed.
 */

// ---------------------------------------------------------------- shared

export type ClassMatch = {
  confidence: number;
  rationale: string;
  slug: string;
  title: string;
  ucJobCode: string;
};

export type KeyResponsibility = {
  duties: string[];
  functionName: string;
  pctTime: number;
};

export type JobEnvelope = {
  conditionsOfEmployment: string[];
  education: string[];
  keyResponsibilities: KeyResponsibility[];
  minQualifications: string[];
  outOfEnvelope: string[];
  physicalRequirements: string[];
  prefQualifications: string[];
  requiredCertifications: string[];
  scopeStatement: string;
  summary: string;
  workEnvironment: string[];
  workExperience: string[];
};

/** A value observed across a class's corpus, with how much of it agreed. */
export type Distribution = {
  agreement: number;
  consensus: string | null;
};

export type ClassStandard = {
  customScope: string;
  education: string[];
  flsa: string;
  genericScope: string;
  grade: string;
  keyResponsibilities: string[];
  ksa: string[];
  licenses: string[];
  longTitle: string;
  persProg: string;
  specialConditions: string[];
  union: string;
};

// ---------------------------------------------------------------- reads

export type ClassListItem = {
  bargainingUnit?: string;
  corpusSize?: number;
  family?: string;
  grade?: string;
  /** False for an in-use UC Davis title with no ingested profile yet. */
  ready: boolean;
  slug: string;
  title: string;
  ucJobCode: string;
};

export type ClassListResponse = { classes: ClassListItem[] };

export type EnvelopeSource = 'claude' | 'deterministic' | 'manual' | 'standard';

/** Curation statistics for one ingested class — the analyst index. */
export type ClassSummary = {
  /** Zero means consolidation has not run for this class. */
  consolidatedFunctions: number;
  corpusSize: number;
  /** Null when no backwards-coverage report exists — distinct from 0% coverage. */
  coverageN: number | null;
  ctJobFamily: string;
  ctJobFunction: string;
  /** Reported as stored. Anything but 100 is the signal that a class needs attention. */
  envelopePctTotal: number;
  envelopeResponsibilities: number;
  envelopeSource: EnvelopeSource | null;
  grade: string | null;
  hasEnvelope: boolean;
  /** Minimum qualifications: consolidated when present, else raw. */
  ksas: number;
  meanCoverage: number | null;
  personnelProgram: string;
  /** Responsibility categories: consolidated when present, else raw functions. */
  responsibilities: number;
  slug: string;
  standardLinked: boolean;
  title: string;
  ucJobCode: string;
  /** Share of JDs at 90% coverage or better, 0..1. */
  wellCoveredPct: number | null;
};

export type ClassSummaryResponse = { classes: ClassSummary[] };

export type ClassProfileResponse = {
  corpusSize: number;
  ctJobFamily: string;
  ctJobFunction: string;
  envelope: JobEnvelope | null;
  envelopeSource: EnvelopeSource | null;
  flsaStatus: Distribution;
  leads: Distribution;
  personnelProgram: string;
  salaryGrade: Distribution;
  slug: string;
  /** Null for roughly 46 of 65 classes. A normal state, not an error. */
  standard: ClassStandard | null;
  supervises: Distribution;
  title: string;
  ucJobCode: string;
  unionCode: Distribution;
  worksOutdoorsOver50pct: Distribution;
};

export type JdCoverage = {
  coveredPct: number;
  sourceFile: string;
  uncovered: Array<{ name: string; pct: number }>;
};

export type CoverageReport = {
  meanCoverage: number;
  n: number;
  perJd: JdCoverage[];
  wellCoveredPct: number;
};

export type JdListItem = {
  coveredPct: number | null;
  departmentName: string;
  sourceFile: string;
  workingTitle: string;
};

export type JdDetailResponse = {
  envelope: JobEnvelope | null;
  profile: { slug: string; title: string; ucJobCode: string };
  record: {
    departmentName: string;
    flsaStatus: string;
    jobSummary: string;
    leads: boolean | null;
    qualifications: {
      education: string;
      ksaMin: string[];
      ksaPref: string[];
      licenses: string[];
      minExperience: string[];
    };
    responsibilities: Array<{
      duties: string[];
      functionName: string;
      /** Marked server-side against the class's CONSOLIDATED members. */
      inEnvelope: boolean;
      pct: number | null;
    }>;
    salaryGrade: string;
    sourceFile: string;
    supervises: boolean | null;
    ucJobCode: string;
    ucJobTitle: string;
    unionCode: string;
    workingTitle: string;
  };
};

// ---------------------------------------------------------------- intake

export type IntakeRequest = { request: string };
export type IntakeResponse = { matches: ClassMatch[] };

// ---------------------------------------------------------------- extraction

export type DocKind = 'docx' | 'html' | 'pdf' | 'text' | 'xlsx';

export type ExtractResponse = {
  filename: string;
  kind: DocKind;
  note?: string | null;
  /** The classification the FORM proposes. Never sent into the ranking prompt. */
  proposed?: { code: string; ingested: boolean; title: string } | null;
  text: string;
};

// ---------------------------------------------------------------- classify
//
// CONTRACT GAP 1. docs/API-CONTRACT.md models the response as a single `verdict`
// object plus `alternatives`. The POC's UI needs materially more and cannot be driven
// by that shape:
//   • up to 4 `matches`, EACH with its own level judgement, coverage percentage and
//     in/out function lists — the alternatives are compared on the same axes, not
//     merely listed;
//   • a top-level verdict TAXONOMY (clear | close-call | level-mismatch | weak) with a
//     note, which is what the page leads with;
//   • `distilled`, which tells the user how their document was read (and whether the
//     free deterministic HRTMS path was used);
//   • `basis` on the proposed assessment, so the UI can say the judgement came from an
//     official standard rather than a corpus envelope.
// Implemented to the POC's shape because that is the specification for the screen.

export type LevelFit = 'above' | 'at' | 'below' | 'unclear';

export type ClassifyMatch = {
  confidence: number;
  coveredPct: number;
  inClass: string[];
  levelFit: LevelFit;
  levelNote: string;
  outOfClass: string[];
  rationale: string;
  slug: string;
  title: string;
  ucJobCode: string;
};

export type ProposedAssessment = {
  /** Envelope when the class has a corpus, else its official standard, else none. */
  basis: 'envelope' | 'none' | 'standard';
  code: string;
  /** Set when the proposed code is superseded and was assessed as its successor. */
  comparedAs?: string;
  contradicts: Array<{ evidence: string; point: string }>;
  fits: 'no' | 'partly' | 'yes';
  missing: string[];
  summary: string;
  supports: string[];
  title: string;
};

export type ClassifyVerdict = 'clear' | 'close-call' | 'level-mismatch' | 'weak';

export type ClassifyRequest = {
  description: string;
  /** Sent SEPARATELY from the description so it cannot leak into the ranking prompt. */
  proposedCode?: string | null;
};

/**
 * How the description was read: its work as functions with % time and duties. Sent back
 * unchanged to start a JD from a recommended class, so the document is not read twice.
 */
export type DistilledJd = {
  education: string[];
  experience: string[];
  functions: Array<{ duties: string[]; name: string; pctTime: number }>;
  ksas: string[];
  source: 'hrtms' | 'text';
  summary: string;
  supervises: 'no' | 'unclear' | 'yes';
  workingTitle: string;
};

export type ClassifyStartRequest = { distilled: DistilledJd; slug: string };

export type ClassifyResponse = {
  distilled: DistilledJd;
  /** Job code the submission was saved to the corpus under; null when not filed. */
  filedUnder?: string | null;
  matches: ClassifyMatch[];
  proposed?: ProposedAssessment;
  verdict: ClassifyVerdict;
  verdictNote: string;
};

// ---------------------------------------------------------------- build
//
// CONTRACT GAP 2. docs/API-CONTRACT.md specifies the request as
// `{ slug, workingTitle, department, keptDutyIds: number[], addedDuties: string[] }`.
// Identifiers cannot express what the build screen actually produces: the author edits
// function NAMES inline, adjusts each `pctTime` to reach 100, drops whole functions, and
// keeps/drops items across six separate qualification sections. Sending ids would
// discard every one of those edits. Implemented to the POC's shape, which sends the
// resolved text.

export type BuildRequest = {
  addedItems: string[];
  /** The saved JD this build session already wrote; re-assembling updates it. */
  authoredJdId?: number | null;
  department: string;
  /** The build screen as left, so the JD can be reopened and continued. */
  draftState?: unknown;
  keptCerts: string[];
  keptEducation: string[];
  keptMinKSA: string[];
  keptPrefKSA: string[];
  keptResponsibilities: KeyResponsibility[];
  keptWorkEnvironment: string[];
  keptWorkExperience: string[];
  notes: string;
  slug: string;
  workingTitle: string;
};

export type EnvelopeCheckResponse = {
  /** A borderline verdict also searches for a better fit; with no suggestion, the class is still best. */
  betterFitChecked: boolean;
  matchedSignals: string[];
  rationale: string;
  suggestedClass: string;
  /** Why the suggested class fits better (borderline only). */
  suggestedRationale: string;
  suggestedSlug: string;
  verdict: 'borderline' | 'in_envelope' | 'out_of_envelope';
};

/** "Look for a better fit": an empty suggestion means the current class is still the best match. */
export type BetterFitResponse = {
  rationale: string;
  suggestedClass: string;
  suggestedSlug: string;
};

export type ComplianceEdit = {
  after: string;
  before: string;
  reason: string;
  section: string;
  source: 'llm' | 'rule';
};

export type JdStatus = 'draft' | 'ready';

export type AssembledJd = {
  /** Every assembly is saved; this is the record it was written to. */
  authoredJdId: number | null;
  // The class attributes travel at the top level, beside the draft, as the server sends them.
  bargainingUnit: string | null;
  /** True exactly when `unallocatedPct` is 0 — the only way a saved JD can be Ready. */
  canPublish: boolean;
  complianceEdits: ComplianceEdit[];
  /** Why the JD was or was not added to its class's corpus. */
  corpusNote: string;
  department: string;
  flsaStatus: string | null;
  /** Assembled straight from the envelope because nothing changed — no model call. */
  fromEnvelope: boolean;
  /** Whether a copy went into the class's corpus at this final stage. */
  inCorpus: boolean;
  jd: {
    conditionsOfEmployment: string[];
    education: string[];
    jobSummary: string;
    keyResponsibilities: KeyResponsibility[];
    licensesCertifications: string[];
    minKSA: string[];
    physicalRequirements: string[];
    prefKSA: string[];
    workEnvironment: string[];
    workExperience: string[];
  };
  salaryGrade: string | null;
  slug: string;
  /** Draft until the time totals exactly 100%. */
  status: JdStatus;
  title: string;
  ucJobCode: string;
  /**
   * Percent of time not accounted for by the kept responsibilities. Non-zero means the JD
   * is NOT publishable.
   *
   * This exists because dropping a standard responsibility carries the remaining
   * percentages through verbatim, so a tailored JD can total less than 100. The shortfall
   * is neither published silently nor rescaled away: rescaling would quietly turn a kept
   * 50% into 71% with nobody told the number moved, which is exactly the kind of invisible
   * edit the compliance trail exists to prevent. The author decides where freed time goes.
   */
  unallocatedPct: number;
  workingTitle: string;
};

// ---------------------------------------------------------------- envelope editing

export type EnvelopeSaveRequest = { envelope: JobEnvelope; slug: string };
export type EnvelopeSaveResponse = { ok: true; slug: string };

/**
 * CONTRACT GAP 3a. The contract specifies `{ slug }`. The editor must check the
 * UNSAVED envelope currently on screen — checking the saved one defeats the purpose,
 * since the whole point is to see whether an edit degrades fit BEFORE committing it.
 */
export type CoverageCheckRequest = { envelope: JobEnvelope; slug: string };

// ---------------------------------------------------------------- fit review
//
// CONTRACT GAP 3b. The contract has `POST /api/fit/suggest` returning
// `{ suggestions: [{kind, detail, rationale}] }`, and no endpoint at all for the
// cross-class misfit list the review page is built on. The POC's suggest call takes
// `{ slug, sourceFile }` — a suggestion is about one JD, not one class — and returns
// ranked ClassMatches, which is what lets the page say "this JD would fit X better".

export type Misfit = {
  classTitle: string;
  coveredPct: number;
  idiosyncratic: Array<{ name: string; pct: number }>;
  slug: string;
  sourceFile: string;
  ucJobCode: string;
};

export type MisfitsResponse = {
  misfits: Misfit[];
  threshold: number;
  totalJds: number;
};

export type FitSuggestRequest = { slug: string; sourceFile: string };
export type FitSuggestResponse = { matches: ClassMatch[] };

/** Rewrite a misfit into a class: its own when targetSlug is omitted. */
export type FitRewriteRequest = { slug: string; sourceFile: string; targetSlug?: string };

/** The rewrite is saved as a draft; open it at /class/{slug}?draft={authoredJdId}. */
export type FitRewriteResponse = {
  authoredJdId: number;
  carriedDuties: number;
  droppedFunctions: number;
  keptFunctions: number;
  /** Source duties matched to a standard duty; shown under it in the build screen. */
  matchedDuties: number;
  /** Incumbent work the rewrite left out because it does not belong in the class. */
  outside: Array<{ pct: number | null; reason: string; text: string }>;
  slug: string;
  title: string;
};

// ---------------------------------------------------------------- admin

export type PendingClass = {
  code: string;
  fileCount: number;
  /** The class exists only as a starter envelope from its standard; ingesting replaces it. */
  replacesStarter: boolean;
  slug: string;
  title: string;
};

/** `configured: false` — no export directory on this server (normal when deployed). */
export type IngestScanResponse = { configured: boolean; pending: PendingClass[] };

export type UploadOutcome = {
  error: string | null;
  fileName: string;
  result: 'added' | 'duplicate' | 'failed';
  title: string | null;
  ucJobCode: string | null;
};

export type UploadResponse = { files: UploadOutcome[] };

export type StandardsUploadResponse = {
  /** New to the store. */
  added: number;
  files: Array<{
    error: string | null;
    fileName: string;
    result: 'added' | 'duplicate' | 'failed';
    standards: number;
  }>;
  linkedCount: number;
  /** Standards in the store after the merge. */
  total: number;
  totalClasses: number;
  uncodedSample: string[];
  /** Replaced a standard with the same exact title. */
  updated: number;
};

export type UploadedClass = {
  code: string;
  corpusJds: number;
  /** Set when the class exists; ingesting refreshes it. */
  existingSlug: string | null;
  /** Rebuilding replaces hand edits to this class's envelope. */
  hasManualEnvelope: boolean;
  /** JDs written in the app and added since the last rebuild. */
  newAuthored: number;
  /** Descriptions filed from Classify since the last rebuild. */
  newClassified: number;
  newFiles: number;
  /** JDs from units outside the college, imported from documents since the last rebuild. */
  newImported: number;
  /** The class exists only as a starter envelope from its standard, which these JDs replace. */
  replacesStarter: boolean;
  title: string;
};

export type UploadedPendingResponse = { classes: UploadedClass[] };

export type StandardsIngestResponse = {
  ambiguousCount: number;
  coded: number;
  count: number;
  linkedCount: number;
  linkedSample: string[];
  sample: string[];
  totalClasses: number;
  uncodedSample: string[];
};

export type BootstrapCandidate = {
  code: string;
  family: string;
  function: string;
  grade: string;
  title: string;
};

/** Candidates are only classes on UC Davis payroll; the counts say how many standards were left out. */
export type BootstrapResponse = {
  candidates: BootstrapCandidate[];
  /** Standards whose title matches no UC job code. */
  noCodeMatch: number;
  /** Standards whose title is in the UC title matrix but not on UC Davis payroll. */
  notOnPayroll: number;
};

export type BootstrapCreateResponse = {
  envelopeSource: EnvelopeSource;
  slug: string;
  title: string;
  ucJobCode: string;
};

/**
 * What retiring a profile under a superseded code does: fold it into the successor's existing
 * profile, re-identify it as the successor (it carries corpus JDs or saved JDs), or remove a
 * standard-only class so the successor can be built from its own standard.
 */
export type RetireAction = 'merge' | 'reidentify' | 'remove';

export type SupersededProfile = {
  action: RetireAction;
  authoredJds: number;
  code: string;
  corpusJds: number;
  /** The successor was built without this class's corpus JDs and should be rebuilt. */
  needsRebuild: boolean;
  slug: string;
  successorCode: string;
  /** Null when the profile is removed. */
  successorSlug: string | null;
  successorTitle: string;
  title: string;
};

/** Why an imported envelope did not become a class in this environment. */
export type QualificationRulesSummary = {
  applied: boolean;
  changed: number;
  envelopes: number;
  equivalentAdded: number;
  /** A sample of the changes, to check before applying. */
  examples: Array<{
    educationAfter: string[];
    educationBefore: string[];
    /** Preferred qualifications that are new (moved from education) or reworded. */
    preferredChanged: string[];
    slug: string;
    title: string;
  }>;
  movedToPreferred: number;
};

export type EnvelopeImportRefusal =
  | 'codeMismatch'
  | 'exists'
  | 'invalid'
  | 'noStandard'
  | 'notOnPayroll'
  | 'superseded';

export type EnvelopeImportResult = {
  created: Array<{ slug: string; title: string; ucJobCode: string }>;
  skipped: Array<{ message: string; reason: EnvelopeImportRefusal; title: string }>;
};

export type RetirementResult = {
  profiles: SupersededProfile[];
  /** Corpus JDs moved from a retired code to its successor. */
  refiledJds: number;
};

// ---------------------------------------------------------------- saved JDs

export type SavedJdSummary = {
  /** False for a draft saved before it was ever assembled. */
  assembled: boolean;
  createdAt: string;
  createdBy: string | null;
  department: string;
  id: number;
  slug: string;
  status: JdStatus;
  title: string;
  ucJobCode: string;
  unallocatedPct: number;
  updatedAt: string;
  workingTitle: string;
};

export type SavedJdListResponse = { jds: SavedJdSummary[] };

/** A saved JD: the build's result shape plus who wrote it, when, and what they added. */
export type SavedJd = AssembledJd & {
  /** False for a draft saved before it was ever assembled. */
  assembled: boolean;
  authorAdditions: string[];
  createdAt: string;
  createdBy: string | null;
  /** The saved build screen, for "Continue editing"; null for JDs saved before drafts. */
  draftState: unknown;
  notes: string;
  updatedAt: string;
};

// ---------------------------------------------------------------- analytics

export type ClassCount = { count: number; slug: string; title: string; ucJobCode: string };

export type AnalyticsReport = {
  classes: {
    envelopeMatch: Array<{ classes: number; label: string }>;
    lowestMatch: Array<{
      jds: number;
      meanCoverage: number;
      slug: string;
      title: string;
      wellCoveredPct: number;
    }>;
    mostAuthored: ClassCount[];
    /** `count` is the class's corpus size here. */
    neverUsed: ClassCount[];
    totalClasses: number;
  };
  usage: {
    active30Days: number;
    active7Days: number;
    draft: number;
    jdsSaved: number;
    /** Oldest first; `weekOf` is the Monday (UTC), as YYYY-MM-DD. */
    perWeek: Array<{ count: number; weekOf: string }>;
    ready: number;
    users: number;
  };
};

// ---------------------------------------------------------------- settings

export type AdminEntry = {
  displayName: string | null;
  /** Admins named in server configuration; they cannot be removed from the app. */
  fromConfiguration: boolean;
  grantedAt: string | null;
  grantedBy: string | null;
  lastSeenAt: string | null;
  loginId: string;
};

export type AdminsResponse = { admins: AdminEntry[] };

/** As SQL Server reports it; null when it cannot be determined. */
export type SecurityStatus = { databaseEncryptedAtRest: boolean | null };

/** Where the Anthropic key in use came from. Never contains the key. */
/** Set per environment in server configuration (Llm:Provider); never chosen in the app. */
export type LlmProvider = 'anthropic' | 'azureOpenAi' | 'openAiCompatible';

export type KeyAuditEntry = {
  action: 'set' | 'cleared';
  at: string;
  by: string | null;
  lastFour: string | null;
};

export type ApiKeyStatus = {
  /** Recent key changes, newest first — who and when, never the value. */
  audit: Array<KeyAuditEntry>;
  configurationHasKey: boolean;
  endpoint: string;
  /** False when this environment manages keys in Key Vault only. */
  keyEntryAllowed: boolean;
  /** False for a local OpenAI-compatible server that needs no key. */
  keyRequired: boolean;
  lastFour: string | null;
  model: string;
  provider: LlmProvider;
  source: 'app' | 'configuration' | 'none';
  /** A key is stored but can no longer be decrypted; configuration is being used instead. */
  storedKeyUnreadable: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
};

/** 4xx bodies. The message is written for the user and is rendered verbatim. */
export type ApiError = { message: string };
