package main

import (
	"fmt"
	"log"
	"net/http"
)

func main() {
	fmt.Println("Iniciando BOE Scraper...")

	// Endpoint health check para validación en .NET Aspire
	http.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		w.Write([]byte("BOE Scraper is running\n"))
	})

	port := "8080" // Se podría leer de variables de entorno luego
	fmt.Printf("Servidor escuchando en puerto %s\n", port)
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		log.Fatalf("Error al iniciar el servidor: %v", err)
	}
}
