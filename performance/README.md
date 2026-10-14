# Isolated read workload

`documentflow-read.js` exercises document list/detail, Manager approvals, and Admin audit reads. It never creates or changes records. It refuses to start unless `PERF_TEST_ISOLATED=true` is set.

Use a dedicated PostgreSQL database and separate file/key directories. Start the API against that database with `RabbitMq__Enabled=false`; in Development the API applies migrations and creates the three demo roles in that database. Create an employee-owned document in the isolated database, then supply its ID as `DOCUMENT_ID`. Obtain the three JWTs from the isolated API and pass them as environment variables. Do not save tokens in this repository or shell history. Keep the 10 MB upload limit and the production/demo databases untouched.

Required variables: `PERF_TEST_ISOLATED`, `BASE_URL`, `DOCUMENT_ID`, `EMPLOYEE_TOKEN`, `MANAGER_TOKEN`, `ADMIN_TOKEN`.

Run `k6 run --summary-export=summary.json performance/documentflow-read.js` from the repository root. Record p50/p95 from `http_req_duration`, requests per second from `http_reqs`, and error rate from `http_req_failed`. To compare before and after, use the same isolated fixture size, machine, API configuration, VU count, and duration. Run `performance/explain.sql` against the isolated database with `psql -f` to inspect scan and buffer behavior. Small datasets commonly use sequential scans even when an index is appropriate at scale.

k6 is optional. If it is unavailable, keep the scenario for a later isolated run and report no latency or throughput results.
