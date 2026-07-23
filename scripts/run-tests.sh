#!/bin/bash

# Runs the TodoApp.Tests unit test suite locally, with no Docker and no running stack.
#
#   ./scripts/run-tests.sh [extra dotnet-test args...]
#
# The tests mock all AMQP and database boundaries, so a .NET 8 SDK is the only prerequisite.
# If no SDK 8.x is found on PATH, one is installed user-locally into ~/.dotnet via Microsoft's
# official install script (no sudo required); subsequent runs reuse it.
#
# Extra arguments are passed through to dotnet test, e.g.:
#   ./scripts/run-tests.sh --filter BaseApiControllerTests

set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEST_PROJECT="$REPO_ROOT/src/TodoApp.Tests/TodoApp.Tests.csproj"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# A user-local install from a previous run takes effect here, before the SDK check
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

if command -v dotnet &> /dev/null && dotnet --list-sdks 2>/dev/null | grep -q "^8\."; then
    echo -e "${GREEN}✓ .NET SDK found: $(dotnet --version)${NC}"
else
    echo -e "${YELLOW}No .NET 8 SDK found - installing user-locally into $DOTNET_ROOT...${NC}"
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$DOTNET_ROOT"
    echo -e "${GREEN}✓ .NET SDK installed: $(dotnet --version)${NC}"
fi

dotnet test "$TEST_PROJECT" -v minimal "$@"
