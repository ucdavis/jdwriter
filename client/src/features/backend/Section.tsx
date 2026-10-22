import type { ReactNode } from 'react';

/** A titled section of a back-end page. */
export const Section = ({
  children,
  id,
  sub,
  title,
}: {
  children: ReactNode;
  id: string;
  sub: string;
  title: string;
}) => (
  <section aria-labelledby={`${id}-heading`} className="mb-8">
    <h2 className="text-xl font-bold" id={`${id}-heading`}>
      {title}
    </h2>
    <p className="mb-3 mt-0.5 text-base text-base-content/70">{sub}</p>
    {children}
  </section>
);
