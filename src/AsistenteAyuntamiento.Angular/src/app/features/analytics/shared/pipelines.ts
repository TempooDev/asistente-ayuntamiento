/** Spanish label and CSS colour variable of each pipeline; names are matched case-insensitively. */
const PIPELINES: Record<string, { label: string; colorVar: string }> = {
  baseline: { label: 'Baseline', colorVar: '--chart-baseline' },
  hierarchical: { label: 'Jerárquica', colorVar: '--chart-hierarchical' },
  recursive: { label: 'Recursiva', colorVar: '--chart-recursive' },
  semantic: { label: 'Semántica', colorVar: '--chart-semantic' },
  arena: { label: 'Arena', colorVar: '--chart-arena' },
  shadow: { label: 'Shadow', colorVar: '--chart-shadow' },
};

const FALLBACK_COLORS = ['#2563eb', '#d97706', '#059669', '#9333ea', '#db2777', '#64748b'];

export function pipelineLabel(pipeline: string): string {
  return PIPELINES[pipeline.toLowerCase()]?.label ?? pipeline;
}

export function pipelineColorVar(pipeline: string): string | null {
  return PIPELINES[pipeline.toLowerCase()]?.colorVar ?? null;
}

/** Stable fallback colour for pipelines without a CSS variable yet. */
export function pipelineFallbackColor(pipeline: string): string {
  const key = pipeline.toLowerCase();
  const known = Object.keys(PIPELINES).indexOf(key);
  if (known >= 0) return FALLBACK_COLORS[known];
  let hash = 0;
  for (const char of key) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return FALLBACK_COLORS[hash % FALLBACK_COLORS.length];
}
