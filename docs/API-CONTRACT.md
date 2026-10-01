# JDWriter API contract

The routes the client codes against. Derived 1:1 from the POC's `src/app/api/*` so the UI port is
mechanical, with the additions the POC got for free from React Server Components and now needs as
real endpoints (the `GET` reads at the bottom).

All paths are relative. Never hardcode an origin — Vite proxies `/api` to the backend in
development and ASP.NET Core serves both in production.

`fetchJson` in `client/src/lib/api.ts` already redirects a `401` to `/login?returnUrl=…`, so no
route below needs to handle unauthenticated itself.

## Roles

| Role | Who | Surfaces |
|---|---|---|
| `Author` | everyone who signs in | browse, intake, classify, guided build |
| `Admin` | campus logins on the admin whitelist | everything above plus the whole back end — envelopes, review, ingest, standards, settings |

Roles are derived on every request, never stored: Author for everyone, Admin when the user's
campus login id (from a `@ucdavis.edu` sign-in name) is whitelisted — either in configuration
(`Admin:BootstrapLoginIds`, which cannot be removed from the app) or by an admin in Settings.
Revoking an admin takes effect on their next request. Enforced server-side with
`[Authorize(Roles = …)]`; the client also hides the back end from Authors, but that is cosmetic.

## Writes

### `POST /api/intake/match` — Author
Free-text role description → ranked candidate classes. Two model calls internally (a cheap
whole-catalog shortlist, then a detailed rank of the finalists), which the client does not see.

```ts
// request
{ request: string }
// response
{ matches: Array<{ slug: string; title: string; ucJobCode: string; confidence: number; rationale: string }> }
```
At most 4 matches, most confident first.

### `POST /api/classify` — Author
An existing written description → an independent classification verdict.

```ts
// request
{ description: string; proposedCode?: string | null }

// response
{
  // How the document was READ. Shown to the user so a bad distillation is visible
  // before they act on the verdict — the distill step is what makes this affordable
  // (a real HRTMS export is ~115KB; feeding raw text to both matcher stages costs ~4x).
  distilled: { workingTitle: string; summary: string; supervises: string;
               functions: Array<{ name: string; pctTime: number; duties: string[] }>;
               education: string[]; experience: string[]; ksas: string[] };

  // Up to 4. Each carries its OWN level judgement and coverage — a caller cannot
  // reuse the top match's numbers for the runners-up.
  matches: Array<{
    slug: string; title: string; ucJobCode: string; confidence: number;
    levelFit: "below" | "at" | "above" | "unclear"; levelNote: string; rationale: string;
    coveredPct: number;      // share of the description's stated time this class covers
    inClass: string[];       // function names the class covers
    outOfClass: string[];    // function names falling outside it
  }>;

  // Decided by RULES IN CODE from the match set, never by the model. This is what an
  // HR reviewer acts on, so it must be reproducible and explainable rather than a
  // model's mood. The page leads with it.
  verdict: "clear" | "close-call" | "level-mismatch" | "weak";
  verdictNote: string;

  proposed?: {
    code: string; title: string;
    fits: "yes" | "no" | "partly";
    summary: string;
    contradicts: Array<{ point: string; evidence: string }>;  // evidence QUOTES the description
    missing: string[];
    supports: string[];
    comparedAs?: string;                      // set when the proposal was superseded
    basis: "envelope" | "standard" | "none";  // what the assessment reasoned from
  } | null;
}
```

**`proposedCode` is sent separately from `description` and never reaches the ranking
prompt.** Anti-anchoring, not confidentiality: a model shown the classification the unit wants
ratifies it, which makes the confidence number meaningless. It is argued afterwards by a second
call whose `contradicts[].evidence` must quote the description — a bare assertion is not something
an analyst can hand back to a department.

`basis` falls back envelope → official standard → `"none"`, and says so rather than reasoning from
a bare title.

A proposal that was never ingested **could not have been returned** by the classifier, so the UI
must present that as a corpus gap rather than a disagreement.

### `POST /api/classify/extract` — Author
`multipart/form-data` with one `file`. Deterministic, never a model call.

```ts
// response
{ text: string; kind: "docx" | "pdf" | "xlsx" | "html" | "text"; filename: string;
  note?: string | null;
  proposed?: { code: string; title: string; ingested: boolean } | null }
```
Returns the text **without classifying it**, so a mangled PDF can be fixed before it costs a call.
`4xx` carries a user-facing message naming the remedy (convert the format, OCR the scan, paste the
text) — surface it verbatim.

### `POST /api/build/check` — Author
Does a proposed set of additions still fit the class envelope?

```ts
// request
{ slug: string; additions: string[] }
// response
{ verdict: "in_envelope" | "borderline" | "out_of_envelope";
  matchedSignals: string[]; suggestedSlug: string | null; suggestedClass: string | null; rationale: string }
```
Skip the call entirely when `additions` is empty — the envelope check only polices additions.

### `POST /api/build/assemble` — Author
Selections → a finished JD with its compliance audit trail.

The request carries RESOLVED CONTENT, not identifiers. Authors edit function names inline, adjust
each `pctTime`, drop whole functions, and keep or drop across six qualification sections — ids
could not express any of that.

```ts
// request
{ slug: string;
  workingTitle: string;
  department: string;
  keptResponsibilities: Array<{ functionName: string; pctTime: number; duties: string[] }>;
  keptCerts: string[];
  keptEducation: string[];
  keptWorkExperience: string[];
  keptMinKSA: string[];
  keptPrefKSA: string[];
  keptWorkEnvironment: string[];
  addedItems: string[];   // every user-added item across all sections — this is what the
                          // envelope check polices, and an empty list skips the call entirely
  notes: string }
// response
{ jd: { jobSummary: string;
        keyResponsibilities: Array<{ functionName: string; pctTime: number; duties: string[] }>;
        licensesCertifications: string[]; education: string[]; workExperience: string[];
        minKSA: string[]; prefKSA: string[]; conditionsOfEmployment: string[];
        workEnvironment: string[]; physicalRequirements: string[] };
  complianceEdits: Array<{ section: string; source: "rule" | "llm"; before: string; after: string; reason: string }>;
  // class attributes sit at the TOP LEVEL beside the draft — there is no `meta` object
  slug: string; title: string; workingTitle: string; department: string; ucJobCode: string;
  salaryGrade: string | null; flsaStatus: string | null; bargainingUnit: string | null;
  unallocatedPct: number;   // 0 when the kept set already sums to 100
  canPublish: boolean }     // exactly unallocatedPct === 0
```

**`pctTime` is carried through verbatim from the envelope, NOT rescaled**, and the endpoint refuses
to produce a publishable JD while `unallocatedPct != 0`.

This is a deliberate three-way choice. Rescaling (so 50:20 becomes 71:29) always sums to 100 and
needs no UI, but it silently inflates — an analyst sees 71% where the envelope said 50% and nobody
told them the number moved. Carrying through and publishing at 70%, as the POC does, produces a JD
that is arguably invalid for HRTMS. So neither: the author is shown the shortfall and decides where
the freed time goes, because dropping a duty is a judgement about the role and reallocating it is
the same judgement.

The client must show a running total, name the shortfall, and disable publishing until it is exactly
100 — with the reason next to the disabled control, not only in a toast. The server enforces it
regardless.

### `JobEnvelope` on the wire

Every endpoint that sends or receives an envelope uses this flat shape — never the normalized
entity, whose list sections are one kind-discriminated `items` table and whose duties are rows.
```ts
type JobEnvelope = {
  summary: string; scopeStatement: string;
  keyResponsibilities: Array<{ functionName: string; pctTime: number; duties: string[] }>;
  requiredCertifications: string[]; education: string[]; workExperience: string[];
  minQualifications: string[]; prefQualifications: string[]; conditionsOfEmployment: string[];
  workEnvironment: string[]; physicalRequirements: string[]; outOfEnvelope: string[] }
```

### `POST /api/envelope/save` — Admin
```ts
{ slug: string; envelope: JobEnvelope }  →  { ok: true; slug: string }
```
**`400` unless `keyResponsibilities` total exactly 100%** — ported from the POC's save route. An
envelope is the standard every author in the class starts from, so it is never valid off 100%; the
server refuses regardless of what the editor allowed. Replaces the envelope wholesale and marks the
class `envelopeSource: "manual"`.

### `POST /api/envelope/coverage` — Admin
Re-run backwards coverage of a class's JDs against a **candidate** envelope — the unsaved one the
analyst is editing. Checking the saved envelope would defeat the purpose: the question is whether
an edit is safe to make, which can only be answered before it is saved.
```ts
{ slug: string; envelope: JobEnvelope }  →  { n: number; meanCoverage: number; wellCoveredPct: number;
                       perJd: Array<{ sourceFile: string; coveredPct: number;
                                      uncovered: Array<{ name: string; pct: number }> }> }
```

### `GET /api/fit/misfits?threshold=90` — Admin
The cross-class review list: real JDs that fit their assigned class poorly. Reads the coverage
already stored on each profile — nothing is recomputed, and no model is called.

```ts
{ totalJds: number; threshold: number;
  misfits: Array<{ slug: string; classTitle: string; ucJobCode: string; sourceFile: string;
                   coveredPct: number;
                   idiosyncratic: Array<{ name: string; pct: number }> }> }   // worst fit first
```

### `POST /api/fit/suggest` — Admin
Which class best fits ONE specific JD? A suggestion is about a job description, not about a class,
so it needs the JD.

```ts
{ slug: string; sourceFile: string }
  →  { matches: ClassMatch[]; currentSlug: string }
```

### `GET /api/admin/ingest/pending` — Admin
```ts
{ pending: Array<{ code: string; slug: string; title: string; fileCount: number }> }
```
Classes present in the corpus directory (`Corpus:Directory`) with no profile yet. `400` with a
message when no corpus directory is configured — a deployed environment may simply not have one.

### `POST /api/admin/ingest/class` — Admin
`{ code: string }  →  { slug, title, ucJobCode, corpusSize }`. One class per request: parse,
consolidate, synthesize an envelope, compute coverage. Several model calls; a minute or two for a
large class, which is why the client loops rather than batching.

### `POST /api/admin/standards/ingest` — Admin
Rebuilds the standards table from the Job Builder workbooks in `Standards:Directory`, replacing it
wholesale inside one transaction.
```ts
{ count: number; coded: number; uncodedSample: string[]; ambiguousCount: number;
  sample: string[]; totalClasses: number; linkedCount: number; linkedSample: string[] }
```
`uncodedSample` names titles rather than counting them: "not a UC Davis title" and "ambiguous" need
different follow-up.

### `GET /api/admin/bootstrap/candidates` — Admin
`{ candidates: Array<{ title: string; code: string; family: string; function: string; grade: string }> }`
— standards with a resolvable code and no profile. Superseded codes are never offered.

### `GET /api/admin/admins` — Admin
`{ admins: Array<{ loginId: string; fromConfiguration: boolean; grantedBy: string | null;
grantedAt: string | null; displayName: string | null; lastSeenAt: string | null }> }`

### `POST /api/admin/admins` — Admin
`{ loginId: string }  →  { loginId }` (normalized). Accepts `ndlewis` or `ndlewis@ucdavis.edu`;
`400` with a message for anything else. Idempotent; the person need not have signed in yet.

### `DELETE /api/admin/admins/{loginId}` — Admin
`400` for a configured admin (remove it from configuration instead) and for removing yourself.

### `POST /api/admin/bootstrap` — Admin
`{ title: string }  →  { slug, title, ucJobCode, envelopeSource }`. `400` naming the successor when
the code is superseded.

## Reads

The POC got these from React Server Components; they are real endpoints now.

### `GET /api/classes` — Author
```ts
{ classes: Array<{ slug: string; title: string; ucJobCode: string; ready: boolean;
                   corpusSize?: number; bargainingUnit?: string; family?: string; grade?: string }> }
```
Ingested classes first (alphabetical), then in-use UC Davis titles with no profile yet
(`ready: false`). **Superseded codes never appear** — authoring against one would produce a JD
under a dead classification.

### `GET /api/classes/summary` — Admin
The analyst index: curation statistics for every **ingested** class, alphabetical. No seeds.
```ts
{ classes: Array<{
    slug: string; title: string; ucJobCode: string;
    ctJobFamily: string; ctJobFunction: string; personnelProgram: string;
    corpusSize: number; grade: string | null;
    envelopeSource: "claude" | "deterministic" | "manual" | "standard" | null;
    hasEnvelope: boolean;
    envelopeResponsibilities: number;
    envelopePctTotal: number;      // as stored, never corrected
    consolidatedFunctions: number; // 0 = consolidation has not run
    responsibilities: number;      // consolidated when present, else raw functions
    ksas: number;                  // consolidated min quals when present, else raw KsaMin
    coverageN: number | null; meanCoverage: number | null;
    wellCoveredPct: number | null; // 0..1; null = no report, which is NOT the same as 0
    standardLinked: boolean }> }
```
**Kept separate from `GET /api/classes` on purpose.** That list answers "what can I author against"
and every author hits it on every visit; this answers "what needs my attention" for a handful of
analysts. Merging them puts coverage statistics on the hot path of the most-used screen.

Projected in SQL — counts and stored numbers only, no profile graphs. `envelopePctTotal` is
reported exactly as stored: an envelope is ours to get right, so a total that is not 100 is the
fastest signal a class needs attention, and correcting it here would hide it.

### `GET /api/classes/{slug}` — Author
The subset of the profile the class, build, edit and standard screens read — flat, not wrapped.
```ts
type Distribution = { consensus: string | null; agreement: number };  // absent → { null, 0 }
{ slug: string; title: string; ucJobCode: string;
  ctJobFamily: string; ctJobFunction: string; personnelProgram: string; corpusSize: number;
  envelopeSource: "claude" | "deterministic" | "manual" | "standard" | null;
  envelope: JobEnvelope | null;
  salaryGrade: Distribution; flsaStatus: Distribution; unionCode: Distribution;
  supervises: Distribution; leads: Distribution; worksOutdoorsOver50pct: Distribution;
  sourceFiles: string[];
  standard: ClassStandard | null }   // ClassStandard as GET /api/standards/{slug}
```

### `GET /api/classes/{slug}/coverage` — Admin
```ts
{ n: number; meanCoverage: number; wellCoveredPct: number;
  perJd: Array<{ sourceFile: string; coveredPct: number; uncovered: Array<{ name: string; pct: number }> }> } | null
```
`null` when the class has no stored report.

### `GET /api/classes/{slug}/jds` — Admin
```ts
{ jds: Array<{ sourceFile: string; workingTitle: string; departmentName: string; coveredPct: number | null }> }
```

### `GET /api/classes/{slug}/jds/{sourceFile}` — Admin
One JD side by side with the envelope, each responsibility marked in-envelope or idiosyncratic.
```ts
{ profile: { slug: string; title: string; ucJobCode: string };
  record: { sourceFile: string; workingTitle: string; ucJobTitle: string; ucJobCode: string;
            departmentName: string; jobSummary: string; salaryGrade: string; flsaStatus: string;
            unionCode: string; supervises: boolean | null; leads: boolean | null;
            qualifications: { licenses: string[]; education: string; minExperience: string[];
                              ksaMin: string[]; ksaPref: string[] };
            responsibilities: Array<{ pct: number | null; functionName: string; duties: string[];
                                      inEnvelope: boolean }> };
  envelope: JobEnvelope | null;
  coveredPct: number | null }
```
`inEnvelope` is decided server-side against the CONSOLIDATED members, not the envelope's wording.

### `GET /api/standards/{slug}` — Admin
The linked official standard, or `null`. Roughly 19 of 65 classes have one — the gap is standards
COVERAGE (the workbooks only cover 19 families), not a matching failure, so "no standard" is a
normal state the UI must render calmly.

### `GET /api/user/me` — any signed-in user
Template-provided. Returns identity plus `roles: string[]`.

## Errors

`4xx` bodies are `{ message: string }` and the message is written for the user — render it
verbatim rather than substituting a generic string. `5xx` is not user-facing; show a retry.
