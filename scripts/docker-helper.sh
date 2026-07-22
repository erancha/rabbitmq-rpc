#!/usr/bin/env bash
#
# Thin Docker Compose helper for the Todo application stack: teardown and scoped logs.
# Bringing the stack UP stays in start.sh (it also validates the Docker environment and runs the
# smoke test); this helper owns the compose operations start.sh does not.
#
#   ./scripts/docker-helper.sh --stop [--volumes] [--prune[=images|volumes|all]]
#       Stop and remove the stack's containers and network. Volumes are KEPT by default so the
#       Postgres data survives a restart; --volumes (alias -v) also removes them for a clean
#       database. --prune additionally clears this project's dangling images and/or volumes (bare
#       --prune = all), label-scoped to the todo-app compose project so other stacks are never
#       touched. Pruning volumes is refused unless --volumes is also given, since it would delete
#       the kept Postgres volume.
#   ./scripts/docker-helper.sh --logs [-e|--errors|-w|--warnings] [--grep <pat>] [--since <dur>] [service...]
#       Follow logs live (last 200 lines). -e filters to ERROR/EXCEPTION/FATAL; -w widens that to
#       also include WARN; --grep <pat> filters to a case-insensitive regex; --since limits the
#       window (e.g. 10m, 1h). A trailing service list narrows to those services (webapi worker
#       postgres rabbitmq).
#   ./scripts/docker-helper.sh --ps
#       Show the stack's container status.
#   -h, --help
#
# Scope to specific services by naming them last (narrows --logs; --stop is whole-stack):
#   ./scripts/docker-helper.sh --logs -e worker       # include: follow errors from one service
# To skip a service, list the others. Combine with -e/-w to filter by severity too —
# e.g. warnings from everything except rabbitmq, whose broker logs otherwise flood a plain follow:
#   ./scripts/docker-helper.sh --logs -w $(docker compose -f scripts/docker-compose.yml config --services 2>/dev/null | grep -vx rabbitmq)
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/scripts/docker-compose.yml"

# start.sh brings the stack up under this fixed project name, so pin it here too (including for
# prune label-scoping) instead of letting compose derive one from the directory name.
PROJECT_NAME=todo-app

# -f resolves the compose file's relative build contexts against scripts/, its own directory, so
# the helper works from any working directory.
compose() { docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" "$@"; }

require_docker() {
  docker info >/dev/null 2>&1 || { echo "Docker is not running — start Docker and retry." >&2; exit 1; }
}

# Print this script's header comment block as --help text (single source of usage docs).
print_help() { awk 'NR==1 {next} /^#/ {sub(/^# ?/, ""); print; next} {exit}' "${BASH_SOURCE[0]}"; }

do_stop() {
  local wipe=false prune=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -v|--volumes) wipe=true ;;
      --prune)      prune=all ;;
      --prune=*)    prune="${1#*=}" ;;
      *)            echo "Unknown --stop option '$1'" >&2; exit 1 ;;
    esac
    shift
  done

  case "$prune" in
    ""|images|volumes|all) ;;
    *) echo "--prune expects images|volumes|all (bare --prune = all)" >&2; exit 1 ;;
  esac
  if ! $wipe && [[ "$prune" == volumes || "$prune" == all ]]; then
    echo "--prune=$prune would delete the kept Postgres volume; pass --volumes too if that is intended." >&2
    exit 1
  fi

  if $wipe; then
    compose down -v --remove-orphans
  else
    compose down --remove-orphans
  fi

  local -a scope=(--filter "label=com.docker.compose.project=$PROJECT_NAME")
  case "$prune" in
    images)  docker image  prune -f "${scope[@]}" ;;
    volumes) docker volume prune -f "${scope[@]}" ;;
    all)     docker image  prune -f "${scope[@]}"; docker volume prune -f "${scope[@]}" ;;
  esac
}

# Severity presets and --grep only choose what to match; the stream follows live. --since bounds the
# window. Remaining args name services to narrow to.
do_logs() {
  local pattern=""
  local -a since=()
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -e|--errors)   pattern='(error|exception|fatal)'; shift ;;
      -w|--warnings) pattern='(warn|warning|error|exception|fatal)'; shift ;;
      --grep)        pattern="${2:?--grep needs a pattern}"; shift 2 ;;
      --since)       since=(--since "${2:?--since needs a duration}"); shift 2 ;;
      *)             break ;;
    esac
  done

  local -a args=(logs -f --tail=200 "${since[@]}" "$@")
  if [[ -n "$pattern" ]]; then
    compose "${args[@]}" | grep -Eai "$pattern"
  else
    compose "${args[@]}"
  fi
}

mode="${1:-}"
case "$mode" in
  -h|--help) print_help; exit 0 ;;
  "")        print_help; exit 1 ;;
esac
shift

require_docker
case "$mode" in
  --stop) do_stop "$@" ;;
  --logs) do_logs "$@" ;;
  --ps)   compose ps "$@" ;;
  *) echo "Unknown command: $mode (expected --stop | --logs | --ps; see -h)" >&2; exit 1 ;;
esac
