import { Component, EventEmitter, Output } from '@angular/core';

@Component({
  selector: 'app-welcome-guide',
  standalone: true,
  template: `
    <div class="fixed inset-0 z-[100] flex items-center justify-center bg-black bg-opacity-50 p-4 backdrop-blur-sm">
      <div class="bg-surface rounded-2xl shadow-2xl max-w-2xl w-full p-6 animate-in fade-in zoom-in duration-300 relative max-h-[90vh] flex flex-col bg-white">
        <button (click)="close()" class="absolute top-4 right-4 text-gray-500 hover:text-gray-800 transition-colors p-2 rounded-full hover:bg-gray-100">
          <span class="material-symbols-outlined">close</span>
        </button>
        
        <div class="flex items-center gap-3 mb-6 text-primary">
          <div class="bg-primary/10 p-2 rounded-lg">
            <span class="material-symbols-outlined text-[32px]">menu_book</span>
          </div>
          <h2 class="text-2xl font-bold m-0 text-gray-900">Guía de Uso del Asistente</h2>
        </div>
        
        <div class="flex-1 overflow-y-auto pr-2 space-y-6">
          <section>
            <div class="flex items-center gap-2 mb-2">
              <span class="material-symbols-outlined text-blue-600">info</span>
              <h3 class="text-lg font-semibold text-gray-900 m-0">¿Qué es este Asistente?</h3>
            </div>
            <p class="text-gray-600 leading-relaxed ml-8">
              Esta herramienta es un asistente inteligente diseñado para ayudarte a consultar documentos oficiales del ayuntamiento, incluyendo boletines como el BOE, BOJA y BOPMA. Simplemente escribe tu pregunta en el chat y el asistente buscará la información relevante en la base documental.
            </p>
          </section>

          <section>
            <div class="flex items-center gap-2 mb-2">
              <span class="material-symbols-outlined text-purple-600">balance</span>
              <h3 class="text-lg font-semibold text-gray-900 m-0">Modo Arena</h3>
            </div>
            <p class="text-gray-600 leading-relaxed ml-8 mb-4">
              El <strong>Modo Arena</strong> te permite comparar dos modelos de Inteligencia Artificial al mismo tiempo para ayudarnos a evaluar y mejorar las respuestas del asistente.
            </p>
            
            <div class="bg-purple-50 border border-purple-100 rounded-xl p-5 ml-8 shadow-sm">
              <h4 class="font-medium text-purple-900 mb-3">¿Cómo funciona?</h4>
              <ul class="space-y-4">
                <li class="flex items-start gap-3">
                  <div class="bg-white p-1 rounded-md shadow-sm shrink-0">
                    <span class="material-symbols-outlined text-purple-600 text-lg block">toggle_on</span>
                  </div>
                  <span class="text-gray-700 text-sm leading-tight mt-1">Activa el interruptor de "Modo Arena" en la barra superior de la pantalla.</span>
                </li>
                <li class="flex items-start gap-3">
                  <div class="bg-white p-1 rounded-md shadow-sm shrink-0">
                    <span class="material-symbols-outlined text-purple-600 text-lg block">chat</span>
                  </div>
                  <span class="text-gray-700 text-sm leading-tight mt-1">Haz una pregunta al asistente como lo harías normalmente en cualquier chat.</span>
                </li>
                <li class="flex items-start gap-3">
                  <div class="bg-white p-1 rounded-md shadow-sm shrink-0">
                    <span class="material-symbols-outlined text-purple-600 text-lg block">visibility</span>
                  </div>
                  <span class="text-gray-700 text-sm leading-tight mt-1">Recibirás dos respuestas diferentes, generadas por el <strong>Modelo Alfa</strong> y el <strong>Modelo Beta</strong>.</span>
                </li>
                <li class="flex items-start gap-3">
                  <div class="bg-white p-1 rounded-md shadow-sm shrink-0">
                    <span class="material-symbols-outlined text-purple-600 text-lg block">thumb_up</span>
                  </div>
                  <span class="text-gray-700 text-sm leading-tight mt-1">Vota por la mejor respuesta haciendo <strong>clic en la burbuja</strong> que prefieras, o elige el botón <strong>"Empate"</strong> si ambas son de igual calidad.</span>
                </li>
              </ul>
            </div>
          </section>
        </div>
        
        <div class="mt-6 flex justify-end shrink-0 pt-4 border-t border-gray-100">
          <button class="bg-primary hover:bg-primary-dark text-white px-6 py-2.5 rounded-xl font-medium transition-colors shadow-sm flex items-center gap-2" (click)="close()">
            <span>¡Entendido, empezar!</span>
            <span class="material-symbols-outlined text-sm">arrow_forward</span>
          </button>
        </div>
      </div>
    </div>
  `
})
export class WelcomeGuideComponent {
  @Output() dismiss = new EventEmitter<void>();

  close() {
    this.dismiss.emit();
  }
}
