#!/usr/bin/env bash
#
# Run psql against the stack's Postgres (database tododb) inside the postgres container.
#
#   ./scripts/sql-helper.sh                          interactive psql shell
#   ./scripts/sql-helper.sh -c "SELECT ..."          run one statement
#   ./scripts/sql-helper.sh -f <host-file> [args]    run a SQL file from the host (streamed via stdin)
#   ./scripts/sql-helper.sh --db <name> ...          target a database other than tododb
#
#   Examples:
#     ./scripts/sql-helper.sh -c 'SELECT count(*) FROM "Users";'
#     ./scripts/sql-helper.sh -f scripts/sql/show-all-tables.sql
#
# Any argument other than --db is forwarded to psql untouched.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/scripts/docker-compose.yml"

# start.sh brings the stack up under this fixed project name; pin it here so the container resolves
# regardless of the working directory.
PROJECT_NAME=todo-app

# -f resolves the compose file's relative build contexts against scripts/, its own directory.
compose() { docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" "$@"; }

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
fi

# --db is ours, not psql's: strip it so every other arg forwards to psql untouched.
DB="tododb"
ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --db) DB="${2:?--db needs a database name}"; shift 2 ;;
    *)    ARGS+=("$1"); shift ;;
  esac
done
set -- "${ARGS[@]+"${ARGS[@]}"}"

PSQL=(psql -U postgres -d "$DB")

if [[ "${1:-}" == "-f" || "${1:-}" == "--file" ]]; then
  # -f names a file on the host, not inside the container, so stream it in on stdin rather than
  # psql's own -f (which would look for the path container-side).
  SQL_FILE="${2:-}"
  if [[ -z "$SQL_FILE" || ! -f "$SQL_FILE" ]]; then
    echo "SQL file not found: ${SQL_FILE:-<none given>}" >&2
    exit 1
  fi
  shift 2
  compose exec -T postgres "${PSQL[@]}" "$@" < "$SQL_FILE"
elif [[ $# -eq 0 ]]; then
  # Interactive shell needs a TTY, so do not pass -T here.
  compose exec postgres "${PSQL[@]}"
else
  compose exec -T postgres "${PSQL[@]}" "$@"
fi
