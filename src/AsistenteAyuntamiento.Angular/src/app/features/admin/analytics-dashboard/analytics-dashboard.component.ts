import { Component, inject } from '@angular/common';
import { CommonModule } from '@angular/common';
import { ArenaService } from '../../../core/services/arena.service';
import { toSignal } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './analytics-dashboard.component.html'
})
export class AnalyticsDashboardComponent {
  private arenaService = inject(ArenaService);
  
  analytics = toSignal(this.arenaService.getAnalytics());
}
