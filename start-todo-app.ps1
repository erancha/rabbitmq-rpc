# Check if Docker Desktop is running and start if needed
$dockerDesktop = Get-Process 'Docker Desktop' -ErrorAction SilentlyContinue
if (-not $dockerDesktop) {
    Write-Host 'Docker Desktop is not running. Attempting to start it...' -ForegroundColor Yellow
    Start-Process 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
}

# Wait for Docker Engine to be fully ready (max 120 seconds)
$timeout = 120
$timer = 0
$engineReady = $false
Write-Host 'Waiting for Docker Engine to be ready... ' -NoNewline

while (-not $engineReady -and $timer -lt $timeout) {
    Start-Sleep -Seconds 5
    $timer += 5
    Write-Host '.' -NoNewline
    
    # Check if Docker Engine is responsive
    try {
        $result = docker version 2>&1
        if ($LASTEXITCODE -eq 0) {
            # Try to pull a small test image to verify Docker is fully functional
            docker pull hello-world 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host 'Ready!' -ForegroundColor Green
                $engineReady = $true
            }
        }
    }
    catch {}
}

if (-not $engineReady) {
    Write-Host "`nError: Docker Engine did not start in time." -ForegroundColor Red
    Write-Host 'Please ensure Docker Desktop is running properly and try again.' -ForegroundColor Yellow
    exit 1
}

Write-Host 'Checking dependencies...' -ForegroundColor Cyan

# Check for Docker
try {
    $dockerVersion = (docker --version) | Out-String
    Write-Host ('✓ Docker is installed: ' + $dockerVersion.Trim()) -ForegroundColor Green
}
catch {
    Write-Host 'Error: Docker is not installed or not running.' -ForegroundColor Red
    Write-Host 'Please install Docker Desktop from https://www.docker.com/products/docker-desktop' -ForegroundColor Yellow
    exit 1
}

# Check for Docker Compose
try {
    $dockerComposeVersion = (docker-compose --version) | Out-String
    Write-Host ('✓ Docker Compose is installed: ' + $dockerComposeVersion.Trim()) -ForegroundColor Green
}
catch {
    Write-Host 'Error: Docker Compose is not installed.' -ForegroundColor Red
    Write-Host 'Docker Compose should be included with Docker Desktop' -ForegroundColor Yellow
    exit 1
}

# Check for .NET SDK
try {
    $dotnetVersion = (dotnet --version) | Out-String
    Write-Host ('✓ .NET SDK is installed: ' + $dotnetVersion.Trim()) -ForegroundColor Green
}
catch {
    Write-Host 'Error: .NET SDK is not installed.' -ForegroundColor Red
    Write-Host 'Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Yellow
    exit 1
}

# Set consistent project name
$env:COMPOSE_PROJECT_NAME = "todo-app"

# Start the application
Write-Host "`nStarting the Todo application..." -ForegroundColor Cyan

# Check for --no-cache flag
$buildFlags = "--build"
if ($args -contains "--no-cache") {
    Write-Host "Starting services with no cache..." -ForegroundColor Yellow
    $buildFlags = "--build --no-cache"
}
else {
    Write-Host "Starting services..." -ForegroundColor Yellow
}

try {
    # Build and start the services
    $result = docker-compose up $buildFlags -d
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: Issues detected while starting the application." -ForegroundColor Red
        Write-Host "Checking container status..." -ForegroundColor Yellow
        docker-compose ps
        Write-Host "You can still use 'docker-compose' commands to troubleshoot." -ForegroundColor Yellow
    }

    # Show containers and endpoints
    Write-Host "`nShowing container logs..." -ForegroundColor Cyan
    if ($args -contains "-f" -or $args -contains "--follow") {
        Write-Host "Following logs (Ctrl+C to stop)..."
        docker-compose logs -f
    }
    else {
        docker-compose ps
        Write-Host "`nServices are running in the background." -ForegroundColor Green
        Write-Host "Use 'docker-compose logs -f' to follow logs"

        Write-Host "Available endpoints:" -ForegroundColor Green
        Write-Host "- WebAPI: http://localhost:5000" -ForegroundColor Cyan
        Write-Host "- RabbitMQ Management UI: http://localhost:15672 (credentials: guest/guest)" -ForegroundColor Cyan
    }

    # Simple test: One user and two todo items
    Write-Host "`nRunning simple test..." -ForegroundColor Cyan

    # Wait for services to be fully ready
    Write-Host "Allowing for services to be ready..."
    Start-Sleep -Seconds 5

    # Create a user
    Write-Host "Creating test user..."
    $maxRetries = 3
    $retryCount = 0
    $userId = $null

    while ($retryCount -lt $maxRetries) {
        try {
            $timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds()
            $userResponse = Invoke-RestMethod -Method Post -Uri 'http://localhost:5000/api/v1/Users' `
                -Headers @{ 'accept' = '*/*'; 'Content-Type' = 'application/json' } `
                -Body "{`"username`": `"testuser_$timestamp`", `"email`": `"testuser_$timestamp@gmail.com`"}"

            $userId = $userResponse.createdId
            Write-Host "Created user with ID: $userId" -ForegroundColor Green
            break
        }
        catch {
            $retryCount++
            if ($retryCount -lt $maxRetries) {
                Write-Host "Retrying user creation in 2 seconds..." -ForegroundColor Yellow
                Start-Sleep -Seconds 2
            }
            else {
                Write-Host "Failed to create user after $maxRetries attempts. Error: $($_.Exception.Message)" -ForegroundColor Red
                exit 1
            }
        }
    }

    # Create two todo items for the user
    Write-Host "Creating test todo items..."
    1..2 | ForEach-Object {
        $i = $_
        try {
            $todoBody = "{`"title`": `"Todo $i`", `"description`": `"Description for todo $i`", `"userId`": $userId}"
            $todoResponse = Invoke-RestMethod -Method Post -Uri 'http://localhost:5000/api/v1/TodoItems' `
                -Headers @{ 'accept' = '*/*'; 'Content-Type' = 'application/json' } `
                -Body $todoBody
            Write-Host "Created todo item $i with ID: $($todoResponse.createdId)" -ForegroundColor Green
        }
        catch {
            Write-Host "Failed to create todo item $i. Error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}
catch {
    Write-Host 'Error starting the application.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
