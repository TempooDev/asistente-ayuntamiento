import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnalyticsService } from '../data-access/analytics.service';
import { AiMetricsSummary, ArenaAnalyticsRequest } from '../models/analytics.models';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './analytics-dashboard.component.html'
})
export class AnalyticsDashboardComponent implements OnInit {
  private analyticsService = inject(AnalyticsService);
  
  // Date filters
  startDate = signal<string>('');
  endDate = signal<string>('');
  
  // Segmentation filter
  selectedPipeline = signal<string>('ALL');

  // AI Metrics
  aiMetrics = signal<AiMetricsSummary | null>(null);

  ngOnInit() {
    this.analyticsService.getAiMetricsSummary().subscribe(data => {
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
      switchMap(filters => this.analyticsService.getArenaAnalytics(filters))
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

