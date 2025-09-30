# CI Pipeline — FREE mode

The CI pipeline should execute in the following order. Each stage fails fast and surfaces artifacts/logs for later troubleshooting.

## 1. Environment bootstrap

- Checkout repository
- Cache `~/.nuget/packages`, `~/.npm`, and `~/.cache/pip`
- Install .NET 9 SDK, Node 20, and Python 3.11

## 2. Orchestrator (.NET)

- `dotnet restore orchestrator-dotnet/src/Orchestrator.csproj`
- `dotnet build orchestrator-dotnet/src/Orchestrator.csproj -c Release`
- `dotnet test orchestrator-dotnet/src/Orchestrator.csproj -c Release --no-build`
- `dotnet format --verify-no-changes`

## 3. Quant package (Python)

- `pip install -r quant-python/requirements.txt`
- `pytest -q quant-python/tests`
- `ruff check quant-python` *(or `flake8` if Ruff unavailable)*

## 4. UI (Angular)

- `npm ci --prefix ui-angular/nebula-pulse`
- `npm test -- --watch=false --browsers=ChromeHeadless --prefix ui-angular/nebula-pulse`
- `npm run build -- --configuration production --prefix ui-angular/nebula-pulse`

## 5. Database schema lint

- `sqlfluff lint db --dialect tsql`
- `sqlcmd -S localhost -U sa -P ${{ secrets.SA_PASSWORD }} -i db/schema.sql` *(run against ephemeral container)*

## 6. Container images

- `docker build -t ghcr.io/<org>/ai-orchestrator:$(git rev-parse --short HEAD) orchestrator-dotnet`
- `docker build -t ghcr.io/<org>/ai-quant:$(git rev-parse --short HEAD) quant-python`
- `docker build -t ghcr.io/<org>/ai-ui:$(git rev-parse --short HEAD) ui-angular/nebula-pulse`

## 7. Security & compliance

- `dotnet list orchestrator-dotnet/src/Orchestrator.csproj package --vulnerable --include-transitive`
- `npm audit --prefix ui-angular/nebula-pulse`
- `pip-audit --requirement quant-python/requirements.txt`
- Optional: `trivy fs --severity HIGH,CRITICAL .`

## 8. Reporting

- Publish test reports (JUnit/coverage) for each stack
- Upload built container digests and SBOMs
- Gate deployment on all previous stages passing
