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
    } catch {}
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
} catch {
    Write-Host 'Error: Docker is not installed or not running.' -ForegroundColor Red
    Write-Host 'Please install Docker Desktop from https://www.docker.com/products/docker-desktop' -ForegroundColor Yellow
    exit 1
}

# Check for Docker Compose
try {
    $dockerComposeVersion = (docker-compose --version) | Out-String
    Write-Host ('✓ Docker Compose is installed: ' + $dockerComposeVersion.Trim()) -ForegroundColor Green
} catch {
    Write-Host 'Error: Docker Compose is not installed.' -ForegroundColor Red
    Write-Host 'Docker Compose should be included with Docker Desktop' -ForegroundColor Yellow
    exit 1
}

# Check for .NET SDK
try {
    $dotnetVersion = (dotnet --version) | Out-String
    Write-Host ('✓ .NET SDK is installed: ' + $dotnetVersion.Trim()) -ForegroundColor Green
} catch {
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
} else {
    Write-Host "Starting services..." -ForegroundColor Yellow
}

try {
    docker-compose up $buildFlags -d
    docker-compose logs -f
} catch {
    Write-Host 'Error starting the application.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
