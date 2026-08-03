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

## Reference measurement

The long plan on a 6-CPU Docker host with 8 worker replicas and a clean-slate stack per run
sustains 627-635 req/s with zero errors and ~312 ms average latency. Two RPC-edge choices account
for that number, each measured against the same plan: the
[split publish/consume connections](architecture.md#threading-model) contribute +7.5% over a shared
connection, and [`prefetchCount: 5`](architecture.md#scalability-notes) a further +27%, together
+38% over 459 req/s. Host CPU sits near saturation at that rate with Postgres the hottest container,
so the next ceiling is database work rather than broker configuration.
