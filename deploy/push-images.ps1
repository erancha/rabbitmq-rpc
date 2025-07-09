param(
    [Parameter(Mandatory = $true)]
    [string]$DockerHubUsername, 

    [Parameter(Mandatory = $false)]
    [string]$Tag = "latest",

    [Parameter(Mandatory = $false)]
    [bool]$usePublicECR = $false
)

# Change to the root directory of the project
Set-Location $PSScriptRoot\..

# Print mode based on flag
if ($usePublicECR) {
    Write-Host "Building images for ECR..." -ForegroundColor Cyan
} else {
    Write-Host "Building images for Docker Hub..." -ForegroundColor Cyan
}

try {
    # Build WebAPI image
    Write-Host "`nBuilding WebAPI image..." -ForegroundColor Yellow
    docker build -t "todo-app:webapi-$Tag" -f src/TodoApp.WebApi/Dockerfile .
    if ($LASTEXITCODE -ne 0) { throw "Failed to build WebAPI image" }

    # Build Worker image
    Write-Host "`nBuilding Worker image..." -ForegroundColor Yellow
    docker build -t "todo-app:worker-$Tag" -f src/TodoApp.WorkerService/Dockerfile .
    if ($LASTEXITCODE -ne 0) { throw "Failed to build Worker image" }

    if ($usePublicECR) {
        # === Call ECR upload script for each image ===

        Write-Host "`nPushing WebAPI image to Public ECR..." -ForegroundColor Yellow
        & "$PSScriptRoot\push-to-ecr.ps1" -ImageName "todo-app:webapi-$Tag" -Tag "webapi-$Tag"

        Write-Host "`nPushing Worker image to Public ECR..." -ForegroundColor Yellow
        & "$PSScriptRoot\push-to-ecr.ps1" -ImageName "todo-app:worker-$Tag" -Tag "worker-$Tag"
    }
    else {
        # Test Docker Hub connectivity
        Write-Host "Testing Docker Hub connectivity..." -ForegroundColor Cyan
        docker pull hello-world:latest > $null 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Error connecting to Docker Hub. Please ensure you're logged in with 'docker login'" -ForegroundColor Red
            exit 1
        }

        # Tag & push to Docker Hub
        Write-Host "Pushing WebAPI image to Docker Hub..." -ForegroundColor Yellow
        docker tag "todo-app:webapi-$Tag" "$DockerHubUsername/todo-app:webapi-$Tag"
        docker push "$DockerHubUsername/todo-app:webapi-$Tag"
        if ($LASTEXITCODE -ne 0) { throw "Failed to push WebAPI image" }

        Write-Host "Pushing Worker image to Docker Hub..." -ForegroundColor Yellow
        docker tag "todo-app:worker-$Tag" "$DockerHubUsername/todo-app:worker-$Tag"
        docker push "$DockerHubUsername/todo-app:worker-$Tag"
        if ($LASTEXITCODE -ne 0) { throw "Failed to push Worker image" }
    }

    Write-Host "`n✅ Successfully built and pushed all images!" -ForegroundColor Green
    Write-Host "You can now run transform-compose.ps1 to create the deployment configuration."
}
catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
    exit 1
}
finally {
    # Return to original directory
    Set-Location $PSScriptRoot
}
