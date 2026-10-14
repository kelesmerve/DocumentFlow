-- Run only against a dedicated performance database with representative data.
EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id", "DocumentNumber", "Title", "Status", "CreatedAtUtc"
FROM documents
WHERE "OwnerId" = (SELECT "Id" FROM users WHERE "Role" = 'Employee' LIMIT 1)
ORDER BY "CreatedAtUtc" DESC, "Id" LIMIT 20;

EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id", "DocumentId", "Status", "CreatedAtUtc"
FROM approval_requests
WHERE "AssignedToUserId" = (SELECT "Id" FROM users WHERE "Role" = 'Manager' LIMIT 1)
ORDER BY "CreatedAtUtc" DESC, "Id" DESC LIMIT 20;

EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id", "Action", "UserId", "CreatedAtUtc"
FROM audit_logs
WHERE "Action" = 'DocumentDownloaded'
ORDER BY "CreatedAtUtc" DESC, "Id" DESC LIMIT 20;

EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id", "EventType", "Status", "CreatedAtUtc"
FROM webhook_deliveries
ORDER BY "CreatedAtUtc" DESC, "Id" DESC LIMIT 20;
