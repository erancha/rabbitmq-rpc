param(
    [Parameter(Mandatory=$true)]
    [string]$DockerHubUsername,
    
    [Parameter(Mandatory=$false)]
    [string]$Tag = "latest"
)

# Change to the root directory of the project
Set-Location $PSScriptRoot\..

Write-Host "Building and pushing images for $DockerHubUsername..." -ForegroundColor Cyan

# Test Docker Hub connectivity by trying to pull a small public image
try {
    Write-Host "Testing Docker Hub connectivity..." -ForegroundColor Cyan
    docker pull hello-world:latest > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error connecting to Docker Hub. Please ensure you're logged in with 'docker login'" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "Error connecting to Docker Hub. Please ensure you're logged in with 'docker login'" -ForegroundColor Red
    exit 1
}

try {
    # Build and push WebAPI
    Write-Host "`nBuilding WebAPI image..." -ForegroundColor Yellow
    docker build -t "$DockerHubUsername/todo-app:webapi-$Tag" -f src/TodoApp.WebApi/Dockerfile .
    if ($LASTEXITCODE -ne 0) { throw "Failed to build WebAPI image" }
    
    Write-Host "Pushing WebAPI image to Docker Hub..." -ForegroundColor Yellow
    docker push "$DockerHubUsername/todo-app:webapi-$Tag"
    if ($LASTEXITCODE -ne 0) { throw "Failed to push WebAPI image" }

    # Build and push Worker
    Write-Host "`nBuilding Worker image..." -ForegroundColor Yellow
    docker build -t "$DockerHubUsername/todo-app:worker-$Tag" -f src/TodoApp.WorkerService/Dockerfile .
    if ($LASTEXITCODE -ne 0) { throw "Failed to build Worker image" }
    
    Write-Host "Pushing Worker image to Docker Hub..." -ForegroundColor Yellow
    docker push "$DockerHubUsername/todo-app:worker-$Tag"
    if ($LASTEXITCODE -ne 0) { throw "Failed to push Worker image" }

    Write-Host "`nSuccessfully built and pushed all images!" -ForegroundColor Green
    Write-Host "You can now run transform-compose.ps1 to create the deployment configuration."
}
catch {
    Write-Host "`nError: $_" -ForegroundColor Red
    exit 1
}
finally {
    # Return to original directory
    Set-Location $PSScriptRoot
}
