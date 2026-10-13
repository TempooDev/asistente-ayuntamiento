import { Injectable, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';

export type Theme = 'light' | 'dark';
const THEME_KEY = 'theme';

/** Light/dark theme, persisted per device and restored at startup. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly root = inject(DOCUMENT).documentElement;
  readonly theme = signal<Theme>('light');

  /** Applies the stored theme, or the system preference when none is stored. */
  restore(): void {
    let stored: string | null = null;
    try {
      stored = localStorage.getItem(THEME_KEY);
    } catch {
      // Storage unavailable: use the system preference.
    }
    const prefersDark = typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;
    this.apply(stored === 'dark' || stored === 'light' ? stored : prefersDark ? 'dark' : 'light');
  }

  toggle(): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.apply(next);
    try {
      localStorage.setItem(THEME_KEY, next);
    } catch {
      // Not persisted; the theme still changes for this session.
    }
  }

  private apply(theme: Theme): void {
    this.theme.set(theme);
    this.root.setAttribute('data-theme', theme);
    this.root.classList.toggle('dark', theme === 'dark');
  }
}
