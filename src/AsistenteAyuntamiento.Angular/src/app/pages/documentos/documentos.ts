import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { IngestionService, BlobInfo } from '../../services/ingestion';

@Component({
  selector: 'app-documentos',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './documentos.html',
  styleUrl: './documentos.scss'
})
export class DocumentosComponent implements OnInit {
  private ingestionService = inject(IngestionService);

  blobs = signal<BlobInfo[] | null>(null);
  statusMessage = signal('');
  
  // Filters
  searchTerm = signal('');
  filterStatus = signal('Todos');
  pageSize = signal(20);
  filterDateFrom = signal<string | null>(null);
  filterDateTo = signal<string | null>(null);
  filterMinSize = signal<number | null>(null);
  filterMaxSize = signal<number | null>(null);
  
  currentPage = signal(1);

  // Computed state for pagination and filtering
  filteredBlobs = computed(() => {
    const currentBlobs = this.blobs();
    if (!currentBlobs) return [];
    
    const search = this.searchTerm().toLowerCase();
    const status = this.filterStatus();
    const dFrom = this.filterDateFrom() ? new Date(this.filterDateFrom()!) : null;
    const dTo = this.filterDateTo() ? new Date(this.filterDateTo()!) : null;
    const minSize = this.filterMinSize();
    const maxSize = this.filterMaxSize();

    return currentBlobs.filter(b => {
      // Name match
      if (search && !b.name.toLowerCase().includes(search)) return false;
      
      // Status match
      if (status !== 'Todos') {
        if (status === 'Procesados' && b.status !== 'Completed') return false;
        if (status === 'Pendientes' && !['Pending', 'Failed', 'Processing'].includes(b.status)) return false;
      }
      
      // Date match
      if (b.lastModified) {
        const dDate = new Date(b.lastModified);
        dDate.setHours(0,0,0,0);
        if (dFrom) {
          const fromDate = new Date(dFrom);
          fromDate.setHours(0,0,0,0);
          if (dDate < fromDate) return false;
        }
        if (dTo) {
          const toDate = new Date(dTo);
          toDate.setHours(0,0,0,0);
          if (dDate > toDate) return false;
        }
      } else if (dFrom || dTo) {
        return false;
      }

      // Size match
      const sizeKB = Math.round(b.size / 1024);
      if (minSize !== null && sizeKB < minSize) return false;
      if (maxSize !== null && sizeKB > maxSize) return false;

      return true;
    });
  });

  totalPages = computed(() => Math.ceil(this.filteredBlobs().length / this.pageSize()));

  pagedBlobs = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize();
    return this.filteredBlobs().slice(start, start + this.pageSize());
  });

  async ngOnInit() {
    await this.loadBlobs();
  }

  // Effect-like behavior: when a filter changes, reset to page 1
  onFilterChange() {
    this.currentPage.set(1);
  }

  async loadBlobs() {
    try {
      const data = await this.ingestionService.getBlobs();
      this.blobs.set(data);
    } catch (ex: any) {
      this.statusMessage.set(`Error al cargar: ${ex.message || ex}`);
    }
  }

  async processBlob(blobName: string) {
    try {
      this.statusMessage.set(`Procesando ${blobName}... (esto puede tardar unos minutos)`);
      
      const parts = blobName.split('/');
      const source = parts.length > 1 ? parts[1] : 'Unknown';
      
      const result = await this.ingestionService.processBlob(blobName, source);
      this.statusMessage.set(result);
      await this.loadBlobs();
    } catch (ex: any) {
      this.statusMessage.set(`Error: ${ex.message || ex}`);
    }
  }

  changePage(newPage: number) {
    if (newPage >= 1 && newPage <= this.totalPages()) {
      this.currentPage.set(newPage);
    }
  }
  
  // Helpers for template
  getDocUrl(blobName: string): string | null {
    const parts = blobName.split('/');
    const docId = parts.length > 0 ? parts[parts.length - 1].replace('.json', '') : '';
    const source = parts.length > 1 ? parts[1] : '';
    if (source === 'BOE') {
      return `https://www.boe.es/buscar/doc.php?id=${docId}`;
    }
    return null;
  }
  
  getSizeKb(sizeBytes: number): number {
    return Math.round(sizeBytes / 1024);
  }
}
