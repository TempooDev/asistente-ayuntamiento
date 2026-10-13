import { documentIdOf, publicUrlOf, sourceOf } from './document-table.component';

describe('document blob helpers', () => {
  it('reads the source and id from the blob path', () => {
    expect(sourceOf('json/BOE/BOE-A-2026-1.json')).toBe('BOE');
    expect(documentIdOf('json/BOE/BOE-A-2026-1.json')).toBe('BOE-A-2026-1');
  });

  it('links BOE and BOJA documents to their public pages', () => {
    expect(publicUrlOf('json/BOE/BOE-A-2026-1.json')).toBe('https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1');
    expect(publicUrlOf('json/BOJA/BOJA-2026-130-4.json')).toBe('https://www.juntadeandalucia.es/boja/2026/130/4.html');
  });

  it('has no link for unknown formats', () => {
    expect(publicUrlOf('json/BOJA/raro.json')).toBeNull();
    expect(publicUrlOf('json/OTRO/x.json')).toBeNull();
  });
});
