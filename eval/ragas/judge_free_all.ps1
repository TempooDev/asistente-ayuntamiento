# Recalcula las métricas sin juez de las ejecuciones que cita la memoria (E1-E4 y las repeticiones de E3 y E4).
# Uso: cd eval\ragas; powershell -ExecutionPolicy Bypass -File judge_free_all.ps1
$ErrorActionPreference = 'Stop'
$runs = @(
  @{ Id = '20261114-133119'; Dataset = '..\dataset' },        # E1, 17 documentos
  @{ Id = '20261114-142259'; Dataset = '..\dataset' },        # E2, 200 documentos
  @{ Id = '20261115-004843'; Dataset = '..\dataset' },        # E3, colección completa
  @{ Id = '20261115-010523'; Dataset = '..\dataset-sizes' },  # E4, batería variada
  @{ Id = '20261119-185051'; Dataset = '..\dataset' },        # E3 repetida
  @{ Id = '20261119-185754'; Dataset = '..\dataset-sizes' }   # E4 repetida
)
foreach ($r in $runs) {
  Write-Host "== $($r.Id)"
  uv run python judge_free.py "..\runs\$($r.Id)" --dataset $r.Dataset
  if ($LASTEXITCODE -ne 0) { throw "judge_free.py falló en $($r.Id)" }
}
