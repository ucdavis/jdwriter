import { AddRow, Badge, Card, Eyebrow } from '@/shared/ui/primitives.tsx';
import { useState, type Dispatch, type SetStateAction } from 'react';
import type { Item } from './useBuildState.ts';

/**
 * A keep/drop checklist over one qualification section, plus a guarded add row.
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
}: {
  /** Why an addition is refused, or null when it may be added. */
  guardAdd: (t: string) => string | null;
  items: Item[];
  setItems: Dispatch<SetStateAction<Item[]>>;
  title: string;
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
        <p className="mt-2 text-base text-base-content/50">
          None in the standard for this class.
        </p>
      ) : null}
      <ul className="mt-2.5 space-y-1.5">
        {items.map((it, i) => (
          <li className="flex items-start gap-2.5" key={`${it.text}-${i}`}>
            <label className="flex flex-1 cursor-pointer items-start gap-2.5">
              <input
                checked={it.kept}
                className="checkbox checkbox-sm mt-0.5"
                onChange={() =>
                  setItems((arr) =>
                    arr.map((x, j) => (j === i ? { ...x, kept: !x.kept } : x))
                  )
                }
                type="checkbox"
              />
              <span
                className={`text-base ${
                  it.kept ? '' : 'text-base-content/50 line-through'
                }`}
              >
                {it.added ? <Badge tone="green">+</Badge> : null} {it.text}
              </span>
            </label>
            {it.added ? (
              <button
                aria-label="Remove added item"
                className="text-base text-base-content/50 hover:text-error"
                onClick={() => setItems((arr) => arr.filter((_, j) => j !== i))}
                type="button"
              >
                ✕
              </button>
            ) : null}
          </li>
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
  <p className="mt-2 text-base text-warning" data-testid="add-refusal" role="status">
    {text}
  </p>
);
