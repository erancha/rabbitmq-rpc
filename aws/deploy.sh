#!/bin/bash

# Prerequisite: sudo apt-get update && sudo apt-get install -y awscli

# Required parameters
VPC_ID=""
SUBNET_ID=""
STACK_NAME=""
DOCKER_HUB_USERNAME=""
INSTANCE_TYPE=""

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --vpc-id)
            VPC_ID="$2"
            shift 2
            ;;
        --subnet-id)
            SUBNET_ID="$2"
            shift 2
            ;;
        --stack-name)
            STACK_NAME="$2"
            shift 2
            ;;
        --docker-hub-username)
            DOCKER_HUB_USERNAME="$2"
            shift 2
            ;;
        --instance-type)
            INSTANCE_TYPE="$2"
            shift 2
            ;;
        *)
            echo "Unknown parameter: $1"
            exit 1
            ;;
    esac
done

# Verify required parameters
if [ -z "$VPC_ID" ] || [ -z "$SUBNET_ID" ] || [ -z "$STACK_NAME" ] || [ -z "$DOCKER_HUB_USERNAME" ]; then
    echo "Error: Required parameters missing"
    echo "Usage: $0 --vpc-id VPC_ID --subnet-id SUBNET_ID --stack-name STACK_NAME --docker-hub-username DOCKER_HUB_USERNAME [--instance-type INSTANCE_TYPE]"
    exit 1
fi

# Source AWS configuration if exists
if [ -f "$(dirname "$0")/aws-configure.sh" ]; then
    source "$(dirname "$0")/aws-configure.sh"
fi

# Verify AWS CLI configuration
echo "Checking AWS CLI configuration..."
if ! aws_identity=$(aws sts get-caller-identity 2>/dev/null); then
    echo "Error: AWS CLI is not installed or not configured properly."
    echo "Please install AWS CLI and run 'aws configure' to set up your credentials."
    exit 1
fi
echo "AWS CLI is configured. Using account: $(echo "$aws_identity" | jq -r .Account)"

# Verify required files exist
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE_PATH="$SCRIPT_DIR/template.yaml"
DOCKER_COMPOSE_PATH="$SCRIPT_DIR/../deploy/docker-compose.yml"

if [ ! -f "$TEMPLATE_PATH" ]; then
    echo "Error: CloudFormation template not found at: $TEMPLATE_PATH"
    exit 1
fi

if [ ! -f "$DOCKER_COMPOSE_PATH" ]; then
    echo "docker-compose.yml not found at: $DOCKER_COMPOSE_PATH !"
    exit 1
fi

# Validate the template
echo -e "\nValidating CloudFormation template..."
if ! aws cloudformation validate-template --template-body "file://$TEMPLATE_PATH" >/dev/null 2>&1; then
    echo "Template validation failed:"
    aws cloudformation validate-template --template-body "file://$TEMPLATE_PATH"
    echo -e "\nTemplate content:"
    cat "$TEMPLATE_PATH"
    exit 1
fi
echo "Template validation successful"

# Deploy the CloudFormation stack
echo -e "\nDeploying CloudFormation stack '$STACK_NAME'..."

# Check if stack exists and get bucket name if it does
STACK_EXISTS=false
BUCKET_NAME=""
if stack_info=$(aws cloudformation describe-stacks --stack-name "$STACK_NAME" 2>/dev/null); then
    STACK_EXISTS=true
    BUCKET_NAME=$(echo "$stack_info" | jq -r '.Stacks[0].Outputs[] | select(.OutputKey=="InitializationBucketName") | .OutputValue')
fi

if [ "$STACK_EXISTS" = true ]; then
    # Update existing stack
    echo "Stack '$STACK_NAME' exists. Updating..."
    update_output=$(aws cloudformation update-stack \
        --stack-name "$STACK_NAME" \
        --template-body "file://$TEMPLATE_PATH" \
        --parameters \
            "ParameterKey=VpcId,ParameterValue=$VPC_ID" \
            "ParameterKey=SubnetId,ParameterValue=$SUBNET_ID" \
            "ParameterKey=InstanceType,ParameterValue=$INSTANCE_TYPE" \
        --capabilities CAPABILITY_IAM 2>&1)
    
    if [ $? -eq 0 ]; then
        UPDATE_INITIATED=true
    elif echo "$update_output" | grep -q "No updates are to be performed"; then
        echo "No updates needed for stack '$STACK_NAME'. Continuing..."
        UPDATE_INITIATED=false
    else
        echo "Error updating stack:"
        echo "$update_output"
        exit 1
    fi
else
    # Create new stack
    echo "Creating new stack '$STACK_NAME'..."
    aws cloudformation create-stack \
        --stack-name "$STACK_NAME" \
        --template-body "file://$TEMPLATE_PATH" \
        --parameters \
            "ParameterKey=VpcId,ParameterValue=$VPC_ID" \
            "ParameterKey=SubnetId,ParameterValue=$SUBNET_ID" \
            "ParameterKey=InstanceType,ParameterValue=$INSTANCE_TYPE" \
        --capabilities CAPABILITY_IAM
fi

# Wait for stack to complete
if [ "$STACK_EXISTS" = true ] && [ "$UPDATE_INITIATED" = true ]; then
    echo "Waiting for stack update to complete..."
    aws cloudformation wait stack-update-complete --stack-name "$STACK_NAME"
elif [ "$STACK_EXISTS" = false ]; then
    echo "Waiting for stack creation to complete..."
    aws cloudformation wait stack-create-complete --stack-name "$STACK_NAME"
fi

# Get stack outputs and upload initialization files
stack_info=$(aws cloudformation describe-stacks --stack-name "$STACK_NAME")
BUCKET_NAME=$(echo "$stack_info" | jq -r '.Stacks[0].Outputs[] | select(.OutputKey=="InitializationBucketName") | .OutputValue')

if [ -n "$BUCKET_NAME" ]; then
    echo -e "\nUploading initialization files to S3 bucket: $BUCKET_NAME"
    echo "aws s3 cp $DOCKER_COMPOSE_PATH s3://$BUCKET_NAME/docker-compose.yml"
    echo "aws s3 cp $SCRIPT_DIR/ec2-initialization.sh s3://$BUCKET_NAME/ec2-initialization.sh"

    aws s3 cp "$DOCKER_COMPOSE_PATH" "s3://$BUCKET_NAME/docker-compose.yml"
    aws s3 cp "$SCRIPT_DIR/ec2-initialization.sh" "s3://$BUCKET_NAME/ec2-initialization.sh"

    echo -e "\nTo manually run initialization on EC2, switch to super user and:"
    echo "aws s3 cp s3://$BUCKET_NAME/ec2-initialization.sh /usr/local/bin/ec2-initialization.sh && chmod +x /usr/local/bin/ec2-initialization.sh && INIT_BUCKET=$BUCKET_NAME STACK_NAME=$STACK_NAME /usr/local/bin/ec2-initialization.sh"
fi

echo -e "\nDeployment completed successfully!"
echo -e "\nApplication endpoints:"
echo "$stack_info" | jq -r '.Stacks[0].Outputs[] | "\(.OutputKey): \(.OutputValue)"'

echo -e "\nTo check deployment status, run these commands on the EC2 instance:"
echo "cat /var/log/cloud-init-output.log"              # View EC2 instance initialization logs
echo "sudo su -"                                       # Switch to super user
echo "systemctl status docker"                         # Check if Docker service is running and enabled
echo "docker version"                                  # Verify Docker Engine version and connectivity
echo "docker compose version"                          # Confirm Docker Compose is installed correctly
echo "docker ps"                                       # List all running containers
echo "cd /opt/$STACK_NAME && docker compose ps"        # List compose containers
echo "cd /opt/$STACK_NAME && docker compose logs -f"   # View container logs in real-time
echo "cd /opt/$STACK_NAME && docker compose logs postgres -f"   # View PostgreSQL logs specifically
echo "cd /opt/$STACK_NAME && docker compose logs 2>&1 | grep -i -E 'warn|error|exception|fail'"   # Check for warnings and errors in all containers

echo -e "\nTo monitor system resources:"
echo "top -b -n 1"                                    # CPU and memory usage snapshot
echo "free -h"                                        # Memory usage and swap
echo "df -h"                                          # Disk space usage
echo "docker stats --no-stream"                       # Container resource usage
