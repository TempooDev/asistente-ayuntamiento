import { Injectable } from '@angular/core';
import DOMPurify from 'dompurify';
import { marked } from 'marked';

let hooksRegistered = false;

/** Links in answers open in a new tab without access to this window. Registered once per page. */
function registerHooks(): void {
  if (hooksRegistered) return;
  hooksRegistered = true;
  DOMPurify.addHook('afterSanitizeAttributes', node => {
    if (node.tagName === 'A') {
      node.setAttribute('target', '_blank');
      node.setAttribute('rel', 'noopener noreferrer');
    }
  });
}

/** Renders model answers (Markdown) to sanitized HTML. */
@Injectable({ providedIn: 'root' })
export class MarkdownService {
  constructor() {
    registerHooks();
  }

  render(markdown: string | null | undefined): string {
    if (!markdown) return '';
    const html = marked.parse(markdown, { async: false }) as string;
    return DOMPurify.sanitize(html, { ADD_ATTR: ['target'] });
  }
}
