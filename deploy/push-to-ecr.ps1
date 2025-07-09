param(
    [Parameter(Mandatory = $true)]
    [string]$ImageName,  # e.g. todo-app:webapi-latest

    [Parameter(Mandatory = $true)]
    [string]$Tag  # e.g. webapi-latest or worker-latest
)

$region = "us-east-1"
$repoUri = "public.ecr.aws/o4a6b3b1/todo-app:$Tag"

# Optional: avoid using default Docker config
$env:DOCKER_CONFIG = "$env:TEMP\docker-tmp"
mkdir $env:DOCKER_CONFIG -Force

# Login to ECR Public
Write-Host '🔐 Logging in to Amazon ECR Public...'
$token = aws ecr-public get-login-password --region $region
$token | docker login --username AWS --password-stdin public.ecr.aws/o4a6b3b1

# Tag image
docker tag $ImageName $repoUri

# Push image
docker push $repoUri
