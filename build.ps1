$ErrorActionPreference = "Stop"

# Cake comes from the repo's tool manifest (.config/dotnet-tools.json); GitVersion and vpk are #tool-pinned in build.cake.
dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pass all script arguments to Cake, e.g. .\build.ps1 --target=Package
dotnet cake --configuration=Release @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
