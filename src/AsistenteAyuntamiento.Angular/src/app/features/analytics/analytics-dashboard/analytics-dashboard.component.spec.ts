import { signal } from '@angular/core';
import { ComponentFixture, DeferBlockBehavior, DeferBlockState, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
import { AnalyticsService } from '../data-access/analytics.service';
import {
  AiMetricsSummary,
  ArenaAnalyticsRequest,
  ArenaAnalyticsResponse,
  DateRange,
  EvaluationRunDetail,
  EvaluationRunSummary,
  IngestionUsage,
} from '../models/analytics.models';
import { AnalyticsDashboardComponent } from './analytics-dashboard.component';

const summary = (overrides: Partial<AiMetricsSummary> = {}): AiMetricsSummary => ({
  generatedAtUtc: '2026-10-13T00:00:00Z',
  from: '2026-09-23',
  to: '2026-10-23',
  totalCalls: 40,
  succeededCalls: 38,
  failedCalls: 2,
  successRate: 95,
  averageDurationMs: 800,
  latencyP50Ms: 2800,
  latencyP95Ms: 9100,
  ttftP50Ms: 850,
  totalInputTokens: 1000,
  totalOutputTokens: 500,
  totalTokens: 1500,
  averageTokensPerCall: 39.5,
  estimatedCostEur: 0.0123,
  unpricedModels: [],
  costByStep: [{ step: 'Respuesta', tokens: 1000, costEur: 0.01 }],
  daily: [
    { date: '2026-10-21', answers: 0, failed: 0, latencyP95Ms: null },
    { date: '2026-10-22', answers: 5, failed: 0, latencyP95Ms: 9000 },
  ],
  pipelines: [
    { pipeline: 'baseline', answers: 30, successRate: 96.7, latencyP50Ms: 2500, latencyP95Ms: 8000, ttftP50Ms: 700, averageTokensPerAnswer: 1200, costPer1000AnswersEur: 1.5 },
    { pipeline: 'arena', answers: 10, successRate: 90, latencyP50Ms: 3000, latencyP95Ms: 9500, ttftP50Ms: null, averageTokensPerAnswer: 1300, costPer1000AnswersEur: 1.7 },
  ],
  consumptions: [{ modelId: 'gpt-4o-mini', type: 'Respuesta', totalCalls: 40, succeededCalls: 38, averageDurationMs: 800, totalTokens: 1500, costEur: 0.0123 }],
  ...overrides,
});

const ingestion: IngestionUsage = {
  generatedAtUtc: '2026-10-13T00:00:00Z',
  from: '2026-09-23',
  to: '2026-10-23',
  documents: 12,
  enrichmentCalls: 30,
  enrichmentTokens: 4000,
  embeddedTokens: 9000,
  averageProcessingDurationMs: 1500,
  pipelines: [
    { pipeline: 'Baseline', documents: 10, processingP50Ms: 4000, processingP95Ms: 12000, averageChunks: 3.2, embeddedTokens: 5000, enrichmentCalls: 0, enrichmentTokens: 0, failedDocuments: 1 },
    { pipeline: 'Hierarchical', documents: 2, processingP50Ms: 6000, processingP95Ms: 15000, averageChunks: 5, embeddedTokens: 4000, enrichmentCalls: 30, enrichmentTokens: 4000, failedDocuments: 0 },
    { pipeline: 'Recursive', documents: 0, processingP50Ms: null, processingP95Ms: null, averageChunks: 0, embeddedTokens: 0, enrichmentCalls: 0, enrichmentTokens: 0, failedDocuments: 0 },
    { pipeline: 'Semantic', documents: 3, processingP50Ms: 9000, processingP95Ms: 20000, averageChunks: 4, embeddedTokens: 7000, enrichmentCalls: 0, enrichmentTokens: 0, failedDocuments: 0 },
  ],
};

const arena: ArenaAnalyticsResponse = {
  totalBattles: 11,
  pendingBattles: 1,
  completedBattles: 10,
  metrics: [
    { pipeline: 'Hierarchical', winCount: 6, lossCount: 2, tieCount: 1, bothBadCount: 1, winRate: 0.75, averageLatencyMs: 900, averageTokens: 300 },
  ],
};

const runSummary = (runId: string): EvaluationRunSummary => ({
  runId,
  startedAt: '2026-10-22T21:30:00Z',
  finishedAt: '2026-10-22T22:30:00Z',
  chatModel: 'qwen2.5:7b',
  embeddingModel: 'qwen3-embedding:0.6b',
  judgeModel: 'qwen2.5:7b',
  questionCount: 25,
  importedAt: '2026-10-22T23:00:00Z',
  results: [{ pipeline: 'Baseline', faithfulness: 0.8, contextRecall: 0.7 }],
});

const runDetail = (runId: string): EvaluationRunDetail => ({
  ...runSummary(runId),
  gitCommit: 'abc',
  seed: 42,
  queryInstruction: null,
  datasetHash: null,
  corpusHash: null,
  ragasVersion: '0.3.9',
  results: [
    {
      pipeline: 'Baseline',
      contextPrecision: 0.7, contextPrecisionStd: 0.2, contextRecall: 0.6, contextRecallStd: 0.2,
      faithfulness: 0.8, faithfulnessStd: 0.1, answerRelevancy: null, answerRelevancyStd: null,
      hallucinationRate: 0.2, hitAt1: 0.5, hitAt3: 0.7, hitAt5: 0.8, mrr: 0.62, noAnswerDetectedRate: null,
      failed: 0, missing: 1, latencyP50Ms: 5000, latencyP95Ms: 9000, ttftP50Ms: 900, ttftP95Ms: 1500, meanTokens: 1500,
      indexingP50Ms: 4000, indexingP95Ms: 9000, chunksPerDocument: 3.1, points: 600,
    },
  ],
});

class FakeAnalytics {
  summaryRanges: DateRange[] = [];
  ingestionRanges: DateRange[] = [];
  arenaRequests: ArenaAnalyticsRequest[] = [];
  runRequests: string[] = [];
  evaluationListCalls = 0;
  current = summary();
  runs: EvaluationRunSummary[] = [];
  runsError: HttpErrorResponse | null = null;

  getAiMetricsSummary(range: DateRange): Observable<AiMetricsSummary> {
    this.summaryRanges.push(range);
    return of(this.current);
  }
  getIngestionUsage(range: DateRange): Observable<IngestionUsage> {
    this.ingestionRanges.push(range);
    return of(ingestion);
  }
  getArenaAnalytics(filters: ArenaAnalyticsRequest): Observable<ArenaAnalyticsResponse> {
    this.arenaRequests.push(filters);
    return of(arena);
  }
  getEvaluationRuns(): Observable<EvaluationRunSummary[]> {
    this.evaluationListCalls++;
    return this.runsError ? throwError(() => this.runsError) : of(this.runs);
  }
  getEvaluationRun(runId: string): Observable<EvaluationRunDetail> {
    this.runRequests.push(runId);
    return of(runDetail(runId));
  }
}

describe('AnalyticsDashboardComponent', () => {
  let analytics: FakeAnalytics;
  let fixture: ComponentFixture<AnalyticsDashboardComponent>;

  beforeAll(() => {
    // jsdom has no canvas: Chart.js gets no context and draws nothing.
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);
  });

  const render = async (roles: { platform: boolean; organization: boolean }, setup?: (fake: FakeAnalytics) => void) => {
    analytics = new FakeAnalytics();
    setup?.(analytics);
    await TestBed.configureTestingModule({
      imports: [AnalyticsDashboardComponent],
      // The evaluation section is deferred until it scrolls into view; the tests render it directly.
      deferBlockBehavior: DeferBlockBehavior.Manual,
      providers: [
        provideTestDependencies(),
        { provide: AnalyticsService, useValue: analytics },
        { provide: AuthFacade, useValue: { isPlatformAdmin: signal(roles.platform), isOrganizationAdmin: signal(roles.organization) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AnalyticsDashboardComponent);
    fixture.detectChanges();
    for (const block of await fixture.getDeferBlocks()) {
      await block.render(DeferBlockState.Complete);
    }
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('shows the success rate as a percentage and labels the call counts', async () => {
    const page = await render({ platform: false, organization: true });

    const rate = page.querySelector('[data-testid="success-rate"]')!.textContent!;
    expect(rate).toContain('95 %');
    expect(rate).not.toMatch(/9[.,\s]?500/);
    expect(page.textContent).toContain('38 correctas');
    expect(page.textContent).toContain('2 con error');
  });

  it('requests the last 30 days and the previous 30 days by default', async () => {
    await render({ platform: false, organization: true });

    expect(analytics.summaryRanges).toHaveLength(2);
    const [current, previous] = analytics.summaryRanges;
    expect(previous.to).toBe(current.from);
    const days = (r: DateRange) => (Date.parse(r.to) - Date.parse(r.from)) / 86_400_000;
    expect(days(current)).toBe(30);
    expect(days(previous)).toBe(30);
  });

  it('shows "—" when no time to first token was recorded', async () => {
    const page = await render({ platform: false, organization: true }, fake => (fake.current = summary({ ttftP50Ms: null })));

    const ttft = page.querySelector('[data-testid="ttft-card"] [data-testid="metric-value"]')!;
    expect(ttft.textContent!.trim()).toBe('—');
  });

  it('labels the pipelines in Spanish and says when the cost is incomplete', async () => {
    const page = await render({ platform: false, organization: true }, fake => (fake.current = summary({ unpricedModels: ['gpt-x'] })));

    expect(page.textContent).toContain('Baseline');
    expect(page.textContent).toContain('Arena');
    expect(page.querySelector('[data-testid="unpriced-notice"]')!.textContent).toContain('gpt-x');
  });

  it('shows only the usage to organization administrators', async () => {
    const page = await render({ platform: false, organization: true });

    expect(analytics.ingestionRanges).toHaveLength(0);
    expect(analytics.evaluationListCalls).toBe(0);
    expect(page.textContent).toContain('Uso del asistente');
    expect(page.textContent).not.toContain('Ingesta');
    expect(page.textContent).not.toContain('Evaluación offline');
    expect(page.textContent).not.toContain('Resultados de la Arena');
  });

  it('shows ingestion, evaluation and Arena, but not the usage, to platform administrators', async () => {
    const page = await render({ platform: true, organization: false });

    expect(analytics.summaryRanges).toHaveLength(0);
    expect(analytics.ingestionRanges).toHaveLength(2);
    expect(page.textContent).not.toContain('Uso del asistente');
    // The recursive variant ingested nothing in the period, so its column is hidden.
    expect([...page.querySelectorAll('[data-testid="ingestion-pipeline"] h3')].map(h => h.textContent)).toEqual(['Baseline', 'Jerárquica', 'Semántica']);
    expect(page.textContent).toContain('Llamadas de enriquecimiento');
    expect(page.textContent).toContain('Jerárquica');
    expect(page.textContent).toContain('Evaluación offline');
    expect(page.textContent).toContain('Resultados de la Arena');
  });

  it('explains how to import a run when there is no evaluation', async () => {
    const page = await render({ platform: true, organization: false });

    const empty = page.querySelector('[data-testid="evaluation-empty"]')!;
    expect(empty.textContent).toContain('--import {runId}');
    expect(analytics.runRequests).toHaveLength(0);
  });

  it('keeps the rest of the page when the evaluations cannot be loaded', async () => {
    const page = await render({ platform: true, organization: true }, fake => (fake.runsError = new HttpErrorResponse({ status: 500 })));

    expect(page.textContent).toContain('Las evaluaciones no están disponibles ahora mismo');
    expect(page.querySelector('[data-testid="evaluation-empty"]')).toBeNull();
    expect(analytics.summaryRanges.length).toBeGreaterThan(0);
    expect(page.textContent).toContain('Respuestas');
  });

  it('treats an API without the evaluation endpoint as no runs', async () => {
    const page = await render({ platform: true, organization: false }, fake => (fake.runsError = new HttpErrorResponse({ status: 404 })));

    expect(page.querySelector('[data-testid="evaluation-empty"]')).not.toBeNull();
  });

  it('shows the newest run, with missing metrics as "—", and the history with two runs', async () => {
    const page = await render({ platform: true, organization: false }, fake => (fake.runs = [runSummary('run-2'), runSummary('run-1')]));

    expect(analytics.runRequests).toEqual(['run-2']);
    expect(page.querySelector('[data-testid="hallucination-rate"]')!.textContent).toContain('20 %');
    expect(page.textContent).toContain('—');
    expect(page.querySelector('[data-testid="evaluation-history"]')).not.toBeNull();
  });

  it('hides the history chart with a single run', async () => {
    const page = await render({ platform: true, organization: false }, fake => (fake.runs = [runSummary('run-1')]));

    expect(page.querySelector('[data-testid="evaluation-history"]')).toBeNull();
  });

  it('keeps the Arena collapsed and requests it only when expanded, for the selected period', async () => {
    const page = await render({ platform: true, organization: false });

    expect(analytics.arenaRequests).toHaveLength(0);
    expect(page.textContent).not.toContain('Ambas malas');

    (page.querySelector('[data-testid="arena-toggle"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(analytics.arenaRequests).toHaveLength(1);
    expect(analytics.arenaRequests[0]).toEqual({ startDate: analytics.ingestionRanges[0].from, endDate: analytics.ingestionRanges[0].to });
    expect(page.textContent).toContain('Ambas malas');
    expect(page.textContent).toContain('Tasa de victorias');
  });

  it('requests every period-dependent section again for the last 7 days', async () => {
    const page = await render({ platform: true, organization: true });
    const select = page.querySelector('[data-testid="period-selector"] select') as HTMLSelectElement;

    select.value = '7d';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(analytics.summaryRanges).toHaveLength(4);
    expect(analytics.ingestionRanges).toHaveLength(4);
    const latest = analytics.summaryRanges[2];
    expect((Date.parse(latest.to) - Date.parse(latest.from)) / 86_400_000).toBe(7);
    expect(analytics.evaluationListCalls).toBe(1);
  });
});
