# Password Reset Requests in Firestore

## Overview

The password-request workflow now persists its audit records in the `passwordResetRequests` Firestore collection. The existing MVC and ASP.NET Identity implementation is unchanged: a user submits **Forgot password?**, an administrator or financial officer reviews the pending queue, and the server issues a temporary password after approval.

The application uses its Firestore persistence service as the durable store and Entity Framework Core's in-memory provider as a short-lived compatibility cache. When the application starts, it rebuilds that cache from Firestore. When the workflow creates or completes a request, the normal repository `Save()` call writes the record to Firestore.

## Stored record shape

| Firestore field | Source property | Meaning |
|---|---|---|
| `id` | `PasswordResetRequest.Id` | Numeric request identifier and Firestore document ID. |
| `userId` | `PasswordResetRequest.UserId` | Internal ASP.NET Identity ID of the requesting account. |
| `status` | `PasswordResetRequest.Status` | `Pending` until an authorized reviewer completes the request, then `Completed`. |
| `requestedAt` | `PasswordResetRequest.RequestedAt` | UTC timestamp when the request was made. |
| `resolvedAt` | `PasswordResetRequest.ResolvedAt` | UTC timestamp when the reset was completed; absent until then. |
| `resolvedByUserId` | `PasswordResetRequest.ResolvedByUserId` | Internal identity ID of the administrator or financial officer who completed the request; absent until then. |

No temporary password, plaintext password, Firebase password-reset token, or ASP.NET Identity password-reset token is stored in Firestore.

## Security model

The `passwordResetRequests` collection is explicitly denied to every browser and mobile client in `firestore.rules`. This protects internal account-recovery workflow data, including the requested account ID and approval history. The MVC server writes these documents through its Google Cloud Firestore server credentials; those credentials are evaluated by IAM rather than Firestore client rules.

> Keep the Firebase service-account JSON file outside source control. For local execution, point `GOOGLE_APPLICATION_CREDENTIALS` at that file and configure `Firebase:ProjectId` in application configuration.

## Deploy and verify

Publish the updated Firestore rules from the application root before relying on the new collection:

```bash
firebase deploy --only firestore:rules
```

Start the application with valid Firebase server credentials, submit a password-reset request for an existing account, and inspect Firestore Console. The first request creates `passwordResetRequests/{requestId}` with `status: "Pending"`. After an administrator or financial officer resolves it in the MVC queue, the same document becomes `Completed` and gains `resolvedAt` and `resolvedByUserId`.

No Firestore composite index is required for this workflow because the MVC application queries its hydrated compatibility cache, rather than issuing a filtered Firestore client query.

## Related files

| File | Purpose |
|---|---|
| `Data/Firestore/FirestorePersistenceService.cs` | Registers the `PasswordResetRequest` entity-to-collection mapping and exposes schema metadata version 3. |
| `firestore/firestore.rules` | Explicitly denies all client access to `passwordResetRequests`. |
| `firestore/seed-firestore.mjs` | Advertises the collection in schema metadata version 3 for new Firestore environments. |
| `firestore/SETUP_WINDOWS.md` | Contains Windows/Firebase setup and workflow validation instructions. |
