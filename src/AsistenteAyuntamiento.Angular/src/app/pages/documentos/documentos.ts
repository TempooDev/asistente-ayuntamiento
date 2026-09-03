import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { IngestionService, BlobInfo, IngestionStatus, PaginatedBlobsResponse } from '../../services/ingestion/ingestion.service';
import { DocumentSource } from '../../services/config/scraper-filter.service';

@Component({
  selector: 'app-documentos',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './documentos.html',
  styleUrl: './documentos.scss'
})
export class DocumentosComponent implements OnInit {
  private ingestionService = inject(IngestionService);

  blobs = signal<BlobInfo[]>([]);
  stats = signal<PaginatedBlobsResponse['stats'] | null>(null);
  totalFilteredCount = signal(0);
  statusMessage = signal('');
  isLoading = signal(false);
  
  // Filters
  searchTerm = signal('');
  filterStatus = signal('Pendientes');
  pageSize = signal(20);
  filterDateFrom = signal<string | null>(null);
  filterDateTo = signal<string | null>(null);
  filterMinSize = signal<number | null>(null);
  filterMaxSize = signal<number | null>(null);
  
  currentPage = signal(1);

  // Expose enum to template
  IngestionStatus = IngestionStatus;

  // Stats
  pendingCount = computed(() => this.stats()?.pending || 0);
  processingCount = computed(() => this.stats()?.processing || 0);
  completedCount = computed(() => this.stats()?.completed || 0);
  totalCount = computed(() => this.stats()?.total || 0);

  totalPages = computed(() => Math.ceil(this.totalFilteredCount() / this.pageSize()) || 1);

  async ngOnInit() {
    await this.loadBlobs();
  }

  async onFilterChange() {
    this.currentPage.set(1);
    await this.loadBlobs();
  }

  async changePage(newPage: number) {
    if (newPage >= 1 && newPage <= this.totalPages()) {
      this.currentPage.set(newPage);
      await this.loadBlobs();
    }
  }

  async loadBlobs() {
    try {
      this.isLoading.set(true); // Show loading only for the list
      const data = await this.ingestionService.getBlobs({
          page: this.currentPage(),
          pageSize: this.pageSize(),
          status: this.filterStatus(),
          search: this.searchTerm(),
          dateFrom: this.filterDateFrom(),
          dateTo: this.filterDateTo(),
          minSizeKb: this.filterMinSize(),
          maxSizeKb: this.filterMaxSize()
      });
      this.blobs.set(data.items);
      this.stats.set(data.stats);
      this.totalFilteredCount.set(data.totalCount);
    } catch (ex: any) {
      this.statusMessage.set(`Error al cargar: ${ex.message || ex}`);
    } finally {
      this.isLoading.set(false);
    }
  }


  selectedBlobs = signal<Set<string>>(new Set());

  toggleSelection(blobName: string) {
    this.selectedBlobs.update(set => {
      const newSet = new Set(set);
      if (newSet.has(blobName)) newSet.delete(blobName);
      else newSet.add(blobName);
      return newSet;
    });
  }

  toggleAllCurrentPage(event: Event) {
    const isChecked = (event.target as HTMLInputElement).checked;
    const currentBlobs = this.blobs();
    this.selectedBlobs.update(set => {
      const newSet = new Set(set);
      if (isChecked) {
        currentBlobs.forEach(b => newSet.add(b.name));
      } else {
        currentBlobs.forEach(b => newSet.delete(b.name));
      }
      return newSet;
    });
  }

  isAllCurrentPageSelected(): boolean {
    const currentBlobs = this.blobs();
    if (currentBlobs.length === 0) return false;
    const set = this.selectedBlobs();
    return currentBlobs.every(b => set.has(b.name));
  }

  updateLocalBlobStatus(blobName: string, status: IngestionStatus) {
    this.blobs.update(bs => bs.map(b => b.name === blobName ? { ...b, status } : b));
  }

  async processBlob(blobName: string) {
    try {
      this.statusMessage.set(`Procesando ${blobName}... (esto puede tardar unos minutos)`);
      this.updateLocalBlobStatus(blobName, IngestionStatus.Processing);
      
      const parts = blobName.split('/');
      const source = parts.length > 1 ? parts[1] : 'Unknown';
      
      const result = await this.ingestionService.processBlob(blobName, source);
      this.statusMessage.set(result);
      this.updateLocalBlobStatus(blobName, IngestionStatus.Completed);
    } catch (ex: any) {
      this.statusMessage.set(`Error: ${ex.message || ex}`);
      this.updateLocalBlobStatus(blobName, IngestionStatus.Failed);
    }
  }

  async enqueueSelected() {
    const selected = Array.from(this.selectedBlobs());
    if (selected.length === 0) return;
    
    this.statusMessage.set(`Encolando ${selected.length} documentos...`);
    
    const requests = selected.map(blobName => {
        const parts = blobName.split('/');
        return { blobPath: blobName, source: parts.length > 1 ? parts[1] : 'Unknown' };
    });
    
    try {
        const result = await this.ingestionService.enqueueBulk(requests);
        this.statusMessage.set(result);
        this.blobs.update(bs => bs.map(b => selected.includes(b.name) ? { ...b, status: IngestionStatus.Pending } : b));
        this.selectedBlobs.set(new Set());
    } catch (ex: any) {
        this.statusMessage.set(`Error al encolar: ${ex.message || ex}`);
    }
  }

  async resetBlob(blobName: string) {
    try {
      this.statusMessage.set(`Reiniciando estado de ${blobName}...`);
      
      const parts = blobName.split('/');
      const docId = parts.length > 0 ? parts[parts.length - 1].replace('.json', '') : '';
      
      const result = await this.ingestionService.resetBlobStatus(docId);
      this.statusMessage.set(result);
      this.updateLocalBlobStatus(blobName, IngestionStatus.Pending);
    } catch (ex: any) {
      this.statusMessage.set(`Error al reiniciar: ${ex.message || ex}`);
    }
  }

  async resetStuckProcessing() {
    try {
      this.statusMessage.set('Reiniciando todos los documentos colgados...');
      const result = await this.ingestionService.resetStuckProcessing();
      this.statusMessage.set(result);
      // For this one, we do a full reload because we don't know which ones changed
      await this.loadBlobs();
    } catch (ex: any) {
      this.statusMessage.set(`Error al reiniciar documentos colgados: ${ex.message || ex}`);
    }
  }

  // Helpers for template
  getDocUrl(blobName: string): string | null {
    const parts = blobName.split('/');
    const docId = parts.length > 0 ? parts[parts.length - 1].replace('.json', '') : '';
    const source = parts.length > 1 ? parts[1] : '';
    
    if (source === DocumentSource.BOE) {
      return `https://www.boe.es/buscar/doc.php?id=${docId}`;
    } else if (source === DocumentSource.BOJA) {
      const docParts = docId.split('-');
      if (docParts.length >= 4 && docParts[0] === DocumentSource.BOJA) {
        const year = docParts[1];
        const num = docParts[2];
        const disp = docParts.slice(3).join('-');
        return `http://www.juntadeandalucia.es/boja/${year}/${num}/${disp}.html`;
      }
    } else if (source === DocumentSource.BOPMA) {
      const docParts = docId.split(`${DocumentSource.BOPMA}-`);
      if (docParts.length > 1) {
        let filename = docParts.slice(1).join(`${DocumentSource.BOPMA}-`);
        if (filename.includes('verificacion.php?archivo=')) {
          filename = filename.split('verificacion.php?archivo=')[1];
        }
        return `https://www.bopmalaga.es/verificacion.php?archivo=${filename}.pdf`;
      }
    }
    return null;
  }
  
  getSizeKb(sizeBytes: number): number {
    return Math.round(sizeBytes / 1024);
  }
}
