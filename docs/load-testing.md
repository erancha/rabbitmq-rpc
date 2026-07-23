# Load Testing

With the application running (see [Getting Started](../README.md#getting-started)), drive load
against it using the JMeter test plans in `jmeter/`. If `jmeter` is not on your PATH, the helper
downloads and installs it locally (only Java is required).

```bash
# Minimal: 2 threads * 5 loops = 10 requests (the default)
./scripts/jmeter-helper.sh

# Long: 200 threads * 250 loops = 50,000 requests
./scripts/jmeter-helper.sh --long
```

By default only JMeter's console summary is printed; pass `--jtl` to also write per-request
results to `jmeter/results-<mode>.jtl`. See
[deploy/README.md](../deploy/README.md#jmeter-load-testing) for what each plan exercises and what
to expect in the database.

Worker replica count is the scaling lever these plans exercise; see the
[Scalability notes](architecture.md#scalability-notes) for how the competing-consumer replicas
are tuned.
