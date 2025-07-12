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

# Validate the template
Write-Host "`nValidating CloudFormation template..." -ForegroundColor Cyan
try {
    $validationOutput = aws cloudformation validate-template --template-body file://$templatePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Template validation failed:" -ForegroundColor Red
        Write-Host $validationOutput -ForegroundColor Red
        Write-Host "`nTemplate content:" -ForegroundColor Yellow
        Get-Content -Path $templatePath
        exit 1
    }
    Write-Host "Template validation successful" -ForegroundColor Green
} catch {
    Write-Host "Template validation failed: $_" -ForegroundColor Red
    Write-Host "`nTemplate content:" -ForegroundColor Yellow
    Get-Content -Path $templatePath
    exit 1
}

# Deploy the CloudFormation stack
Write-Host "`nDeploying CloudFormation stack '$StackName'..." -ForegroundColor Cyan

try {
    # Check if stack exists and get bucket name if it does
    $stackExists = $null
    $bucketName = $null
    try {
        $stackExists = aws cloudformation describe-stacks --stack-name $StackName | ConvertFrom-Json
        if ($stackExists) {
            $outputs = $stackExists.Stacks[0].Outputs
            $bucketName = ($outputs | Where-Object { $_.OutputKey -eq "InitializationBucketName" }).OutputValue
        }
    }
    catch {}

    if ($stackExists) {
        # Update existing stack
        Write-Host "Stack '$StackName' exists. Updating..." -ForegroundColor Yellow
        $params = @(
            "--stack-name", $StackName,
            "--template-body", "file://$templatePath",
            "--parameters",
            "ParameterKey=VpcId,ParameterValue=$VpcId",
            "ParameterKey=SubnetId,ParameterValue=$SubnetId",
            "ParameterKey=InstanceType,ParameterValue=$InstanceType",
            "--capabilities", "CAPABILITY_IAM"
        )
        aws cloudformation update-stack @params
    }
    else {
        # Create new stack
        Write-Host "Creating new stack '$StackName'..." -ForegroundColor Yellow
        aws cloudformation create-stack --stack-name $StackName --template-body file://$templatePath --parameters "ParameterKey=VpcId,ParameterValue=$VpcId" "ParameterKey=SubnetId,ParameterValue=$SubnetId" "ParameterKey=InstanceType,ParameterValue=$InstanceType" --capabilities CAPABILITY_IAM
    }

    # Wait for stack to complete
    Write-Host "Waiting for stack operation to complete..." -ForegroundColor Cyan
    if ($stackExists) {
        aws cloudformation wait stack-update-complete --stack-name $StackName
    } else {
        aws cloudformation wait stack-create-complete --stack-name $StackName
    }
    
    # Get stack outputs and upload initialization files
    $stack = aws cloudformation describe-stacks --stack-name $StackName | ConvertFrom-Json
    $outputs = $stack.Stacks[0].Outputs
    $bucketName = ($outputs | Where-Object { $_.OutputKey -eq "InitializationBucketName" }).OutputValue

    if ($bucketName) {
        Write-Host "`nUploading initialization files to S3 bucket: $bucketName" -ForegroundColor Cyan
        Write-Host "aws s3 cp $dockerComposePath s3://$bucketName/docker-compose.yml" -ForegroundColor Gray
        Write-Host "aws s3 cp $PSScriptRoot/docker-compose-setup.sh s3://$bucketName/docker-compose-setup.sh" -ForegroundColor Gray

        aws s3 cp $dockerComposePath "s3://$bucketName/docker-compose.yml" 
        aws s3 cp "$PSScriptRoot/docker-compose-setup.sh" "s3://$bucketName/docker-compose-setup.sh"

        Write-Host "`nTo manually run initialization on EC2, switch to super user and:" -ForegroundColor Cyan
        Write-Host "aws s3 cp s3://$bucketName/docker-compose-setup.sh /tmp/docker-compose-setup.sh && chmod +x /tmp/docker-compose-setup.sh && INIT_BUCKET=$bucketName STACK_NAME=$StackName /tmp/docker-compose-setup.sh" -ForegroundColor Gray
    }

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
    Write-Host "cd /opt/$StackName && docker compose logs postgres -f" -ForegroundColor Yellow   # View PostgreSQL logs specifically
    Write-Host "cat /var/log/cloud-init-output.log" -ForegroundColor Yellow             # View EC2 instance initialization logs
    Write-Host "netstat -tulpn | grep -E '5000|5672|15672'" -ForegroundColor Yellow     # Check if required ports are listening

    Write-Host "`nTo monitor system resources:" -ForegroundColor Cyan
    Write-Host "top -b -n 1" -ForegroundColor Yellow                                    # CPU and memory usage snapshot
    Write-Host "free -h" -ForegroundColor Yellow                                        # Memory usage and swap
    Write-Host "df -h" -ForegroundColor Yellow                                          # Disk space usage
    Write-Host "docker stats --no-stream" -ForegroundColor Yellow                       # Container resource usage
}
catch {
    Write-Host "Error deploying CloudFormation stack:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
