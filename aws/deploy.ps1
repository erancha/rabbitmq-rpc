param(
    [Parameter(Mandatory = $true)]
    [string]$VpcId,
    
    [Parameter(Mandatory = $true)]
    [string]$SubnetId,
    
    [Parameter(Mandatory = $true)]
    [string]$StackName,
    
    [Parameter(Mandatory = $true)]
    [string]$DockerHubUsername,
    
    [Parameter(Mandatory = $false)]
    [string]$InstanceType
)

if (Test-Path "$PSScriptRoot/aws-configure.ps1") {
    . "$PSScriptRoot/aws-configure.ps1"
}

# Verify AWS CLI is installed and configured
Write-Host "Checking AWS CLI configuration..." -ForegroundColor Cyan
try {
    $awsIdentity = aws sts get-caller-identity | ConvertFrom-Json
    Write-Host "AWS CLI is configured. Using account: $($awsIdentity.Account)" -ForegroundColor Green
}
catch {
    Write-Host "Error: AWS CLI is not installed or not configured properly." -ForegroundColor Red
    Write-Host "Please install AWS CLI and run 'aws configure' to set up your credentials." -ForegroundColor Yellow
    exit 1
}

# Verify required files exist
$templatePath = Join-Path $PSScriptRoot "template.yaml"
$dockerComposePath = Join-Path $PSScriptRoot "..\deploy\docker-compose.yml"

if (-not (Test-Path $templatePath)) {
    Write-Host "Error: CloudFormation template not found at: $templatePath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $dockerComposePath)) {
    Write-Host "docker-compose.yml not found at: $dockerComposePath !" -ForegroundColor Yellow
    exit 1
}

# Read docker-compose content
$dockerComposeContent = Get-Content -Path $dockerComposePath -Raw

# Create temporary template with docker-compose content
$tempTemplatePath = Join-Path $PSScriptRoot "template.tmp.yaml"
$templateContent = Get-Content -Path $templatePath -Raw

# Process docker-compose content with proper indentation
$lines = $dockerComposeContent -split "`n"
$processedLines = $lines | ForEach-Object { 
    $line = $_.TrimEnd()  # Remove trailing whitespace
    if ($line) {
        # Add proper indentation (10 base + 2 for each level)
        "          $line".PadLeft("          $line".Length + 2)
    } else { "" }
}
$processedContent = $processedLines -join "`n"
$processedContent = $processedContent.TrimEnd()  # Remove any trailing newlines

# Replace the placeholder with the processed content
$templateContent = $templateContent -replace '<docker-compose-content>', $processedContent
Set-Content -Path $tempTemplatePath -Value $templateContent

# Validate the template
Write-Host "`nValidating CloudFormation template..." -ForegroundColor Cyan
try {
    $validationOutput = aws cloudformation validate-template --template-body file://$tempTemplatePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Template validation failed:" -ForegroundColor Red
        Write-Host $validationOutput -ForegroundColor Red
        Write-Host "`nGenerated template content:" -ForegroundColor Yellow
        Get-Content -Path $tempTemplatePath
        exit 1
    }
    Write-Host "Template validation successful" -ForegroundColor Green
} catch {
    Write-Host "Template validation failed: $_" -ForegroundColor Red
    Write-Host "`nGenerated template content:" -ForegroundColor Yellow
    Get-Content -Path $tempTemplatePath
    exit 1
}

# Deploy the CloudFormation stack
Write-Host "`nDeploying CloudFormation stack '$StackName'..." -ForegroundColor Cyan

try {
    # Check if stack exists
    $stackExists = $null
    try {
        $stackExists = aws cloudformation describe-stacks --stack-name $StackName | ConvertFrom-Json
    }
    catch {}

    if ($stackExists) {
        # Update existing stack
        Write-Host "Stack '$StackName' exists. Updating..." -ForegroundColor Yellow
        aws cloudformation update-stack --stack-name $StackName --template-body file://$tempTemplatePath --parameters "ParameterKey=VpcId,ParameterValue=$VpcId" "ParameterKey=SubnetId,ParameterValue=$SubnetId" --capabilities CAPABILITY_IAM
    }
    else {
        # Create new stack
        Write-Host "Creating new stack '$StackName'..." -ForegroundColor Yellow
        aws cloudformation create-stack --stack-name $StackName --template-body file://$tempTemplatePath --parameters "ParameterKey=VpcId,ParameterValue=$VpcId" "ParameterKey=SubnetId,ParameterValue=$SubnetId" --capabilities CAPABILITY_IAM
    }

    # Wait for stack to complete
    Write-Host "Waiting for stack operation to complete..." -ForegroundColor Cyan
    aws cloudformation wait stack-create-complete --stack-name $StackName

    # Get stack outputs
    $stack = aws cloudformation describe-stacks --stack-name $StackName | ConvertFrom-Json
    $outputs = $stack.Stacks[0].Outputs

    Write-Host "`nDeployment completed successfully!" -ForegroundColor Green
    Write-Host "`nApplication endpoints:" -ForegroundColor Cyan
    foreach ($output in $outputs) {
        Write-Host "$($output.OutputKey): $($output.OutputValue)" -ForegroundColor Yellow
    }

    Write-Host "`nTo check deployment status, run these commands on the EC2 instance:" -ForegroundColor Cyan
    Write-Host "sudo su -" -ForegroundColor Yellow                                      # Switch to super user
    Write-Host "systemctl status docker" -ForegroundColor Yellow                        # Check if Docker service is running and enabled
    Write-Host "docker version" -ForegroundColor Yellow                                 # Verify Docker Engine version and connectivity
    Write-Host "docker compose version" -ForegroundColor Yellow                         # Confirm Docker Compose is installed correctly
    Write-Host "docker ps" -ForegroundColor Yellow                                      # List all running containers
    Write-Host "cd /opt/$StackName && docker compose ps" -ForegroundColor Yellow        # List compose containers
    Write-Host "cd /opt/$StackName && docker compose logs -f" -ForegroundColor Yellow   # View container logs in real-time
    Write-Host "cat /var/log/cloud-init-output.log" -ForegroundColor Yellow             # View EC2 instance initialization logs
    Write-Host "netstat -tulpn | grep -E '5000|5672|15672'" -ForegroundColor Yellow     # Check if required ports are listening
}
catch {
    Write-Host "Error deploying CloudFormation stack:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
