#!/bin/bash

# The test plans and their .jtl results live in jmeter/, not beside this script, so run from there:
# the plan and results filenames below resolve against it, as does JMeter's own jmeter.log.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT/jmeter"

JMETER_VERSION="${JMETER_VERSION:-5.6.3}"
JMETER_HOME="${JMETER_HOME:-$HOME/.local/share/apache-jmeter-$JMETER_VERSION}"

usage() {
    cat <<EOF
Usage: $(basename "$0") [minimal|long] [-h|--help]

Runs an Apache JMeter load test against the Todo application on http://localhost:5000.
Start the application first with scripts/start-todo-app.sh.

Modes:
  minimal   2 threads * 5 loops = 10 requests (default)
  long      200 threads * 500 loops = 100,000 requests

Results are written to jmeter/results-<mode>.jtl.

If jmeter is not on PATH, JMeter $JMETER_VERSION is downloaded, checksum-verified, and unpacked into
\$JMETER_HOME (default: \$HOME/.local/share/apache-jmeter-$JMETER_VERSION). No sudo, and nothing
outside that directory is touched. Requires java on PATH.

Environment:
  JMETER_VERSION   version to install when jmeter is absent (default: 5.6.3)
  JMETER_HOME      install location (default: \$HOME/.local/share/apache-jmeter-\$JMETER_VERSION)
EOF
}

die() { echo "Error: $*" >&2; exit 1; }

# Unpacks the release tarball under JMETER_HOME's parent; the archive's top-level directory is
# apache-jmeter-<version>, which is exactly the leaf JMETER_HOME defaults to.
install_jmeter() {
    command -v java > /dev/null \
        || die "JMeter requires Java, which is not on PATH. Install it, e.g. sudo apt-get install -y default-jre"

    local archive="apache-jmeter-${JMETER_VERSION}.tgz"
    local url="https://archive.apache.org/dist/jmeter/binaries/${archive}"
    local tmp
    tmp="$(mktemp -d)" || die "cannot create a temporary directory"
    trap 'rm -rf "$tmp"' EXIT

    echo "jmeter not found on PATH. Installing JMeter ${JMETER_VERSION} into ${JMETER_HOME}..."
    curl -fsSL --retry 3 -o "$tmp/$archive" "$url" || die "failed to download $url"
    curl -fsSL --retry 3 -o "$tmp/$archive.sha512" "$url.sha512" || die "failed to download $url.sha512"

    # A corrupted or tampered archive must abort before anything from it is executed.
    (cd "$tmp" && sha512sum -c "$archive.sha512" > /dev/null) || die "checksum verification failed for $archive"

    mkdir -p "$(dirname "$JMETER_HOME")" || die "cannot create $(dirname "$JMETER_HOME")"
    tar -xzf "$tmp/$archive" -C "$(dirname "$JMETER_HOME")" || die "failed to extract $archive"
    [ -x "$JMETER_HOME/bin/jmeter" ] || die "extracted archive has no executable at $JMETER_HOME/bin/jmeter"

    echo "Installed JMeter ${JMETER_VERSION}."
}

case "${1:-minimal}" in
    -h|--help)
        usage
        exit 0
        ;;
    minimal)
        test_plan="test-minimal.jmx"
        results_file="results-minimal.jtl"
        ;;
    long)
        test_plan="test-long.jmx"
        results_file="results-long.jtl"
        ;;
    *)
        usage >&2
        exit 2
        ;;
esac

if ! command -v jmeter > /dev/null; then
    [ -x "$JMETER_HOME/bin/jmeter" ] || install_jmeter
    PATH="$JMETER_HOME/bin:$PATH"
fi

# Record start time
start_time=$(date +%s)

# Run JMeter test in non-GUI mode
jmeter -Djava.security.egd=file:/dev/urandom \
       -Dxstream.allow=org.apache.jmeter.save.ScriptWrapper \
       -n -t "$test_plan" -l "$results_file"

exit_code=$?

# Record end time and calculate duration
end_time=$(date +%s)
duration=$((end_time - start_time))
minutes=$((duration / 60))
seconds=$((duration % 60))

# Check if test was successful
if [ $exit_code -eq 0 ]; then
    echo "Test completed successfully in ${minutes}m ${seconds}s!"
    echo "Results available in: jmeter/${results_file}"

    # Check container logs for errors and warnings
    # echo "Checking container logs for issues..."
    # cd ..
    # echo "WebAPI Errors/Warnings:"
    # docker compose -p todo-app logs webapi 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
    
    # echo "Worker Errors/Warnings:"
    # docker compose -p todo-app logs worker 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
    
    # echo "Postgres Errors/Warnings:"
    # docker compose -p todo-app logs postgres 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
    
    # echo "RabbitMQ Errors/Warnings:"
    # docker compose -p todo-app logs rabbitmq 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
else
    echo "Test failed after ${minutes}m ${seconds}s!"
    echo "Results available in: jmeter/${results_file}"
    exit $exit_code
fi
