#!/bin/bash

set -e

GREEN=${GREEN:-'\033[0;32m'}
RED=${RED:-'\033[0;31m'}
YELLOW=${YELLOW:-'\033[1;33m'}
CYAN=${CYAN:-'\033[0;36m'}
NC=${NC:-'\033[0m'}

API_BASE_URL=${API_BASE_URL:-"http://localhost:5000"}

echo -e "\n${CYAN}Running simple test...${NC}"

echo "Creating test user..."
MAX_RETRIES=3
RETRY_COUNT=0

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
    TIMESTAMP=$(date +%s)
    USER_RESPONSE=$(curl -s -X 'POST' \
      "${API_BASE_URL}/api/v1/Users" \
      -H 'accept: */*' \
      -H 'Content-Type: application/json' \
      -d "{\"username\": \"testuser_${TIMESTAMP}\", \"email\": \"testuser_${TIMESTAMP}@gmail.com\"}")

    if [[ $USER_RESPONSE == *"createdId"* ]]; then
        USER_ID=$(echo $USER_RESPONSE | grep -o '"createdId":[0-9]*' | cut -d ':' -f2)
        echo -e "${GREEN}Created user with ID: $USER_ID${NC}"
        break
    fi

    RETRY_COUNT=$((RETRY_COUNT + 1))
    if [ $RETRY_COUNT -lt $MAX_RETRIES ]; then
        echo -e "${YELLOW}Retrying user creation in 2 seconds...${NC}"
        sleep 2
    else
        echo -e "${RED}Failed to create user after $MAX_RETRIES attempts. Response: $USER_RESPONSE${NC}"
        exit 1
    fi
done

echo "Creating test todo items..."
for i in {1..2}; do
    TODO_RESPONSE=$(curl -s -X 'POST' \
      "${API_BASE_URL}/api/v1/TodoItems" \
      -H 'accept: */*' \
      -H 'Content-Type: application/json' \
      -d "{\"title\": \"Todo $i\", \"description\": \"Description for todo $i\", \"userId\": $USER_ID}")

    if [[ $TODO_RESPONSE == *"createdId"* ]]; then
        TODO_ID=$(echo $TODO_RESPONSE | grep -o '"createdId":[0-9]*' | cut -d ':' -f2)
        echo -e "${GREEN}Created todo item $i with ID: $TODO_ID${NC}"
    else
        echo -e "${RED}Failed to create todo item $i. Response: $TODO_RESPONSE${NC}"
        exit 1
    fi
done
