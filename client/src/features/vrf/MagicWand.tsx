import { messageOf } from '@/features/browse/NlIntake.tsx';
import { SlowNote } from '@/shared/ui/primitives.tsx';
import { useVrfNarrative, type VrfContext } from '@/queries/vrf.ts';
import { useState } from 'react';
import type { NarrativeKey } from './vrfModel.ts';

/** How many words of notes before the wand appears: enough to build on. */
const WORDS = 3;

const words = (s: string) => s.trim().split(/\s+/).filter(Boolean).length;

/**
 * The magic wand under a narrative: once the author has written a few words, it drafts a paragraph
 * from them and the position. The draft replaces the notes, which "Undo" puts back.
 */
export const MagicWand = ({
  context,
  field,
  label,
  onChange,
  value,
}: {
  context: VrfContext;
  field: NarrativeKey;
  label: string;
  onChange: (text: string) => void;
  value: string;
}) => {
  const draft = useVrfNarrative();
  const [notes, setNotes] = useState<string | null>(null);

  if (words(value) < WORDS && notes === null) {
    return (
      <p className="mt-1 text-sm text-base-content/70">
        Jot down a few words — the reasons, in your own shorthand — and ✨ can draft the paragraph from them.
      </p>
    );
  }

  return (
    <div className="mt-1 flex flex-wrap items-center gap-2">
      <button
        aria-label={`Draft the ${label} paragraph from my notes`}
        className="btn btn-sm border-warning/60 bg-warning/10 text-warning-ink hover:bg-warning/20"
        disabled={draft.isPending}
        onClick={() =>
          draft.mutate(
            { context, field, notes: value },
            {
              onSuccess: (r) => {
                setNotes(value);
                onChange(r.paragraph);
              },
            }
          )
        }
        type="button"
      >
        {draft.isPending ? 'Drafting…' : notes === null ? '✨ Draft it from my notes' : '✨ Draft again'}
      </button>
      <SlowNote show={draft.isPending} />
      {notes !== null && !draft.isPending ? (
        <>
          <span className="text-sm text-base-content/70">Drafted by AI from your notes — read it and make it yours.</span>
          <button
            className="text-sm text-primary underline"
            onClick={() => {
              onChange(notes);
              setNotes(null);
            }}
            type="button"
          >
            Undo
          </button>
        </>
      ) : null}
      {draft.error ? (
        <span className="text-sm text-error" role="alert">
          {messageOf(draft.error)}
        </span>
      ) : null}
    </div>
  );
};
