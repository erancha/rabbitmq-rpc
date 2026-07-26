#!/usr/bin/env bash
#
# End-to-end throughput benchmark plus host/runtime data collection, for comparing the same stack
# across machines. Runs the full teardown -> start -> JMeter load -> row-count sequence, and while
# the load runs it samples per-container resource usage so a throughput gap can be attributed to
# CPU saturation, memory pressure, queue backlog, or I/O rather than guessed at.
#
#   ./scripts/benchmark.sh [--minimal] [--interval N] [-h|--help]
#
# Options:
#   --minimal      run JMeter's minimal plan (10 requests) instead of --long (50,000). Default: long.
#   --interval N   seconds between docker-stats samples during the load run (default: 3).
#
# All collected data is written to a timestamped directory under benchmark-results/ and condensed
# into report.txt. Run this on each machine and share the two report.txt files; they hold the
# host facts (CPU count, memory, WSL/cgroup limits, Docker view) and the load-time resource samples
# needed to explain a throughput difference without a guessing saga.
#
# Prerequisites are the same as the underlying scripts: Docker with Compose v2, and java on PATH for
# JMeter (jmeter-helper.sh installs JMeter itself if absent).
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/scripts/docker-compose.yml"
PROJECT_NAME=todo-app

CYAN='\033[0;36m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'

# -f resolves the compose file's relative build contexts against scripts/, its own directory, so the
# stack resolves regardless of the working directory (mirrors the sibling helper scripts).
compose() { docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" "$@"; }

print_help() { awk 'NR==1 {next} /^#/ {sub(/^# ?/, ""); print; next} {exit}' "${BASH_SOURCE[0]}"; }

MODE=long
INTERVAL=3
while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)  print_help; exit 0 ;;
    --minimal)  MODE=minimal; shift ;;
    --long)     MODE=long; shift ;;
    --interval) INTERVAL="${2:?--interval needs a number of seconds}"; shift 2 ;;
    *)          echo "Unknown option '$1' (see -h)" >&2; exit 2 ;;
  esac
done

docker info >/dev/null 2>&1 || { echo "Docker is not running — start Docker and retry." >&2; exit 1; }

# One run directory per invocation keeps machines' results side by side without overwriting.
STAMP="$(date +%Y%m%d-%H%M%S)"
HOST="$(hostname)"
OUT_DIR="$REPO_ROOT/benchmark-results/${STAMP}-${HOST}"
mkdir -p "$OUT_DIR"
REPORT="$OUT_DIR/report.txt"
STATS_LOG="$OUT_DIR/docker-stats.log"      # human-readable per-sample dumps
STATS_CSV="$OUT_DIR/docker-stats.csv"      # elapsed,container,cpu_pct,mem_mib — for the summary
JMETER_LOG="$OUT_DIR/jmeter.log"
ENV_LOG="$OUT_DIR/environment.txt"

# Tee to both the terminal and the report so the on-screen run doubles as the shareable artifact.
log() { echo -e "$@" | tee -a "$REPORT"; }
section() { printf '\n===== %s =====\n' "$1" | tee -a "$REPORT" >/dev/null; echo -e "${CYAN}$1${NC}"; }

# ------------------------------------------------------------------ static host / runtime facts ---
# Captured once up front: these are exactly the axes on which two WSL2 hosts diverge (cores and
# memory handed to the VM, cgroup limits, what the Docker daemon itself sees), so a reader can line
# the two machines' files up field by field.
collect_environment() {
  section "Collecting host + runtime environment"
  {
    echo "# Benchmark environment — ${HOST} @ ${STAMP}"
    echo "# mode=${MODE} sample-interval=${INTERVAL}s"
    echo

    echo "## Host CPU"
    if command -v lscpu >/dev/null 2>&1; then
      lscpu | grep -Ei 'model name|^cpu\(s\)|socket|core|thread|mhz|bogomips'
    fi
    echo "nproc: $(nproc)"
    echo "cpuinfo processors: $(grep -c '^processor' /proc/cpuinfo)"
    echo

    echo "## Host memory"
    free -h
    echo

    echo "## Load average / uptime"
    uptime
    echo

    echo "## Kernel / WSL"
    uname -a
    cat /proc/version 2>/dev/null
    echo

    # PSI (pressure stall information) directly reports time spent stalled on CPU/mem/IO contention —
    # the single clearest signal that a host is oversubscribed rather than genuinely slow.
    echo "## Pressure stall info (some=stalled time; higher avg10 => more contention)"
    for r in cpu memory io; do
      [ -r "/proc/pressure/$r" ] && { echo "[$r]"; cat "/proc/pressure/$r"; }
    done
    echo

    echo "## cgroup CPU/memory limits seen by this shell"
    for f in /sys/fs/cgroup/cpu.max /sys/fs/cgroup/memory.max \
             /sys/fs/cgroup/cpu/cpu.cfs_quota_us /sys/fs/cgroup/memory/memory.limit_in_bytes; do
      [ -r "$f" ] && echo "$f: $(cat "$f")"
    done
    echo

    # .wslconfig lives on the Windows side; from WSL it is reachable under /mnt/c. It caps the VM's
    # cores/memory, so a difference here alone can explain the whole gap.
    echo "## .wslconfig (Windows host cap on the WSL2 VM, if present)"
    shopt -s nullglob
    local found=false
    for cfg in /mnt/c/Users/*/.wslconfig; do
      echo "--- $cfg"; cat "$cfg"; found=true
    done
    $found || echo "(none found under /mnt/c/Users/*/.wslconfig — defaults apply)"
    shopt -u nullglob
    echo

    echo "## Docker version"
    docker version
    echo

    # The daemon's own CPU/Memory line is what actually bounds every container — often lower than the
    # host's because it inherits the WSL2 VM's allocation.
    echo "## Docker info (CPUs / Total Memory / storage + cgroup driver bound every container)"
    docker info
    echo

    echo "## Resolved compose config (worker replica count + memory limits in effect)"
    WORKER_REPLICAS="${WORKER_REPLICAS:-<compose default>}"
    echo "WORKER_REPLICAS env: $WORKER_REPLICAS"
    compose config 2>/dev/null | grep -Ei 'replicas|memory:|image:' | sed 's/^/  /'
    echo

    echo "## Git revision"
    git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null
    git -C "$REPO_ROOT" status --porcelain 2>/dev/null | head -n 20
  } > "$ENV_LOG" 2>&1

  # Surface the handful of fields that most often decide the comparison so the terminal run is
  # informative on its own; the full dump stays in environment.txt.
  {
    echo "Host: $HOST"
    echo "CPUs (nproc): $(nproc)"
    grep -m1 'model name' /proc/cpuinfo | sed 's/model name[[:space:]]*:/CPU:/'
    free -h | awk '/^Mem:/ {print "Memory total/used/free: "$2" "$3" "$4}'
    echo "Docker CPUs: $(docker info --format '{{.NCPU}}' 2>/dev/null)"
    echo "Docker Total Memory: $(docker info --format '{{.MemTotal}}' 2>/dev/null | awk '{printf "%.1f GiB\n", $1/1073741824}')"
  } | tee -a "$REPORT"
}

# ---------------------------------------------------------------- resource sampling during load ---
# Background sampler: every INTERVAL seconds it snapshots all containers' CPU/mem/net/block IO and
# appends a parseable CPU/mem row per container to the CSV. Runs only for the duration of the load so
# the samples line up with the throughput window.
SAMPLER_PID=""
start_sampler() {
  echo "elapsed_s,container,cpu_pct,mem_mib" > "$STATS_CSV"
  : > "$STATS_LOG"
  local start_ts; start_ts=$(date +%s)
  (
    while true; do
      local now elapsed
      now=$(date +%s); elapsed=$((now - start_ts))
      {
        echo "----- t+${elapsed}s ($(date +%H:%M:%S)) -----"
        docker stats --no-stream \
          --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.NetIO}}\t{{.BlockIO}}\t{{.PIDs}}'
      } >> "$STATS_LOG" 2>&1

      # Rows look like:  todo-app-worker-1  142.35%  83.4MiB / 256MiB
      # -> name=$1, cpu%=$2, mem value+unit=$3. Normalise CPU and memory to plain numbers.
      docker stats --no-stream --format '{{.Name}} {{.CPUPerc}} {{.MemUsage}}' 2>/dev/null \
        | awk -v e="$elapsed" '
            {
              cpu=$2; gsub(/%/,"",cpu);
              val=$3; sub(/[A-Za-z]+$/,"",val);   # numeric part of e.g. 83.4MiB
              unit=$3; sub(/^[0-9.]+/,"",unit);    # trailing unit
              mib=val;
              if (unit=="GiB") mib=val*1024;
              else if (unit=="KiB") mib=val/1024;
              else if (unit=="B")   mib=val/1048576;
              printf "%s,%s,%s,%.1f\n", e, $1, cpu, mib;
            }' >> "$STATS_CSV"
      sleep "$INTERVAL"
    done
  ) &
  SAMPLER_PID=$!
}

stop_sampler() {
  [ -n "$SAMPLER_PID" ] && kill "$SAMPLER_PID" 2>/dev/null
  wait "$SAMPLER_PID" 2>/dev/null
  SAMPLER_PID=""
}
# Never leave a sampler running if the benchmark is interrupted.
trap 'stop_sampler' EXIT INT TERM

# Per-container CPU average/peak and memory peak across the load window — the summary that turns the
# raw samples into "which container was pinned".
summarize_stats() {
  section "Resource summary (per container, over the load window)"
  {
    echo "container            avg_cpu%   peak_cpu%   peak_mem_MiB   samples"
    awk -F, 'NR>1 {
        sum[$2]+=$3; n[$2]++;
        if ($3>maxc[$2]) maxc[$2]=$3;
        if ($4>maxm[$2]) maxm[$2]=$4;
      }
      END {
        for (c in n)
          printf "%-18s  %8.1f   %8.1f   %12.1f   %7d\n", c, sum[c]/n[c], maxc[c], maxm[c], n[c];
      }' "$STATS_CSV" | sort
    echo
    # Aggregate CPU across all containers per sample vs the CPU budget: if peak total approaches
    # NCPU*100%, the host is CPU-bound and more workers/RAM will not help.
    local ncpu; ncpu=$(docker info --format '{{.NCPU}}' 2>/dev/null)
    echo "CPU budget: ${ncpu} CPUs => ${ncpu}00% aggregate ceiling"
    awk -F, 'NR>1 { tot[$1]+=$3 } END {
        for (t in tot) { s+=tot[t]; k++; if (tot[t]>mx) mx=tot[t] }
        if (k) printf "Aggregate container CPU%%: avg %.0f%%, peak %.0f%% (across %d samples)\n", s/k, mx, k;
      }' "$STATS_CSV"
  } | tee -a "$REPORT"
}

# --------------------------------------------------------------------------------- the run steps ---
run_teardown() {
  section "Teardown (stop + prune + wipe volumes) for a clean, comparable start"
  "$REPO_ROOT/scripts/docker-helper.sh" --stop --prune=all --volumes 2>&1 | tee -a "$REPORT"
}

run_start() {
  section "Starting the stack"
  "$REPO_ROOT/scripts/start.sh" 2>&1 | tee -a "$REPORT"
}

run_load() {
  section "Running JMeter load (${MODE}) with live resource sampling every ${INTERVAL}s"
  start_sampler
  local t0 t1
  t0=$(date +%s)
  "$REPO_ROOT/scripts/jmeter-helper.sh" $([ "$MODE" = long ] && echo --long) 2>&1 | tee "$JMETER_LOG" | tee -a "$REPORT"
  t1=$(date +%s)
  stop_sampler
  log "\nLoad wall-clock: $(( t1 - t0 ))s"

  # The final "summary =" line carries the run's overall throughput and error rate — the headline
  # number the whole comparison turns on.
  local headline
  headline=$(grep 'summary =' "$JMETER_LOG" | tail -n 1)
  [ -n "$headline" ] && log "Throughput (final): ${headline}"
}

collect_post_run() {
  section "Post-run row counts"
  "$REPO_ROOT/scripts/sql-helper.sh" -f scripts/sql/show-all-tables.sql 2>&1 | tee -a "$REPORT"

  section "RabbitMQ queue state after the run (residual backlog => worker-bound)"
  compose exec -T rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged \
    2>&1 | tee -a "$REPORT"
}

# ------------------------------------------------------------------------------------------ main ---
echo -e "${GREEN}Benchmark results -> ${OUT_DIR}${NC}"
: > "$REPORT"
log "Benchmark: host=${HOST} mode=${MODE} interval=${INTERVAL}s stamp=${STAMP}"

collect_environment
run_teardown
run_start
run_load
collect_post_run
summarize_stats

section "Done"
log "Full data in: ${OUT_DIR}"
log "  report.txt          — this condensed report"
log "  environment.txt     — full host/Docker/compose facts"
log "  docker-stats.log    — per-sample container dumps"
log "  docker-stats.csv    — parseable CPU/mem samples"
log "  jmeter.log          — full JMeter output"
echo -e "\n${YELLOW}Share ${OUT_DIR}/report.txt from each machine to compare.${NC}"
