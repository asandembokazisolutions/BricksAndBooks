#!/usr/bin/env bash
set -euo pipefail

PROJECT_ID="bricks-and-books"
DATABASE_ID="(default)"
BASE_URL="https://firestore.googleapis.com/v1/projects/${PROJECT_ID}/databases/${DATABASE_ID}/documents/users"
TOKEN="$(gcloud auth print-access-token)"

write_document() {
  local document_id="$1"
  local payload="$2"

  curl --fail-with-body --silent --show-error \
    --request PATCH \
    --header "Authorization: Bearer ${TOKEN}" \
    --header "Content-Type: application/json" \
    --data "${payload}" \
    "${BASE_URL}/${document_id}" >/dev/null
}

write_document "00000000-0000-0000-0000-000000000001" '{
  "fields": {
    "email": { "stringValue": "admin@bricksandbooks.com" },
    "userName": { "stringValue": "admin@bricksandbooks.com" },
    "firstName": { "stringValue": "Admin" },
    "lastName": { "stringValue": "User" },
    "isStudent": { "booleanValue": false },
    "isStaff": { "booleanValue": false },
    "isFinancialOfficer": { "booleanValue": false },
    "isAdmin": { "booleanValue": true },
    "role": { "stringValue": "admin" },
    "status": { "stringValue": "active" }
  }
}'

write_document "00000000-0000-0000-0000-000000000002" '{
  "fields": {
    "email": { "stringValue": "finance@bricksandbooks.com" },
    "userName": { "stringValue": "finance@bricksandbooks.com" },
    "firstName": { "stringValue": "Financial" },
    "lastName": { "stringValue": "Officer" },
    "isStudent": { "booleanValue": false },
    "isStaff": { "booleanValue": false },
    "isFinancialOfficer": { "booleanValue": true },
    "isAdmin": { "booleanValue": false },
    "role": { "stringValue": "financialOfficer" },
    "status": { "stringValue": "active" }
  }
}'

echo "Staff profiles written."
for document_id in \
  "00000000-0000-0000-0000-000000000001" \
  "00000000-0000-0000-0000-000000000002"; do
  curl --fail-with-body --silent --show-error \
    --header "Authorization: Bearer ${TOKEN}" \
    "${BASE_URL}/${document_id}"
  echo
done
