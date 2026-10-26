import type { SavedJd } from '@/lib/contracts.ts';

/**
 * The Vacancy Request Form, as the campus VRF (a Smartsheet form) asks it, in its order. The server
 * keeps the same fields and rules (server.core/Vrf/VrfForm.cs). Answer lists come from the official
 * Word form; which questions apply to which request is our reading of it until the Smartsheet's own
 * branching is known.
 */
export type VrfForm = {
  actionRequested: string;
  adverseImpact: string;
  alternativeOptions: string;
  bargainingUnit: string;
  budgetConsideration: string;
  budgetSource: string;
  criticalRole: string;
  dateVacated: string;
  department: string;
  directReports: string;
  division: string;
  executiveApprover: string;
  executiveApproverEmail: string;
  fillMethod: string;
  fte: string;
  fundingSecured: string;
  fundingSources: string;
  hiringManagerEmail: string;
  hiringManagerName: string;
  hrbpEmail: string;
  hrbpName: string;
  indirectReports: string;
  jobCode: string;
  jobOpeningId: string;
  notes: string;
  otherReason: string;
  payrollTitle: string;
  positionNumber: string;
  rationale: string;
  reasons: string[];
  salaryGrade: string;
  salaryMax: string;
  salaryMid: string;
  salaryMin: string;
  vacancies: string;
  workingTitle: string;
};

export const CAES = 'College of Agricultural and Environmental Sciences';

export const emptyVrf = (): VrfForm => ({
  actionRequested: '',
  adverseImpact: '',
  alternativeOptions: '',
  bargainingUnit: '',
  budgetConsideration: '',
  budgetSource: '',
  criticalRole: '',
  dateVacated: '',
  department: '',
  directReports: '',
  division: CAES,
  executiveApprover: '',
  executiveApproverEmail: '',
  fillMethod: '',
  fte: '1.0',
  fundingSecured: '',
  fundingSources: '',
  hiringManagerEmail: '',
  hiringManagerName: '',
  hrbpEmail: '',
  hrbpName: '',
  indirectReports: '',
  jobCode: '',
  jobOpeningId: '',
  notes: '',
  otherReason: '',
  payrollTitle: '',
  positionNumber: '',
  rationale: '',
  reasons: [],
  salaryGrade: '',
  salaryMax: '',
  salaryMid: '',
  salaryMin: '',
  vacancies: '1',
  workingTitle: '',
});

export const ACTIONS = [
  'New Career Position',
  'Backfill Position',
  'New Contract Appt',
  'Per Diem',
  'New Short Term/Limited Appointment',
  'Contract/Limited Extension or Career Conversion',
  'Retiree/Rehire',
];

export const FILL_METHODS = [
  'Open Competitive Recruitment',
  'Internal Recruitment (Campuswide)',
  'Internal Recruitment (Dept. Limited)',
  'Direct Hire (STE- Limited Appt)',
  'Direct Hire (Career, Contract Appt w/ Waiver of Recruitment)',
  'Contract/Limited Appointment Extension Request',
];

export const REASONS = [
  'Backfilling a vacancy',
  'Health and Safety',
  'Compliance with Federal, State, and/or Local laws/regulations',
  'Cost Savings',
  'Essential UC Davis Functions',
  'Revenue Generation',
  'Other',
];

/** Suggestions only: the campus form's own list hasn't been confirmed, so any source can be typed. */
export const BUDGET_SOURCES = ['19900 - State', '20000 - Federal', '25000 - Endowment/Gift', '33000 - Recharge', 'Student Fees', 'Grant'];

/** A position someone held: when it was vacated, or when it expires, matters. */
export const replacesSomeone = (f: VrfForm) =>
  f.actionRequested === 'Backfill Position' ||
  f.actionRequested === 'Contract/Limited Extension or Career Conversion' ||
  f.actionRequested === 'Retiree/Rehire';

/** A contract or limited appointment: whether its funding is secured matters. */
export const isContract = (f: VrfForm) =>
  f.actionRequested === 'New Contract Appt' ||
  f.actionRequested === 'New Short Term/Limited Appointment' ||
  f.actionRequested === 'Contract/Limited Extension or Career Conversion';

export type NarrativeKey = 'adverseImpact' | 'alternativeOptions' | 'budgetConsideration' | 'criticalRole' | 'rationale';

export const NARRATIVES: Array<{ key: NarrativeKey; label: string; question: string }> = [
  { key: 'rationale', label: 'Rationale', question: 'Please outline the business case for this request.' },
  { key: 'criticalRole', label: 'Critical Role', question: 'Describe why this position is critical to your division.' },
  {
    key: 'adverseImpact',
    label: 'Adverse Impact',
    question: 'Describe the adverse impact of not filling this position for up to 6 months.',
  },
  {
    key: 'budgetConsideration',
    label: 'Budget Consideration',
    question:
      'What cost-saving measures, if any, have been implemented to support this request? Have you considered other options such as TES or Limited Contract (one-time funding) in lieu of a permanent and ongoing financial commitment at this time?',
  },
  {
    key: 'alternativeOptions',
    label: 'Alternative Options',
    question: 'If this request is not approved, how will the work associated with the position be accomplished?',
  },
];

export type StepKey = 'budget' | 'people' | 'position' | 'request' | 'review' | 'why';

export const STEPS: Array<{ key: StepKey; label: string }> = [
  { key: 'position', label: 'The position' },
  { key: 'request', label: 'The request' },
  { key: 'budget', label: 'Budget' },
  { key: 'why', label: 'Why it’s needed' },
  { key: 'people', label: 'People' },
  { key: 'review', label: 'Review & download' },
];

type Required = { applies?: (f: VrfForm) => boolean; key: keyof VrfForm; label: string; step: StepKey };

/** What the campus form requires. Salary is left to HR, by request. */
const REQUIRED: Required[] = [
  { key: 'department', label: 'Department name', step: 'position' },
  { key: 'division', label: 'Division/School/College', step: 'position' },
  { key: 'payrollTitle', label: 'Payroll title', step: 'position' },
  { key: 'jobCode', label: 'Job code', step: 'position' },
  { key: 'workingTitle', label: 'Working title', step: 'position' },
  { key: 'bargainingUnit', label: 'Bargaining unit', step: 'position' },
  { key: 'actionRequested', label: 'Action requested', step: 'request' },
  { key: 'fillMethod', label: 'How the role will be filled', step: 'request' },
  { key: 'positionNumber', label: 'Position number (or N/A)', step: 'request' },
  { applies: replacesSomeone, key: 'dateVacated', label: 'Date vacated or expiration date', step: 'request' },
  { key: 'vacancies', label: 'Number of vacancies', step: 'request' },
  { key: 'fte', label: 'FTE', step: 'request' },
  { key: 'budgetSource', label: 'Budget source', step: 'budget' },
  { key: 'reasons', label: 'Reason(s) for recruitment', step: 'why' },
  { applies: (f) => f.reasons.includes('Other'), key: 'otherReason', label: 'The other reason, explained', step: 'why' },
  ...NARRATIVES.map((n) => ({ key: n.key, label: n.label, step: 'why' as const })),
  { key: 'hiringManagerName', label: 'Hiring manager name', step: 'people' },
  { key: 'hiringManagerEmail', label: 'Hiring manager email', step: 'people' },
  { key: 'hrbpName', label: 'HR business partner name', step: 'people' },
  { key: 'hrbpEmail', label: 'HR business partner email', step: 'people' },
  { key: 'executiveApprover', label: 'Executive approver', step: 'people' },
  { key: 'executiveApproverEmail', label: 'Executive approver email', step: 'people' },
];

const blank = (v: VrfForm[keyof VrfForm]) => (Array.isArray(v) ? v.length === 0 : v.trim() === '');

/** Required answers still missing, step by step. */
export const missingFields = (f: VrfForm) => REQUIRED.filter((r) => (r.applies?.(f) ?? true) && blank(f[r.key]));

/** What a finished JD already says about the position. */
export const fromJd = (jd: SavedJd): Partial<VrfForm> => ({
  bargainingUnit: jd.bargainingUnit ?? '',
  department: jd.department,
  directReports: jd.supervises && jd.supervisesCount ? String(jd.supervisesCount) : '',
  jobCode: jd.ucJobCode,
  payrollTitle: jd.title,
  salaryGrade: jd.salaryGrade ?? '',
  workingTitle: jd.workingTitle,
});
