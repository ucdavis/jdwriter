import profilesFixture from './profiles.json' with { type: 'json' };
import { delay, http, HttpResponse } from 'msw';
import type {
  SecurityStatus,
  UploadedPendingResponse,
  UploadResponse,
  SavedJd,
  SavedJdListResponse,
  ApiKeyStatus,
  AdminsResponse,
  AssembledJd,
  BootstrapCreateResponse,
  BootstrapResponse,
  BuildRequest,
  ClassifyRequest,
  ClassifyResponse,
  ClassListResponse,
  ClassProfileResponse,
  ClassSummaryResponse,
  CoverageReport,
  EnvelopeCheckResponse,
  ExtractResponse,
  FitSuggestResponse,
  IntakeRequest,
  IntakeResponse,
  JdDetailResponse,
  MisfitsResponse,
  StandardsIngestResponse,
} from '../lib/contracts.ts';

/**
 * MSW handlers for the whole contract.
 *
 * Fixture data keeps the SHAPE of three real class profiles — a 154-JD class, a 3-JD
 * class, and a standard-derived class with no JDs at all — so the UI is exercised against
 * real percentage splits, section sizes and coverage numbers. Every piece of envelope TEXT
 * is synthetic: envelopes are products of the system and are not committed. Filenames are
 * synthetic too, because real HRTMS filenames embed UCPath position numbers.
 */

type Fixture = ClassProfileResponse & { __coverage: CoverageReport | null };

const profiles = profilesFixture as unknown as Fixture[];

const profileOf = (slug: string) => profiles.find((p) => p.slug === slug);

const strip = ({ __coverage: _coverage, ...rest }: Fixture): ClassProfileResponse => rest;

/** An official standard for exactly one class, so the UI exercises both states. */
const STANDARD_SLUG = profiles[0].slug;

const sampleStandard = {
  customScope: 'Works within a single laboratory or research unit.',
  education: ['High school diploma or equivalent certification.'],
  flsa: 'Non-Exempt',
  genericScope:
    'Applies acquired job skills to perform a range of standard laboratory support tasks under general supervision.',
  grade: 'Grade 19',
  keyResponsibilities: [
    'Prepares media, reagents and sample materials to established protocols.',
    'Operates and maintains routine laboratory instrumentation.',
    'Records and verifies experimental data.',
  ],
  ksa: [
    'Working knowledge of laboratory safety practice.',
    'Skill in accurate record keeping.',
  ],
  licenses: [],
  longTitle: 'Laboratory Assistant 1',
  persProg: 'PSS',
  specialConditions: [],
  union: 'TX',
};

/**
 * Titles with no ingested profile. The real list is ~1,200 entries; a handful is enough
 * to exercise the "not yet ingested" branch and the search cap message.
 */
const seedTitles = [
  { code: '007399', family: 'Research Administration', grade: 'Grade 23', title: 'Project Policy Analyst 4' },
  { code: '006206', family: 'Research Administration', grade: 'Grade 21', title: 'Research Administrator 3' },
  { code: '008543', family: 'Agriculture', grade: 'STEPS', title: 'Farm Laborer' },
  { code: '004698', family: 'Information Technology', grade: 'Grade 22', title: 'Systems Administrator 2' },
];

const misfitsFor = (threshold: number): MisfitsResponse => {
  const withCoverage = profiles.filter((p) => p.__coverage);
  const misfits = withCoverage.flatMap((p) =>
    (p.__coverage as CoverageReport).perJd
      .filter((j) => j.coveredPct < threshold)
      .map((j) => ({
        classTitle: p.title,
        coveredPct: j.coveredPct,
        idiosyncratic: j.uncovered,
        slug: p.slug,
        sourceFile: j.sourceFile,
        ucJobCode: p.ucJobCode,
      }))
  );

  return {
    misfits: misfits.sort((a, b) => a.coveredPct - b.coveredPct),
    threshold,
    totalJds: withCoverage.reduce((sum, p) => sum + p.corpusSize, 0),
  };
};

/** What the mocked build has "saved" this session, so My JDs has something to show. */
let savedJds: SavedJd[] = [];

export const handlers = [
  http.get('/api/jds', () =>
    HttpResponse.json<SavedJdListResponse>({
      jds: [...savedJds].reverse().map((j) => ({
        createdAt: j.createdAt,
        createdBy: j.createdBy,
        department: j.department,
        id: j.authoredJdId ?? 0,
        slug: j.slug,
        status: j.status,
        title: j.title,
        ucJobCode: j.ucJobCode,
        unallocatedPct: j.unallocatedPct,
        updatedAt: j.updatedAt,
        workingTitle: j.workingTitle,
      })),
    })
  ),

  http.get('/api/jds/:id', ({ params }) => {
    const jd = savedJds.find((j) => j.authoredJdId === Number(params.id));
    return jd
      ? HttpResponse.json<SavedJd>(jd)
      : HttpResponse.json({ message: 'That job description was not found.' }, { status: 404 });
  }),

  http.get('/api/user/me', () =>
    HttpResponse.json({
      email: 'ndlewis@ucdavis.edu',
      iamId: '10000000',
      id: 'mock-user',
      name: 'Mock Admin',
      // Admin so the back end is reachable in the mocked app.
      roles: ['Author', 'Admin'],
    })
  ),

  http.get('/api/classes', () =>
    HttpResponse.json<ClassListResponse>({
      classes: [
        ...profiles.map((p) => ({
          bargainingUnit: p.unionCode.consensus ?? undefined,
          corpusSize: p.corpusSize,
          ready: true,
          slug: p.slug,
          title: p.title,
          ucJobCode: p.ucJobCode,
        })),
        ...seedTitles.map((t) => ({
          family: t.family,
          grade: t.grade,
          ready: false,
          slug: `seed-${t.code}`,
          title: t.title,
          ucJobCode: t.code,
        })),
      ],
    })
  ),

  // Registered before /api/classes/:slug, which would otherwise claim "summary" as a slug.
  // The fixtures carry envelopes but not the consolidated rows, so the counts are read off
  // the envelope — close enough to exercise the page, not a statement about real data.
  http.get('/api/classes/summary', () =>
    HttpResponse.json<ClassSummaryResponse>({
      classes: profiles.map((p) => {
        const responsibilities = p.envelope?.keyResponsibilities ?? [];
        return {
          consolidatedFunctions: p.corpusSize > 0 ? responsibilities.length : 0,
          corpusSize: p.corpusSize,
          coverageN: p.__coverage?.n ?? null,
          ctJobFamily: p.ctJobFamily,
          ctJobFunction: p.ctJobFunction,
          envelopePctTotal: responsibilities.reduce((sum, r) => sum + r.pctTime, 0),
          envelopeResponsibilities: responsibilities.length,
          envelopeSource: p.envelopeSource,
          grade: p.salaryGrade.consensus,
          hasEnvelope: p.envelope !== null,
          ksas: p.envelope?.minQualifications.length ?? 0,
          meanCoverage: p.__coverage?.meanCoverage ?? null,
          personnelProgram: p.personnelProgram,
          responsibilities: responsibilities.length,
          slug: p.slug,
          standardLinked: p.slug === STANDARD_SLUG,
          title: p.title,
          ucJobCode: p.ucJobCode,
          wellCoveredPct: p.__coverage?.wellCoveredPct ?? null,
        };
      }),
    })
  ),

  http.get('/api/classes/:slug', ({ params }) => {
    const profile = profileOf(String(params.slug));
    if (!profile) {
      return HttpResponse.json({ message: 'No such class.' }, { status: 404 });
    }
    return HttpResponse.json<ClassProfileResponse>({
      ...strip(profile),
      standard: profile.slug === STANDARD_SLUG ? sampleStandard : null,
    });
  }),

  http.get('/api/classes/:slug/coverage', ({ params }) =>
    HttpResponse.json<CoverageReport | null>(
      profileOf(String(params.slug))?.__coverage ?? null
    )
  ),

  http.get('/api/classes/:slug/jds', ({ params }) => {
    const coverage = profileOf(String(params.slug))?.__coverage;
    return HttpResponse.json({
      jds: (coverage?.perJd ?? []).map((j, i) => ({
        coveredPct: j.coveredPct,
        departmentName: 'Plant Pathology',
        sourceFile: j.sourceFile,
        workingTitle: `Laboratory Assistant ${i + 1}`,
      })),
    });
  }),

  http.get('/api/classes/:slug/jds/*', ({ params }) => {
    const profile = profileOf(String(params.slug));
    if (!profile?.envelope) {
      return HttpResponse.json({ message: 'No such job description.' }, { status: 404 });
    }
    const envelopeFunctions = profile.envelope.keyResponsibilities.map((r) => r.functionName);
    return HttpResponse.json<JdDetailResponse>({
      envelope: profile.envelope,
      profile: { slug: profile.slug, title: profile.title, ucJobCode: profile.ucJobCode },
      record: {
        departmentName: 'Plant Pathology',
        flsaStatus: profile.flsaStatus.consensus ?? '',
        jobSummary:
          'Supports an active research laboratory by preparing materials, running routine assays and recording results for the principal investigator.',
        leads: false,
        qualifications: {
          education: 'High school diploma or equivalent.',
          ksaMin: ['Careful record keeping.', 'Knowledge of laboratory safety.'],
          ksaPref: ['Experience with plate readers.'],
          licenses: [],
          minExperience: ['One year of laboratory experience.'],
        },
        responsibilities: [
          ...envelopeFunctions.slice(0, 2).map((name, i) => ({
            duties: ['Prepares materials to protocol.', 'Logs results.'],
            functionName: name,
            inEnvelope: true,
            pct: i === 0 ? 55 : 25,
          })),
          {
            // The idiosyncratic one — the drift the review page exists to surface.
            duties: ['Coordinates the departmental seminar series.'],
            functionName: 'EVENT COORDINATION',
            inEnvelope: false,
            pct: 20,
          },
        ],
        salaryGrade: profile.salaryGrade.consensus ?? '',
        sourceFile: 'Sample Class/JD-001.HTML',
        supervises: false,
        ucJobCode: profile.ucJobCode,
        ucJobTitle: profile.title,
        unionCode: profile.unionCode.consensus ?? '',
        workingTitle: 'Laboratory Assistant',
      },
    });
  }),

  http.get('/api/standards/:slug', ({ params }) =>
    HttpResponse.json(String(params.slug) === STANDARD_SLUG ? sampleStandard : null)
  ),

  http.get('/api/fit/misfits', ({ request }) => {
    const threshold = Number(new URL(request.url).searchParams.get('threshold') ?? 90);
    return HttpResponse.json(misfitsFor(threshold));
  }),

  http.post('/api/intake/match', async ({ request }) => {
    const { request: text } = (await request.json()) as IntakeRequest;
    await delay(400);
    if (!text.trim()) {
      return HttpResponse.json({ message: 'Describe the role first.' }, { status: 400 });
    }
    return HttpResponse.json<IntakeResponse>({
      matches: profiles.slice(0, 2).map((p, i) => ({
        confidence: i === 0 ? 86 : 41,
        rationale:
          i === 0
            ? 'The described sample handling and result logging match this class’s core responsibilities.'
            : 'Overlaps on field work but sits at a higher level of independence.',
        slug: p.slug,
        title: p.title,
        ucJobCode: p.ucJobCode,
      })),
    });
  }),

  http.post('/api/classify/extract', async () => {
    await delay(500);
    return HttpResponse.json<ExtractResponse>({
      filename: 'PD_Evaluation_Analyst_final.xlsx',
      kind: 'xlsx',
      note: 'Position Description workbook — read the “New Recruitment” sheet.',
      // Not ingested, which is the corpus-gap case the UI must name correctly.
      proposed: { code: '7399', ingested: false, title: 'Project Policy Analyst 4' },
      text: [
        'Working Title: Evaluation Analyst',
        'Supervises other employees: no',
        '',
        'Job Summary:',
        'Designs survey instruments, cleans and analyses study datasets, and reports findings to principal investigators.',
        '',
        'Essential Responsibilities:',
        '85% ANALYSIS',
        '  - Builds and validates survey instruments.',
        '  - Cleans and analyses study datasets.',
        '15% OTHER DUTIES AS ASSIGNED',
        '  - Attends programme meetings.',
      ].join('\n'),
    });
  }),

  http.post('/api/classify', async ({ request }) => {
    const body = (await request.json()) as ClassifyRequest;
    await delay(700);
    if (!body.description.trim()) {
      return HttpResponse.json(
        { message: 'Paste a description or upload a file first.' },
        { status: 400 }
      );
    }
    return HttpResponse.json<ClassifyResponse>({
      distilled: {
        functions: [{ name: 'ANALYSIS' }, { name: 'OTHER DUTIES AS ASSIGNED' }],
        source: 'text',
        workingTitle: 'Evaluation Analyst',
      },
      matches: profiles.slice(0, 2).map((p, i) => ({
        confidence: i === 0 ? 86 : 44,
        coveredPct: i === 0 ? 78 : 41,
        inClass: p.envelope?.keyResponsibilities.slice(0, 2).map((r) => r.functionName) ?? [],
        levelFit: i === 0 ? 'at' : 'below',
        levelNote:
          i === 0
            ? 'The independence described matches the journey level for this class.'
            : 'The described autonomy exceeds this class’s scope.',
        outOfClass: ['SURVEY INSTRUMENT DESIGN'],
        rationale:
          i === 0
            ? 'Dataset preparation and statistical reporting are this class’s core work.'
            : 'Shares the reporting element but not the analytical depth.',
        slug: p.slug,
        title: p.title,
        ucJobCode: p.ucJobCode,
      })),
      proposed: body.proposedCode
        ? {
            basis: 'standard',
            code: body.proposedCode,
            contradicts: [
              {
                evidence: 'reports findings to principal investigators',
                point: 'No policy analysis or programme administration is described.',
              },
            ],
            fits: 'partly',
            missing: ['Policy development', 'Programme budget responsibility'],
            summary:
              'The description supports analytical work but not the policy and programme scope this class expects.',
            supports: ['Independent analysis of complex data'],
            title: 'Project Policy Analyst 4',
          }
        : undefined,
      verdict: 'clear',
      verdictNote:
        'One class fits clearly, with a small amount of work that falls outside it.',
    });
  }),

  http.post('/api/build/check', async ({ request }) => {
    const body = (await request.json()) as BuildRequest;
    await delay(500);
    const many = body.addedItems.length > 2;
    return HttpResponse.json<EnvelopeCheckResponse>({
      matchedSignals: many ? ['Supervises other staff', 'Sets unit budget'] : [],
      rationale: many
        ? 'The additions describe supervisory and budget responsibility, which belong to a higher class.'
        : 'The additions stay within the normal scope of this class.',
      suggestedClass: many ? profiles[1].title : '',
      suggestedSlug: many ? profiles[1].slug : '',
      verdict: many ? 'out_of_envelope' : 'in_envelope',
    });
  }),

  http.post('/api/build/assemble', async ({ request }) => {
    const body = (await request.json()) as BuildRequest;
    const profile = profileOf(body.slug);
    await delay(900);
    const authoredJdId = body.authoredJdId ?? savedJds.length + 1;
    const envelope = profile?.envelope;

    // Percentages are recomputed server-side to sum to 100. Mirrored here so the UI is
    // exercised against a total that actually balances.
    const kept = body.keptResponsibilities.filter((r) => r.duties.length > 0);
    const rawTotal = kept.reduce((sum, r) => sum + r.pctTime, 0) || 1;
    const normalised = kept.map((r, i) => ({
      ...r,
      pctTime:
        i === kept.length - 1
          ? 100 - kept.slice(0, -1).reduce((s, x) => s + Math.round((x.pctTime / rawTotal) * 100), 0)
          : Math.round((r.pctTime / rawTotal) * 100),
    }));

    const unallocatedPct = 100 - normalised.reduce((sum, r) => sum + r.pctTime, 0);

    const assembled: AssembledJd = {
      authoredJdId,
      bargainingUnit: profile?.unionCode.consensus ?? null,
      canPublish: unallocatedPct === 0,
      complianceEdits: [
        {
          after: 'Lifts up to 25 pounds, with or without reasonable accommodation.',
          before: 'Must be able to lift 25 pounds.',
          reason: 'Physical requirements are framed with reasonable-accommodation language.',
          section: 'physicalRequirements[0]',
          source: 'rule',
        },
      ],
      department: body.department,
      flsaStatus: profile?.flsaStatus.consensus ?? null,
      jd: {
        conditionsOfEmployment: envelope?.conditionsOfEmployment ?? [],
        education: body.keptEducation,
        jobSummary:
          envelope?.summary ??
          'Supports the unit by performing the responsibilities described below.',
        keyResponsibilities: normalised,
        licensesCertifications: body.keptCerts,
        minKSA: body.keptMinKSA,
        physicalRequirements: envelope?.physicalRequirements ?? [],
        prefKSA: body.keptPrefKSA,
        workEnvironment: body.keptWorkEnvironment,
        workExperience: body.keptWorkExperience,
      },
      salaryGrade: profile?.salaryGrade.consensus ?? null,
      slug: body.slug,
      status: unallocatedPct === 0 ? 'ready' : 'draft',
      title: profile?.title ?? '',
      ucJobCode: profile?.ucJobCode ?? '',
      unallocatedPct,
      workingTitle: body.workingTitle || (profile?.title ?? ''),
    };
    const saved: SavedJd = {
      ...assembled,
      authorAdditions: body.addedItems,
      createdAt: new Date().toISOString(),
      createdBy: 'Mock Admin',
      notes: body.notes,
      updatedAt: new Date().toISOString(),
    };
    savedJds = [...savedJds.filter((j) => j.authoredJdId !== authoredJdId), saved];
    return HttpResponse.json<AssembledJd>(assembled);
  }),

  http.post('/api/envelope/save', async ({ request }) => {
    const body = (await request.json()) as { slug: string };
    await delay(400);
    return HttpResponse.json({ ok: true as const, slug: body.slug });
  }),

  http.post('/api/envelope/coverage', async () => {
    await delay(600);
    return HttpResponse.json<CoverageReport>(
      profiles[0].__coverage ?? { meanCoverage: 0, n: 0, perJd: [], wellCoveredPct: 0 }
    );
  }),

  http.post('/api/fit/suggest', async () => {
    await delay(600);
    return HttpResponse.json<FitSuggestResponse>({
      matches: [
        {
          confidence: 74,
          rationale: 'The event-coordination work is central to this class rather than incidental.',
          slug: profiles[1].slug,
          title: profiles[1].title,
          ucJobCode: profiles[1].ucJobCode,
        },
      ],
    });
  }),

  http.get('/api/admin/ingest/pending', () =>
    HttpResponse.json({
      configured: true,
      pending: [
        { code: '006256', fileCount: 9, slug: '006256-rsch-data-anl-2', title: 'Rsch Data Anl 2' },
      ],
    })
  ),

  http.post('/api/admin/ingest/class', async () => {
    await delay(1200);
    return HttpResponse.json({ ok: true });
  }),

  http.post('/api/admin/standards/ingest', async () => {
    await delay(900);
    return HttpResponse.json<StandardsIngestResponse>({
      ambiguousCount: 0,
      coded: 187,
      count: 214,
      linkedCount: 19,
      linkedSample: ['Laboratory Assistant 1', 'Agriculture Manager 1'],
      sample: ['Laboratory Assistant 1', 'Laboratory Assistant 2'],
      totalClasses: 65,
      uncodedSample: ['Systemwide Academic Personnel Analyst 3'],
    });
  }),

  http.get('/api/admin/uploads/pending', () =>
    HttpResponse.json<UploadedPendingResponse>({ classes: [] })
  ),

  http.post('/api/admin/uploads', () => HttpResponse.json<UploadResponse>({ files: [] })),

  http.post('/api/admin/uploads/ingest', async () => {
    await delay(900);
    return HttpResponse.json({ ok: true });
  }),

  http.get('/api/admin/settings/security', () =>
    HttpResponse.json<SecurityStatus>({ databaseEncryptedAtRest: true })
  ),

  http.get('/api/admin/settings/api-key', () =>
    HttpResponse.json<ApiKeyStatus>({
      configurationHasKey: true,
      lastFour: 'cfg1',
      source: 'configuration',
      storedKeyUnreadable: false,
      updatedAt: null,
      updatedBy: null,
    })
  ),

  http.put('/api/admin/settings/api-key', async ({ request }) => {
    const { key } = (await request.json()) as { key: string };
    await delay(600);
    return HttpResponse.json<ApiKeyStatus>({
      configurationHasKey: true,
      lastFour: key.slice(-4),
      source: 'app',
      storedKeyUnreadable: false,
      updatedAt: new Date().toISOString(),
      updatedBy: 'Mock Admin',
    });
  }),

  http.delete('/api/admin/settings/api-key', () =>
    HttpResponse.json<ApiKeyStatus>({
      configurationHasKey: true,
      lastFour: 'cfg1',
      source: 'configuration',
      storedKeyUnreadable: false,
      updatedAt: null,
      updatedBy: null,
    })
  ),

  http.get('/api/admin/admins', () =>
    HttpResponse.json<AdminsResponse>({
      admins: [
        {
          displayName: 'Mock Admin',
          fromConfiguration: true,
          grantedAt: null,
          grantedBy: null,
          lastSeenAt: new Date().toISOString(),
          loginId: 'mockadmin',
        },
      ],
    })
  ),

  http.post('/api/admin/admins', async ({ request }) => {
    const { loginId } = (await request.json()) as { loginId: string };
    return HttpResponse.json({ loginId: loginId.trim().toLowerCase() });
  }),

  http.delete('/api/admin/admins/:loginId', () => HttpResponse.json({ ok: true as const })),

  http.post('/api/admin/bootstrap', async ({ request }) => {
    const { title } = (await request.json()) as { title: string };
    await delay(900);
    return HttpResponse.json<BootstrapCreateResponse>({
      envelopeSource: 'standard',
      slug: `004501-${title.toLowerCase().replaceAll(/[^\da-z]+/g, '-')}`,
      title,
      ucJobCode: '004501',
    });
  }),

  http.get('/api/admin/bootstrap/candidates', async () => {
    await delay(700);
    return HttpResponse.json<BootstrapResponse>({
      candidates: [
        {
          code: '004501',
          family: 'Student Services',
          function: 'Academic Advising',
          grade: 'Grade 20',
          title: 'Academic Achievement Counselor 3',
        },
      ],
    });
  }),
];
