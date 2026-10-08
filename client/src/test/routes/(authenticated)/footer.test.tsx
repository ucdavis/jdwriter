import { renderRoute, setupRouteTest } from '@/test/renderRoute.tsx';
import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import '@/test/mswUtils.ts';

describe('app footer', () => {
  setupRouteTest();

  it('credits CRU and links to help for JDWriter, under the CAES lockup', async () => {
    renderRoute({ initialPath: '/' });

    const footer = await screen.findByRole('contentinfo');
    expect(
      within(footer).getByRole('img', {
        name: 'UC Davis College of Agricultural and Environmental Sciences',
      })
    ).toHaveAttribute('src', '/caes.svg');
    expect(footer).toHaveTextContent(/Created in association with CRU\s*\|\s*Help/);
    expect(within(footer).getByRole('link', { name: 'CRU' })).toHaveAttribute(
      'href',
      'https://computing.caes.ucdavis.edu/'
    );
    expect(within(footer).getByRole('link', { name: 'Help' })).toHaveAttribute(
      'href',
      'https://caeshelp.ucdavis.edu/?appname=JDWriter'
    );
  });

  it('keeps the corner artwork out of the accessibility tree', async () => {
    renderRoute({ initialPath: '/' });

    const footer = await screen.findByRole('contentinfo');
    // Only the lockup is announced; the writing-tool art is decoration.
    expect(within(footer).getAllByRole('img')).toHaveLength(1);
  });
});
