#!/bin/bash

# The test plans (and their .jtl results, when --jtl is passed) live in jmeter/, not beside this
# script, so run from there: the plan and results filenames below resolve against it, as does
# JMeter's own jmeter.log.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT/jmeter"

JMETER_VERSION="${JMETER_VERSION:-5.6.3}"
JMETER_HOME="${JMETER_HOME:-$HOME/.local/share/apache-jmeter-$JMETER_VERSION}"

usage() {
    cat <<EOF
Usage: $(basename "$0") [--long] [--jtl] [-h|--help]

Runs an Apache JMeter load test against the Todo application on http://localhost:5000.
Start the application first with scripts/start.sh.

Options:
  --long    run the long plan: 200 threads * 250 loops = 50,000 requests
            (default: the minimal plan, 2 threads * 5 loops = 10 requests)
  --jtl     also write per-request results to jmeter/results-<mode>.jtl; by default only
            JMeter's console summary is produced

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

mode="minimal"
write_jtl=false
for arg in "$@"; do
    case "$arg" in
        -h|--help)
            usage
            exit 0
            ;;
        --long)
            mode="long"
            ;;
        --jtl)
            write_jtl=true
            ;;
        *)
            usage >&2
            exit 2
            ;;
    esac
done

test_plan="test-${mode}.jmx"
results_file="results-${mode}.jtl"

if ! command -v jmeter > /dev/null; then
    [ -x "$JMETER_HOME/bin/jmeter" ] || install_jmeter
    PATH="$JMETER_HOME/bin:$PATH"
fi

start_time=$(date +%s)

# Run JMeter test in non-GUI mode; the results log is opt-in via --jtl
jtl_args=()
if $write_jtl; then
    jtl_args=(-l "$results_file")
fi

jmeter -Djava.security.egd=file:/dev/urandom \
       -Dxstream.allow=org.apache.jmeter.save.ScriptWrapper \
       -n -t "$test_plan" "${jtl_args[@]}"

exit_code=$?

end_time=$(date +%s)
duration=$((end_time - start_time))
minutes=$((duration / 60))
seconds=$((duration % 60))

if [ $exit_code -eq 0 ]; then
    echo "Test completed successfully in ${minutes}m ${seconds}s!"
else
    echo "Test failed after ${minutes}m ${seconds}s!"
fi

if $write_jtl; then
    echo "Results available in: jmeter/${results_file}"
fi

exit $exit_code
