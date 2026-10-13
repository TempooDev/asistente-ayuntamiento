import { TestBed } from '@angular/core/testing';
import { MarkdownService } from './markdown.service';
import { MarkdownPipe } from './markdown.pipe';

describe('MarkdownService', () => {
  let markdown: MarkdownService;

  beforeEach(() => {
    markdown = TestBed.inject(MarkdownService);
  });

  it('renders Markdown', () => {
    expect(markdown.render('**Plazo**: 10 días')).toContain('<strong>Plazo</strong>');
  });

  it('removes scripts and event handlers', () => {
    const html = markdown.render('<img src=x onerror="alert(1)"><script>alert(2)</script>');

    expect(html).not.toContain('onerror');
    expect(html).not.toContain('<script');
  });

  it('opens links in a new tab without opener', () => {
    const html = markdown.render('[BOE](https://www.boe.es)');

    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noopener noreferrer"');
  });

  it('registers the sanitizer hook only once', () => {
    TestBed.inject(MarkdownService);
    new MarkdownService();

    const html = markdown.render('[a](https://a.es)');
    expect(html.match(/target=/g)).toHaveLength(1);
  });

  it('treats empty input as empty output', () => {
    expect(markdown.render(undefined)).toBe('');
    expect(TestBed.runInInjectionContext(() => new MarkdownPipe()).transform('')).toBe('');
  });
});
