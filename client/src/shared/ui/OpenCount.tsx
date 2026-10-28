/** How many reports are waiting, as a small pill: said in full to a screen reader. */
export const OpenCount = ({ open }: { open: number }) =>
  open > 0 ? (
    <span className="ml-1.5 inline-flex min-w-5 items-center justify-center rounded-full bg-ucd-gold px-1.5 text-sm font-bold text-ucd-blue">
      <span aria-hidden>{open}</span>
      <span className="sr-only">{open === 1 ? ', 1 open report' : `, ${open} open reports`}</span>
    </span>
  ) : null;
