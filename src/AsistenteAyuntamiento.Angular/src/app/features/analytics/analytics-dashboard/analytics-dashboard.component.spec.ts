import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
import { AnalyticsService } from '../data-access/analytics.service';
import { AiMetricsSummary, ArenaAnalyticsResponse, IngestionUsage } from '../models/analytics.models';
import { AnalyticsDashboardComponent } from './analytics-dashboard.component';

const summary: AiMetricsSummary = {
  generatedAtUtc: '2026-10-13T00:00:00Z',
  totalCalls: 40,
  succeededCalls: 38,
  failedCalls: 2,
  successRate: 95,
  averageDurationMs: 800,
  totalInputTokens: 1000,
  totalOutputTokens: 500,
  totalTokens: 1500,
  averageTokensPerCall: 39.5,
  consumptions: [{ modelId: 'gpt-4o-mini', type: 'Chat', totalCalls: 40, succeededCalls: 38, averageDurationMs: 800, totalTokens: 1500 }],
};

const ingestion: IngestionUsage = {
  generatedAtUtc: '2026-10-13T00:00:00Z',
  documents: 12,
  enrichmentCalls: 30,
  enrichmentTokens: 4000,
  embeddedTokens: 9000,
  averageProcessingDurationMs: 1500,
};

const arena: ArenaAnalyticsResponse = {
  totalBattles: 11,
  pendingBattles: 1,
  completedBattles: 10,
  metrics: [
    { pipeline: 'Hierarchical', winCount: 6, lossCount: 2, tieCount: 1, bothBadCount: 1, winRate: 0.75, averageLatencyMs: 900, averageTokens: 300 },
  ],
};

class FakeAnalytics {
  ingestionCalls = 0;
  getAiMetricsSummary(): Observable<AiMetricsSummary> {
    return of(summary);
  }
  getIngestionUsage(): Observable<IngestionUsage> {
    this.ingestionCalls++;
    return of(ingestion);
  }
  getArenaAnalytics(): Observable<ArenaAnalyticsResponse> {
    return of(arena);
  }
}

describe('AnalyticsDashboardComponent', () => {
  let analytics: FakeAnalytics;

  const render = async (roles: { platform: boolean; organization: boolean }) => {
    analytics = new FakeAnalytics();
    await TestBed.configureTestingModule({
      imports: [AnalyticsDashboardComponent],
      providers: [
        provideTestDependencies(),
        { provide: AnalyticsService, useValue: analytics },
        { provide: AuthFacade, useValue: { isPlatformAdmin: signal(roles.platform), isOrganizationAdmin: signal(roles.organization) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalyticsDashboardComponent);
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
    expect(page.textContent).not.toContain('Llamadas (Éxito)');
  });

  it('does not request or show the platform ingestion cost to organization administrators', async () => {
    const page = await render({ platform: false, organization: true });

    expect(analytics.ingestionCalls).toBe(0);
    expect(page.textContent).not.toContain('Consumo de ingesta');
  });

  it('shows the ingestion cost and the "both bad" votes to platform administrators', async () => {
    const page = await render({ platform: true, organization: false });

    expect(analytics.ingestionCalls).toBe(1);
    expect(page.textContent).toContain('Llamadas de enriquecimiento');
    expect(page.textContent).toContain('Ambas malas');
    expect(page.textContent).toContain('Tasa de victorias');
  });
});
