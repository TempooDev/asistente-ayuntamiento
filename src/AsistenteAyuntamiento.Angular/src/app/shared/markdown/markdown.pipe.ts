import { Pipe, PipeTransform, inject } from '@angular/core';
import { MarkdownService } from './markdown.service';

/** {{ text | markdown }} via [innerHTML]: sanitized HTML of a Markdown string. Pure: re-renders only on change. */
@Pipe({ name: 'markdown' })
export class MarkdownPipe implements PipeTransform {
  private readonly markdown = inject(MarkdownService);

  transform(value: string | null | undefined): string {
    return this.markdown.render(value);
  }
}
