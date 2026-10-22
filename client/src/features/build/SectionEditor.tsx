import { AddRow, Badge, Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import { useState, type Dispatch, type SetStateAction } from 'react';
import { LineEditor } from './LineEditor.tsx';
import type { CheckedChange } from '@/lib/contracts.ts';
import type { Item } from './useBuildState.ts';

/**
 * One qualification section: each line kept or dropped by its checkbox and editable in place,
 * plus a guarded add row.
 *
 * Only ADDED items can be deleted outright. A standard item is unchecked rather than
 * removed, so the author can always see what the class expects and put it back — which
 * is the difference between tailoring a standard and rewriting one.
 */
export const SectionEditor = ({
  guardAdd,
  items,
  setItems,
  title,
  verdicts,
}: {
  /** Why an addition is refused, or null when it may be added. */
  guardAdd: (t: string) => string | null;
  items: Item[];
  setItems: Dispatch<SetStateAction<Item[]>>;
  title: string;
  /** The current check's verdict on each added or edited line, by its text. */
  verdicts?: Map<string, CheckedChange>;
}) => {
  const [draft, setDraft] = useState('');
  const [refusal, setRefusal] = useState<string | null>(null);
  const keptCount = items.filter((i) => i.kept).length;

  const add = () => {
    const v = draft.trim();
    if (!v) {
      return;
    }
    const refused = guardAdd(v);
    setRefusal(refused);
    if (refused) {
      return;
    }
    setItems((arr) => [...arr, { added: true, kept: true, text: v }]);
    setDraft('');
  };

  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <Eyebrow>{title}</Eyebrow>
        <Badge tone="muted">{keptCount} selected</Badge>
      </div>
      {items.length === 0 ? (
        <p className="mt-2 text-base text-base-content/70">
          None in the standard for this class.
        </p>
      ) : null}
      <ul className="mt-2.5 space-y-1.5">
        {items.map((it, i) => (
          <LineEditor
            item={it}
            // By position, not text: the text changes as it's edited, and a key that changes
            // with every keystroke would remount the box and lose the cursor.
            key={i}
            label={it.original ?? it.text}
            onChange={(next) => setItems((arr) => arr.map((x, j) => (j === i ? next : x)))}
            onRemove={() => setItems((arr) => arr.filter((_, j) => j !== i))}
            verdict={it.kept ? verdicts?.get(it.text.trim()) : undefined}
          />
        ))}
      </ul>
      <AddRow
        onAdd={add}
        onChange={(v) => {
          setDraft(v);
          setRefusal(null);
        }}
        placeholder={`Add to ${title.toLowerCase()}…`}
        value={draft}
      />
      {refusal ? <AddRefusal text={refusal} /> : null}
    </Card>
  );
};

/** Why an addition was not included, shown under the box it was typed in. */
export const AddRefusal = ({ text }: { text: string }) => (
  <p className="mt-2 text-base text-warning-ink" data-testid="add-refusal" role="status">
    {text}
  </p>
);
