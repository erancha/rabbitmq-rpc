param(
    [Parameter(Mandatory=$true)]
    [string]$DockerHubUsername
)

# Change to the deploy folder
Set-Location $PSScriptRoot

# Read the original docker-compose.yml
$sourceCompose = Get-Content -Path "..\docker-compose.yml" -Raw

# Ensure version is preserved and replace build sections with image references
$transformedCompose = $sourceCompose -replace 
    "(?ms)  webapi:\r?\n    build:\r?\n      context: \.\r?\n      dockerfile: src/TodoApp\.WebApi/Dockerfile", 
    "  webapi:`n    image: $DockerHubUsername/todo-app:webapi-latest"

$transformedCompose = $transformedCompose -replace 
    "(?ms)  worker:\r?\n    build:\r?\n      context: \.\r?\n      dockerfile: src/TodoApp\.WorkerService/Dockerfile",
    "  worker:`n    image: $DockerHubUsername/todo-app:worker-latest"

# Remove ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT lines
$finalCompose = $transformedCompose -replace '.*ASPNETCORE_ENVIRONMENT.*\r?\n?', '' -replace '.*DOTNET_ENVIRONMENT.*\r?\n?', ''
$finalCompose | Set-Content -Path "docker-compose.yml"

Write-Host "Created deployment docker-compose.yml with Docker Hub images in the deploy folder"
Write-Host "You can now execute 'docker-compose up' or share ${PSScriptRoot}\docker-compose.yml"
