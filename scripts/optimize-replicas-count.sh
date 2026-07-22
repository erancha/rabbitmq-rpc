#!/usr/bin/env bash
#
# Find the worker replica count with the best throughput on this machine, by exponential probing
# plus binary search instead of trying every value: measure 1, 2, 4, 8, ... (up to [max],
# default 32) until throughput stops increasing, then binary-search between the best count and
# the first worse one until the bracket closes.
#
# Each measurement starts from a clean slate (./scripts/docker-helper.sh --stop --volumes
# --prune), starts the stack via ./scripts/start.sh with WORKER_REPLICAS=<n>, waits until the
# full RPC path answers (a POST /api/v1/Users round-trips through RabbitMQ, a worker, and
# Postgres), runs the long JMeter plan (./scripts/jmeter-helper.sh --long), and records the
# cumulative throughput JMeter reports. A failed measurement counts as zero throughput, so the search backs away from counts
# the machine cannot sustain.
#
#   ./scripts/optimize-replicas-count.sh [max]
#
# Outputs under jmeter/replica-sweep/ (gitignored):
#   results.tsv    one row per measured count: throughput, error count/percent, duration
#   sweep.log      progress plus teardown/startup noise
#   jmeter-<n>.log full JMeter console output per run
#
# The final table names the best-throughput count; if it looks stable, set it as the
# WORKER_REPLICAS fallback in scripts/docker-compose.yml so it becomes the default.
set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$ROOT_DIR/jmeter/replica-sweep"
RESULTS="$OUT_DIR/results.tsv"
SWEEP_LOG="$OUT_DIR/sweep.log"

MAX="${1:-32}"
[[ "$MAX" =~ ^[0-9]+$ && "$MAX" -ge 1 ]] \
  || { echo "Usage: $(basename "$0") [max], with max >= 1" >&2; exit 2; }

mkdir -p "$OUT_DIR"
: > "$SWEEP_LOG"
printf 'replicas\tthroughput_rps\terrors\terr_pct\tduration\n' > "$RESULTS"

log() { echo "[$(date +%T)] $*" | tee -a "$SWEEP_LOG"; }

# Floating-point a > b (JMeter reports fractional req/s).
gt() { awk -v a="$1" -v b="$2" 'BEGIN{exit !(a > b)}'; }

# Readiness = one warm-up request completes end to end, proving webapi, RabbitMQ, at least one
# worker, and a migrated Postgres are all up. Freshly started replicas can still lose the
# first-boot migration race and restart, hence the generous retry window.
wait_for_rpc_path() {
  local n=$1 i resp
  for i in $(seq 1 60); do
    resp=$(curl -s -m 8 -X POST http://localhost:5000/api/v1/Users \
      -H 'Content-Type: application/json' \
      -d "{\"username\":\"warmup_${n}_${i}_${RANDOM}\",\"email\":\"warmup_${n}_${i}_${RANDOM}@example.com\"}" || true)
    [[ $resp == *createdId* ]] && return 0
    sleep 3
  done
  return 1
}

# Measures throughput for $1 worker replicas into RPS (0 on failure), one clean-slate stack
# start + long JMeter plan per count; repeated counts return the cached value.
declare -A CACHE
measure() {
  local n=$1 line rps dur err err_pct
  if [[ -n "${CACHE[$n]:-}" ]]; then
    RPS="${CACHE[$n]}"
    log "replicas=$n: cached ${RPS}/s"
    return
  fi

  RPS=0
  log "=== replicas=$n: clean slate ==="
  "$ROOT_DIR/scripts/docker-helper.sh" --stop --volumes --prune >> "$SWEEP_LOG" 2>&1

  log "replicas=$n: starting stack via start.sh"
  # start.sh's own smoke test retries only briefly and can lose the startup race, so its exit
  # code is advisory here; the readiness poll below is the real gate.
  if ! WORKER_REPLICAS=$n "$ROOT_DIR/scripts/start.sh" >> "$SWEEP_LOG" 2>&1; then
    log "replicas=$n: start.sh reported failure, deferring to the readiness poll"
  fi

  if ! wait_for_rpc_path "$n"; then
    log "replicas=$n: RPC path never became ready"
    printf '%s\tNA\tNA\tNA\tNA\n' "$n" >> "$RESULTS"
    CACHE[$n]=0
    return
  fi

  log "replicas=$n: running the long plan (50,000 requests)"
  local jm_log="$OUT_DIR/jmeter-$n.log"
  "$ROOT_DIR/scripts/jmeter-helper.sh" --long > "$jm_log" 2>&1

  # Cumulative summariser line, e.g.:
  # summary =  50000 in 00:00:50 =  993.4/s Avg: ... Err:     0 (0.00%)
  line=$(grep -E '^summary =' "$jm_log" | tail -1)
  if [[ -z "$line" ]]; then
    log "replicas=$n: no JMeter summary found (see $jm_log)"
    printf '%s\tNA\tNA\tNA\tNA\n' "$n" >> "$RESULTS"
    CACHE[$n]=0
    return
  fi
  rps=$(sed -E 's|.* ([0-9.]+)/s.*|\1|' <<< "$line")
  dur=$(sed -E 's|.* in +([0-9:]+) =.*|\1|' <<< "$line")
  err=$(sed -E 's|.*Err: +([0-9]+) .*|\1|' <<< "$line")
  err_pct=$(sed -E 's|.*Err: +[0-9]+ \(([0-9.]+)%\).*|\1|' <<< "$line")
  printf '%s\t%s\t%s\t%s\t%s\n' "$n" "$rps" "$err" "$err_pct" "$dur" >> "$RESULTS"
  log "replicas=$n: ${rps}/s, errors=$err (${err_pct}%), duration=$dur"
  RPS=$rps
  CACHE[$n]=$rps
}

# Exponential probe: double until throughput stops increasing (or the cap is hit).
measure 1
best_n=1
best_rps=$RPS
hi=""
n=2
while (( n <= MAX )); do
  measure "$n"
  if gt "$RPS" "$best_rps"; then
    best_n=$n
    best_rps=$RPS
    n=$((n * 2))
  else
    hi=$n
    break
  fi
done

# Binary search inside the bracket (best_n, hi): a midpoint beating the current best moves the
# lower bound up; otherwise the peak lies below it.
if [[ -z "$hi" ]]; then
  log "throughput was still increasing at the cap ($MAX); rerun with a higher max to keep probing"
else
  lo=$best_n
  while (( hi - lo > 1 )); do
    mid=$(( (lo + hi) / 2 ))
    measure "$mid"
    if gt "$RPS" "$best_rps"; then
      best_n=$mid
      best_rps=$RPS
      lo=$mid
    else
      hi=$mid
    fi
  done
fi

log "search done: final teardown"
"$ROOT_DIR/scripts/docker-helper.sh" --stop --volumes --prune >> "$SWEEP_LOG" 2>&1

echo
{ head -1 "$RESULTS"; tail -n +2 "$RESULTS" | sort -n; } | column -t -s $'\t'
echo -e "\nBest throughput at replicas=$best_n (${best_rps}/s) — check err_pct above before adopting it as the default"
