import http from 'k6/http';
import { check, fail, sleep } from 'k6';

if (__ENV.PERF_TEST_ISOLATED !== 'true') fail('Set PERF_TEST_ISOLATED=true only for a dedicated test database and API.');
const baseUrl = __ENV.BASE_URL;
const documentId = __ENV.DOCUMENT_ID;
const tokens = {
  employee: __ENV.EMPLOYEE_TOKEN,
  manager: __ENV.MANAGER_TOKEN,
  admin: __ENV.ADMIN_TOKEN,
};
if (!baseUrl || !documentId || Object.values(tokens).some((token) => !token))
  fail('BASE_URL, DOCUMENT_ID and role-specific tokens are required.');

export const options = {
  scenarios: {
    documents: { executor: 'constant-vus', vus: 3, duration: '2m', exec: 'documents' },
    approvals: { executor: 'constant-vus', vus: 2, duration: '2m', exec: 'approvals' },
    audit: { executor: 'constant-vus', vus: 1, duration: '2m', exec: 'audit' },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1000'],
  },
};

function get(path, role, name) {
  const response = http.get(`${baseUrl}${path}`, {
    headers: { Authorization: `Bearer ${tokens[role]}` },
    tags: { name },
  });
  check(response, { [`${name} returns 200`]: (result) => result.status === 200 });
}

export function documents() {
  get('/api/documents?page=1&pageSize=20', 'employee', 'documents-list');
  get(`/api/documents/${documentId}`, 'employee', 'document-detail');
  sleep(1);
}

export function approvals() {
  get('/api/approvals?page=1&pageSize=20', 'manager', 'approvals-list');
  sleep(1);
}

export function audit() {
  get('/api/audit?page=1&pageSize=20', 'admin', 'audit-list');
  sleep(1);
}
