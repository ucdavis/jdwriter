import { asset } from '@/lib/basePath.ts';

/**
 * The CAES People footer: the college lockup, who built it, and where to get help. It sits on the
 * page's own gray, with no band or rule of its own, as the other CAES apps' footers do. The corner art
 * is JDWriter's own — writing tools, gold on the left and blues on the right — in the layout the
 * other CAES apps share (Leaves has leaves), so the family reads as one while each app is its own.
 */
export const AppFooter = () => (
  <footer className="relative mt-16 overflow-hidden">
    {/* Decoration only: hidden from assistive tech, and dropped on narrow screens where it would
        crowd the logo. */}
    <img
      alt=""
      aria-hidden
      className="pointer-events-none absolute bottom-0 left-0 hidden h-[200px] w-auto select-none md:block"
      src={asset('tools-gold.svg')}
    />
    <img
      alt=""
      aria-hidden
      className="pointer-events-none absolute bottom-0 right-0 hidden h-[200px] w-auto select-none md:block"
      src={asset('tools-blue.svg')}
    />
    <div className="relative mx-auto flex min-h-[220px] max-w-[1100px] flex-col items-center justify-center gap-3 px-8 py-10">
      <a href="https://caes.ucdavis.edu/" rel="noreferrer" target="_blank">
        <img
          alt="UC Davis College of Agricultural and Environmental Sciences"
          className="h-14 w-auto"
          src={asset('caes.svg')}
        />
      </a>
      <p className="text-base text-base-content/60">
        Created in association with{' '}
        <a
          className="underline hover:text-primary"
          href="https://computing.caes.ucdavis.edu/"
          rel="noreferrer"
          target="_blank"
        >
          CRU
        </a>
        <span aria-hidden className="mx-2 text-base-content/30">
          |
        </span>
        <a
          className="underline hover:text-primary"
          href="https://caeshelp.ucdavis.edu/?appname=JDWriter"
          rel="noreferrer"
          target="_blank"
        >
          Help
        </a>
      </p>
    </div>
  </footer>
);
