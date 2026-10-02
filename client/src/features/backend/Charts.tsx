import { useState } from 'react';

/**
 * Small, dependency-free charts for the admin dashboard. One hue (the theme's primary),
 * thin marks with a 4px rounded data end, a hairline baseline, values in text tokens —
 * never in the bar colour — and a hover/focus tooltip on every mark. Each chart is paired
 * with a table view by its caller.
 */

/** Columns over time. Labels only the latest and the peak; the tooltip carries the rest. */
export const ColumnChart = ({
  data,
  label,
  valueName,
}: {
  data: Array<{ key: string; label: string; tick: string; value: number }>;
  label: string;
  valueName: string;
}) => {
  const [active, setActive] = useState<string | null>(null);
  const max = Math.max(1, ...data.map((d) => d.value));
  const peak = data.reduce((best, d) => (d.value > best.value ? d : best), data[0]);
  const last = data.at(-1);

  return (
    <figure aria-label={label} className="mt-3" role="group">
      {/* Top padding is headroom for the tooltip, so it never leaves the chart. */}
      <div className="relative flex h-48 items-end gap-1 border-b border-base-300 pt-10">
        {data.map((d) => {
          const showValue = d.key === last?.key || (d.key === peak?.key && d.value > 0);
          const isActive = active === d.key;
          return (
            <button
              aria-label={`${d.label}: ${d.value} ${valueName}`}
              className="group relative flex h-full flex-1 items-end justify-center focus:outline-none"
              key={d.key}
              onBlur={() => setActive(null)}
              onFocus={() => setActive(d.key)}
              onMouseEnter={() => setActive(d.key)}
              onMouseLeave={() => setActive(null)}
              type="button"
            >
              {showValue ? (
                <span
                  className="absolute text-[11px] font-medium text-base-content/70 tnum"
                  style={{ bottom: `calc(${(d.value / max) * 100}% + 2px)` }}
                >
                  {d.value}
                </span>
              ) : null}
              <span
                className={`block w-full max-w-[24px] rounded-t-[4px] ${
                  isActive ? 'bg-primary' : 'bg-primary/80'
                } group-focus-visible:ring-2 group-focus-visible:ring-primary/40`}
                style={{ height: d.value === 0 ? '1px' : `${(d.value / max) * 100}%` }}
              />
              {isActive ? (
                <span
                  className="pointer-events-none absolute z-10 whitespace-nowrap rounded-md bg-base-content px-2 py-1 text-[11px] text-base-100 shadow"
                  role="tooltip"
                  style={{ bottom: `calc(${(d.value / max) * 100}% + 20px)` }}
                >
                  {d.label}: {d.value} {valueName}
                </span>
              ) : null}
            </button>
          );
        })}
      </div>
      <div className="mt-1 flex gap-1">
        {data.map((d) => (
          <span className="flex-1 text-center text-[10px] text-base-content/50" key={d.key}>
            {d.tick}
          </span>
        ))}
      </div>
    </figure>
  );
};

/** Horizontal bars, value at the tip. For ranked lists and bucket counts. */
export const BarList = ({
  data,
  label,
  valueName,
}: {
  data: Array<{ key: string; label: string; value: number }>;
  label: string;
  valueName: string;
}) => {
  const max = Math.max(1, ...data.map((d) => d.value));
  return (
    <ul aria-label={label} className="mt-3 space-y-2">
      {data.map((d) => (
        <li
          className="grid grid-cols-[minmax(0,12rem)_1fr_2.5rem] items-center gap-3"
          key={d.key}
          title={`${d.label}: ${d.value} ${valueName}`}
        >
          <span className="truncate text-[12.5px]">{d.label}</span>
          {/* The bar is a share of its own track, so small values stay visible; the value
              sits outside the track, at the tip, in a text token. */}
          <span className="block h-3">
            <span
              className="block h-3 rounded-r-[4px] bg-primary/80"
              style={{ minWidth: d.value > 0 ? '3px' : '1px', width: `${(d.value / max) * 100}%` }}
            />
          </span>
          <span className="text-[12px] font-medium text-base-content/70 tnum">{d.value}</span>
        </li>
      ))}
    </ul>
  );
};
