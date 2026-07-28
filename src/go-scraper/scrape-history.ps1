param (
    [string]$StartDate = "2026-07-01",
    [string]$EndDate = "2026-07-31",
    [string]$StorageConnString = "UseDevelopmentStorage=true"
)

$env:SCRAPE_START_DATE = $StartDate
$env:SCRAPE_END_DATE = $EndDate
$env:ConnectionStrings__boletines = $StorageConnString

Write-Host "Iniciando volcado masivo desde $StartDate hasta $EndDate..."
Write-Host "Destino: $StorageConnString"
Write-Host "--------------------------------------------------------"

go run .
