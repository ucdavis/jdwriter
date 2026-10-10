import { SiteNote } from '@/shared/ui/SiteNote.tsx';
import { Badge, Card, Eyebrow, Meter, Note } from '@/shared/ui/primitives.tsx';
import { Link, useNavigate } from '@tanstack/react-router';
import { messageOf } from '@/features/browse/NlIntake.tsx';
import { ProposedCompare } from './ProposedCompare.tsx';
import { useClassify, useExtractDocument, useStartJdFromClass } from '@/queries/authoring.ts';
import { useRef, useState, type DragEvent } from 'react';
import type { ClassifyMatch, ClassifyVerdict, LevelFit } from '@/lib/contracts.ts';

const verdictTone: Record<ClassifyVerdict, 'green' | 'orange' | 'red' | 'yellow'> = {
  clear: 'green',
  'close-call': 'yellow',
  'level-mismatch': 'orange',
  weak: 'red',
};

const verdictLabel: Record<ClassifyVerdict, string> = {
  clear: 'Clear match',
  'close-call': 'Between two classes',
  'level-mismatch': 'Level mismatch',
  weak: 'No confident match',
};

const levelTone: Record<LevelFit, 'green' | 'muted' | 'orange'> = {
  above: 'orange',
  at: 'green',
  below: 'orange',
  unclear: 'muted',
};

const levelLabel: Record<LevelFit, string> = {
  above: 'Above class level',
  at: 'At level',
  below: 'Below class level',
  unclear: 'Level unclear',
};

const confTone = (c: number) => (c >= 70 ? 'green' : c >= 45 ? 'yellow' : 'muted');

const ACCEPTED =
  '.docx,.pdf,.xlsx,.xlsm,.html,.htm,.txt,.md,application/pdf,text/html,text/plain';

/**
 * Classify an existing written description against the ingested classes.
 *
 * The two-step shape is deliberate. Uploading EXTRACTS the text and stops; classifying is
 * a separate action. That lets the user see a mangled PDF and fix it before spending a
 * model call, which matters because the extraction is free and the classification is not.
 */
export const DescriptionClassifier = () => {
  const [text, setText] = useState('');
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  // Drag events fire on every child element, so a boolean flickers as the pointer crosses
  // the textarea inside the zone. Counting enter/leave pairs is what actually tracks
  // "is the pointer still somewhere inside".
  const depth = useRef(0);

  const extract = useExtractDocument();
  const classify = useClassify();
  const start = useStartJdFromClass();
  const navigate = useNavigate();
  const file = extract.data;

  const ingestFile = (f: File) => {
    classify.reset();
    extract.mutate(f, {
      onSuccess: (result) => setText(result.text),
    });
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    depth.current = 0;
    setDragging(false);
    const f = e.dataTransfer.files?.[0];
    if (f) {
      ingestFile(f);
    }
  };

  const result = classify.data;
  const [top, ...alternatives] = result?.matches ?? [];

  // The class's standard as the frame, this description's work merged in, opened as a draft.
  const startFrom = (slug: string) => {
    if (result) {
      start.mutate(
        { distilled: result.distilled, slug },
        {
          onSuccess: (r) =>
            void navigate({
              params: { slug: r.slug },
              search: { draft: r.authoredJdId },
              to: '/class/$slug',
            }),
        }
      );
    }
  };
  const startProps = (slug: string) => ({
    onStart: () => startFrom(slug),
    startDisabled: start.isPending,
    starting: start.isPending && start.variables?.slug === slug,
  });
  const wordCount = text.trim().split(/\s+/).filter(Boolean).length;

  return (
    <div>
      <Card className="p-5">
        <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
          <div>
            <Eyebrow>Existing description</Eyebrow>
            <p className="mt-1 text-base text-base-content/65">
              Drop in an Excel Position Description, a Word file, or a PDF — or paste the
              text.
            </p>
          </div>
          <button
            className="btn btn-outline btn-sm shrink-0"
            disabled={extract.isPending}
            onClick={() => inputRef.current?.click()}
            type="button"
          >
            {extract.isPending ? 'Reading…' : 'Choose file'}
          </button>
          <input
            accept={ACCEPTED}
            // Visually hidden but must still be reachable by name: the visible control is
            // a styled button that forwards the click here, so without a label this input
            // is invisible to assistive tech and to tests alike.
            aria-label="Upload a job description file"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) {
                ingestFile(f);
              }
              // Reset so re-picking the same file fires onChange again.
              e.target.value = '';
            }}
            ref={inputRef}
            type="file"
          />
        </div>

        <div
          className={`relative rounded-lg transition-colors ${
            dragging ? 'ring-2 ring-primary ring-offset-2 ring-offset-base-100' : ''
          }`}
          onDragEnter={(e) => {
            e.preventDefault();
            depth.current += 1;
            setDragging(true);
          }}
          onDragLeave={(e) => {
            e.preventDefault();
            depth.current -= 1;
            if (depth.current <= 0) {
              setDragging(false);
            }
          }}
          onDragOver={(e) => e.preventDefault()}
          onDrop={onDrop}
        >
          <textarea
            aria-label="Job description text"
            className="textarea textarea-bordered w-full resize-y text-base"
            onChange={(e) => {
              setText(e.target.value);
              // Once edited, the file chip would otherwise claim credit for text the
              // extractor did not produce — including its proposed code.
              extract.reset();
            }}
            placeholder="Drag an Excel, Word, or PDF file here, or paste the full job description — summary, responsibilities, qualifications…"
            rows={12}
            value={text}
          />
          {dragging || extract.isPending ? (
            <div className="pointer-events-none absolute inset-0 flex items-center justify-center rounded-lg bg-primary/10">
              <span className="text-base font-semibold text-primary">
                {extract.isPending
                  ? 'Reading the document…'
                  : 'Drop to read the document'}
              </span>
            </div>
          ) : null}
        </div>

        {file ? (
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <Badge tone="teal">{file.kind.toUpperCase()}</Badge>
            <span className="text-base font-semibold">{file.filename}</span>
            {file.note ? (
              <span className="text-sm text-base-content/50">{file.note}</span>
            ) : null}
            <span className="text-sm text-base-content/50">
              Check the text below before classifying — edit it if the layout came through
              wrong.
            </span>
          </div>
        ) : null}

        {extract.error ? (
          <div className="mt-3">
            <Note tone="red">{messageOf(extract.error)}</Note>
          </div>
        ) : null}

        <div className="mt-2 flex items-center gap-3">
          <button
            className="btn btn-primary btn-sm"
            disabled={classify.isPending || !text.trim()}
            onClick={() =>
              classify.mutate({
                description: text,
                // Sent SEPARATELY from the description so it cannot reach the ranking
                // prompt. The server keeps it out; the client keeps it out of `text`.
                proposedCode: file?.proposed?.code ?? null,
              })
            }
            type="button"
          >
            {classify.isPending ? 'Classifying…' : 'Classify this description'}
          </button>
          <span className="text-sm text-base-content/50">
            {classify.isPending
              ? 'Reading the description, then comparing it against every ingested class.'
              : `${wordCount.toLocaleString()} words`}
          </span>
        </div>
      </Card>

      {classify.error ? (
        <div className="mt-4">
          <Note tone="red">{messageOf(classify.error)}</Note>
        </div>
      ) : null}

      {result ? (
        <div className="mt-5 space-y-4">
          <Card className="p-5">
            <div className="flex items-center gap-2.5">
              <Badge tone={verdictTone[result.verdict]}>
                {verdictLabel[result.verdict]}
              </Badge>
              {result.distilled.source === 'hrtms' ? (
                <Badge tone="teal">HRTMS export parsed</Badge>
              ) : null}
            </div>
            <p className="mt-2.5 text-base">{result.verdictNote}</p>
            {result.filedUnder ? (
              <p className="mt-2 text-sm text-base-content/55" data-testid="filed-under">
                This description has been saved to the corpus for job code{' '}
                <span className="tnum">{result.filedUnder}</span>, where it will inform that
                class&apos;s envelope.
              </p>
            ) : null}
            <p className="mt-1.5 text-base text-base-content/50">
              Read as {result.distilled.functions.length} responsibility function
              {result.distilled.functions.length === 1 ? '' : 's'}
              {result.distilled.workingTitle
                ? ` under the title “${result.distilled.workingTitle}”`
                : ''}
              .
            </p>
            {file?.proposed && top ? (
              <ProposedCompare
                assessment={result.proposed}
                got={top.title}
                proposed={file.proposed}
              />
            ) : null}
          </Card>

          {start.error ? <Note tone="red">{messageOf(start.error)}</Note> : null}

          {top ? <MatchCard match={top} primary {...startProps(top.slug)} /> : null}

          {alternatives.length > 0 ? (
            <div>
              <div className="eyebrow mb-2">Other classes considered</div>
              <div className="space-y-3">
                {alternatives.map((m) => (
                  <MatchCard key={m.slug} match={m} {...startProps(m.slug)} />
                ))}
              </div>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};

const MatchCard = ({
  match,
  onStart,
  primary = false,
  startDisabled,
  starting,
}: {
  match: ClassifyMatch;
  onStart: () => void;
  primary?: boolean;
  startDisabled: boolean;
  starting: boolean;
}) => (
  <Card className={`p-5 ${primary ? 'border-primary/40' : ''}`}>
    <div className="flex items-start justify-between gap-3">
      <div>
        <Link
          className="text-base font-semibold hover:text-primary"
          params={{ slug: match.slug }}
          to="/class/$slug"
        >
          {match.title}
        </Link>
        <div className="mt-0.5 text-sm text-base-content/50 tnum">
          Job code {match.ucJobCode}
        </div>
        <SiteNote site={match.site} siteTwins={match.siteTwins} />
      </div>
      <div className="flex shrink-0 items-center gap-2">
        <Badge tone={levelTone[match.levelFit]}>{levelLabel[match.levelFit]}</Badge>
        <Badge tone={confTone(match.confidence)}>{match.confidence}% confidence</Badge>
      </div>
    </div>

    <p className="mt-2.5 text-base text-base-content/65">{match.rationale}</p>
    <p className="mt-1.5 text-base text-base-content/65">{match.levelNote}</p>

    <div className="mt-3.5">
      <div className="mb-1 flex items-center justify-between text-sm text-base-content/50">
        <span>Description covered by this class</span>
        <span className="font-semibold tnum">{match.coveredPct}%</span>
      </div>
      <Meter pct={match.coveredPct} />
    </div>

    {match.inClass.length > 0 || match.outOfClass.length > 0 ? (
      <div className="mt-3.5 grid grid-cols-1 gap-3 sm:grid-cols-2">
        <FunctionList items={match.inClass} label="In this class" tone="green" />
        <FunctionList items={match.outOfClass} label="Outside this class" tone="orange" />
      </div>
    ) : null}

    <div className="mt-4 flex flex-wrap items-center gap-3 border-t border-base-300 pt-3.5">
      <button
        className={`btn btn-sm ${primary ? 'btn-primary' : 'btn-outline'}`}
        disabled={startDisabled}
        onClick={onStart}
        type="button"
      >
        {starting ? 'Starting your JD…' : 'Start a JD from this class →'}
      </button>
      <span className="text-sm text-base-content/50">
        Uses this class&apos;s standard and merges in your description&apos;s work. Opens as a
        draft you can edit.
      </span>
    </div>
  </Card>
);

const FunctionList = ({
  items,
  label,
  tone,
}: {
  items: string[];
  label: string;
  tone: 'green' | 'orange';
}) => (
  <div>
    <div className="mb-1.5 text-sm font-semibold text-base-content/50">{label}</div>
    {items.length === 0 ? (
      <div className="text-base text-base-content/50">—</div>
    ) : (
      <ul className="space-y-1">
        {items.map((f) => (
          <li className="flex gap-1.5 text-base text-base-content/65" key={f}>
            <span className={tone === 'green' ? 'text-success' : 'text-error'}>
              {tone === 'green' ? '✓' : '•'}
            </span>
            <span>{f}</span>
          </li>
        ))}
      </ul>
    )}
  </div>
);
