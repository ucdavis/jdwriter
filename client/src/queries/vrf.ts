import { fetchJson, HttpError } from '../lib/api.ts';
import { appUrl } from '../lib/basePath.ts';
import { useMutation } from '@tanstack/react-query';
import type { AnnualRange } from '../lib/contracts.ts';
import type { NarrativeKey, VrfForm } from '../features/vrf/vrfModel.ts';

/** What a narrative is about: the position and the request, never who held the position before. */
export type VrfContext = Pick<
  VrfForm,
  | 'actionRequested'
  | 'bargainingUnit'
  | 'department'
  | 'division'
  | 'fillMethod'
  | 'fte'
  | 'jobCode'
  | 'payrollTitle'
  | 'reasons'
  | 'salaryGrade'
  | 'vacancies'
  | 'workingTitle'
>;

export const contextOf = (f: VrfForm): VrfContext => ({
  actionRequested: f.actionRequested,
  bargainingUnit: f.bargainingUnit,
  department: f.department,
  division: f.division,
  fillMethod: f.fillMethod,
  fte: f.fte,
  jobCode: f.jobCode,
  payrollTitle: f.payrollTitle,
  reasons: f.reasons,
  salaryGrade: f.salaryGrade,
  vacancies: f.vacancies,
  workingTitle: f.workingTitle,
});

/** The magic wand: the author's notes for one narrative, drafted into a paragraph to edit. */
export const useVrfNarrative = () =>
  useMutation({
    mutationFn: (body: { context: VrfContext; field: NarrativeKey; notes: string }) =>
      fetchJson<{ paragraph: string }>('/api/vrf/narrative', { body: JSON.stringify(body), method: 'POST' }),
  });

/**
 * The finished form as a Word file, saved by the browser. Built on the server from what's on the
 * screen and sent straight back — nothing is stored.
 */
export const downloadVrf = async (form: VrfForm) => {
  const res = await fetch(appUrl('/api/vrf/docx'), {
    body: JSON.stringify(form),
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'JDWriter' },
    method: 'POST',
  });
  if (!res.ok) {
    const text = await res.text();
    let body: unknown = text;
    try {
      body = JSON.parse(text);
    } catch {
      // Not JSON: keep the text.
    }
    throw new HttpError(res.status, '/api/vrf/docx', body);
  }

  const name = /filename\*?=(?:utf-8'')?"?([^";]+)"?/i.exec(res.headers.get('Content-Disposition') ?? '')?.[1];
  const url = URL.createObjectURL(await res.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = name ? decodeURIComponent(name) : 'VRF.docx';
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
};

export const useDownloadVrf = () => useMutation({ mutationFn: downloadVrf });

/**
 * The Career Tracks annual range for a salary grade ("Grade 20"), or null: a represented title
 * ("STEPS"), a grade the scale lacks, or no scale imported yet.
 */
export const salaryRangeQueryOptions = (grade: string) => ({
  enabled: grade.trim() !== '',
  queryFn: () => fetchJson<{ range: AnnualRange | null }>(`/api/vrf/salary-range?grade=${encodeURIComponent(grade.trim())}`),
  queryKey: ['vrf', 'salary-range', grade.trim()] as const,
  staleTime: Infinity,
});
