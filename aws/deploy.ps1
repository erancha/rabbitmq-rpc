param(
    [Parameter(Mandatory = $true)]
    [string]$VpcId,
    
    [Parameter(Mandatory = $true)]
    [string]$SubnetId,
    
    [Parameter(Mandatory = $false)]
    [string]$StackName = "todo-app-stack",
    
    [Parameter(Mandatory = $false)]
    [string]$Region = "eu-central-1"
    
    # [Parameter(Mandatory=$false)]
    # [string]$InstanceType = "t2.small"
)

# Verify AWS CLI is installed and configured
Write-Host "Checking AWS CLI configuration..." -ForegroundColor Cyan
try {
    $awsIdentity = aws sts get-caller-identity | ConvertFrom-Json
    Write-Host "✓ AWS CLI is configured. Using account: $($awsIdentity.Account)" -ForegroundColor Green
}
catch {
    Write-Host "Error: AWS CLI is not installed or not configured properly." -ForegroundColor Red
    Write-Host "Please install AWS CLI and run 'aws configure' to set up your credentials." -ForegroundColor Yellow
    exit 1
}

# Verify the template file exists
$templatePath = Join-Path $PSScriptRoot "cloudformation\todo-app-template.yaml"
if (-not (Test-Path $templatePath)) {
    Write-Host "Error: CloudFormation template not found at: $templatePath" -ForegroundColor Red
    exit 1
}

# Deploy the CloudFormation stack
Write-Host "`nDeploying CloudFormation stack '$StackName'..." -ForegroundColor Cyan

try {
    # Check if stack exists
    $stackExists = $null
    try {
        $stackExists = aws cloudformation describe-stacks --stack-name $StackName --region $Region | ConvertFrom-Json
    }
    catch {}

    if ($stackExists) {
        # Update existing stack
        Write-Host "Stack '$StackName' exists. Updating..." -ForegroundColor Yellow
        aws cloudformation update-stack `
            --stack-name $StackName `
            --template-body file://$templatePath `
            --parameters `
            ParameterKey=VpcId, ParameterValue=$VpcId `
            ParameterKey=SubnetId, ParameterValue=$SubnetId `
            --region $Region `
            --capabilities CAPABILITY_IAM
    }
    else {
        # Create new stack
        Write-Host "Creating new stack '$StackName'..." -ForegroundColor Yellow
        aws cloudformation create-stack `
            --stack-name $StackName `
            --template-body file://$templatePath `
            --parameters `
            ParameterKey=VpcId, ParameterValue=$VpcId `
            ParameterKey=SubnetId, ParameterValue=$SubnetId `
            --region $Region `
            --capabilities CAPABILITY_IAM
    }

    # Wait for stack to complete
    Write-Host "Waiting for stack operation to complete..." -ForegroundColor Cyan
    aws cloudformation wait stack-create-complete --stack-name $StackName --region $Region

    # Get stack outputs
    $stack = aws cloudformation describe-stacks --stack-name $StackName --region $Region | ConvertFrom-Json
    $outputs = $stack.Stacks[0].Outputs

    Write-Host "`nDeployment completed successfully!" -ForegroundColor Green
    Write-Host "`nApplication endpoints:" -ForegroundColor Cyan
    foreach ($output in $outputs) {
        Write-Host "$($output.OutputKey): $($output.OutputValue)" -ForegroundColor Yellow
    }

}
catch {
    Write-Host "Error deploying CloudFormation stack:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
