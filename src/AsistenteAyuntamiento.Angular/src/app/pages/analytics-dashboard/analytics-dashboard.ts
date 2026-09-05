import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ArenaService, ArenaAnalyticsRequest } from '../../services/arena/arena.service';
import { MetricsService, AiMetricsSummary } from '../../services/metrics/metrics.service';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './analytics-dashboard.html'
})
export class AnalyticsDashboardComponent implements OnInit {
  private arenaService = inject(ArenaService);
  private metricsService = inject(MetricsService);
  
  // Date filters
  startDate = signal<string>('');
  endDate = signal<string>('');
  
  // Segmentation filter
  selectedPipeline = signal<string>('ALL');

  // AI Metrics
  aiMetrics = signal<AiMetricsSummary | null>(null);

  ngOnInit() {
    this.metricsService.getAiMetricsSummary().subscribe(data => {
      this.aiMetrics.set(data);
    });
  }

  // Trigger for refetching
  private filterTrigger = computed(() => {
    return {
      startDate: this.startDate(),
      endDate: this.endDate()
    } as ArenaAnalyticsRequest;
  });

  // Reactive data fetching
  analytics = toSignal(
    toObservable(this.filterTrigger).pipe(
      switchMap(filters => this.arenaService.getAnalytics(filters))
    )
  );

  // Filtered metrics for the table
  filteredMetrics = computed(() => {
    const data = this.analytics();
    if (!data) return [];
    
    if (this.selectedPipeline() === 'ALL') {
      return data.metrics;
    }
    
    return data.metrics.filter((m: any) => m.pipeline === this.selectedPipeline());
  });

  // Available pipelines for the dropdown
  availablePipelines = computed(() => {
    const data = this.analytics();
    if (!data) return [];
    return data.metrics.map((m: any) => m.pipeline);
  });
}

