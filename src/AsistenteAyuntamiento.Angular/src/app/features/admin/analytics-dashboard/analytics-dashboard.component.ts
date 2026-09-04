import { Component, inject, signal, computed } from '@angular/common';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ArenaService, ArenaAnalyticsRequest } from '../../../core/services/arena.service';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './analytics-dashboard.component.html'
})
export class AnalyticsDashboardComponent {
  private arenaService = inject(ArenaService);
  
  // Date filters
  startDate = signal<string>('');
  endDate = signal<string>('');
  
  // Segmentation filter
  selectedPipeline = signal<string>('ALL');

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
    
    return data.metrics.filter(m => m.pipeline === this.selectedPipeline());
  });

  // Available pipelines for the dropdown
  availablePipelines = computed(() => {
    const data = this.analytics();
    if (!data) return [];
    return data.metrics.map(m => m.pipeline);
  });
}
