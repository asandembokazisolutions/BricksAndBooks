# Exact Firestore Setup Instructions for Bricks And Books

This setup uses your existing Firebase project:

```text
Project ID: bricks-and-books
Database: (default)
```

Your screenshot already shows that the Firestore database exists. Firestore does not use pre-created SQL tables. It creates a collection the first time an application writes a document to it. The steps below install rules, indexes, an administrator profile, and metadata so the database is ready for your web and Android development.

## Part 1 — Enable Firebase Authentication

1. In Firebase Console, open the `Bricks And Books` project.
2. In the left menu, select **Build → Authentication**.
3. Click **Get started**.
4. Open the **Sign-in method** tab.
5. Choose **Email/Password**, turn on **Enable**, and click **Save**.
6. Open the **Users** tab.
7. Click **Add user** and create your first administrator account. Use an email address you control and a strong temporary password.
8. After the account appears in the users list, copy its **User UID**. Keep this UID available; it is needed in Part 4.

> Do not add the password to Firestore. Firebase Authentication holds sign-in credentials; Firestore holds the user profile, business data, and permissions.

## Part 2 — Install the Firebase command line tool

These commands are for **Windows PowerShell**. Open PowerShell in the project folder that contains `firebase.json`.

```powershell
npm install -g firebase-tools
firebase login
firebase projects:list
```

A browser window may open so you can sign in to the Google account that owns `bricks-and-books`. Check that `bricks-and-books` appears in `firebase projects:list`.

Then connect this folder to your Firebase project:

```powershell
firebase use --add
```

When prompted, choose **bricks-and-books** and give it the alias `default`.

## Part 3 — Deploy the Firestore security rules and indexes

The backend-local `firebase.json` points to the canonical hybrid app rules at `../firebase/proposed-firestore.rules`; the index file remains local:

```text
firebase.json
../firebase/proposed-firestore.rules
firestore/firestore.indexes.json
```

From the folder containing `firebase.json`, run:

```powershell
firebase deploy --only firestore
```

This publishes the canonical rules and starts building the composite indexes. In Firebase Console, open **Firestore Database → Rules** and confirm that `proposed-firestore.rules` is the published policy. Open **Indexes** to monitor index creation; use the app only after the required indexes are enabled.

The rules start with a secure default: public residence listings are readable, signed-in residents can read only permitted records (including published announcements), and sensitive mutations and finance/admin data remain server-only. Test these rules in the **Rules** simulator before launching the application. Do not deploy the legacy `firestore/firestore.rules` file directly; it does not match the hybrid native app contract.

## Part 4 — Create starter records in Firestore

The `seed-firestore.mjs` script creates only three safe records:

| Collection and document | Purpose |
|---|---|
| `settings/app` | Application metadata and supported roles. |
| `schemaMetadata/v1` | Lists the Firestore collections used by the application. |
| `users/{YOUR_ADMIN_UID}` | The first administrator profile. |

### 4.1 Create a temporary service-account key

This key is used only for running the local seed script. It gives privileged server access, so it must never be committed to source control, included in the Android app, or included in browser JavaScript.

1. In Firebase Console, click the gear icon beside **Project Overview** and choose **Project settings**.
2. Open the **Service accounts** tab.
3. Choose **Firebase Admin SDK**.
4. Click **Generate new private key**, then confirm the download.
5. Move the downloaded JSON file outside the Bricks And Books project folder, for example to `C:\FirebaseSecrets\bricks-and-books-admin.json`.
6. Do not share this file. Revoke and replace it immediately if it is exposed.

### 4.2 Run the seed script

In PowerShell, still in the project folder, install the temporary script dependency and point Node.js to the service-account key:

```powershell
cd firestore
npm install firebase-admin
$env:GOOGLE_APPLICATION_CREDENTIALS = "C:\FirebaseSecrets\bricks-and-books-admin.json"
node .\seed-firestore.mjs "PASTE_ADMIN_UID_HERE" "admin@example.com" "YourFirstName" "YourLastName"
```

Replace all four values with the administrator Firebase Authentication UID, email, first name, and last name you actually created in Part 1.

When the command reports success, return to **Firebase Console → Firestore Database → Data**. You should see these collections:

```text
settings
schemaMetadata
users
```

Open `users` and verify that the document ID is the same as the Authentication UID and that its `role` is `admin`.

## Part 5 — Create application records

Do not create empty collections manually. Firestore creates them as soon as the application writes real records. The app should use these collections:

```text
users/{firebaseUid}
accommodations/{accommodationId}
units/{unitId}
tariffs/{tariffId}
accommodationApplications/{applicationId}
occupancies/{occupancyId}
bills/{billId}
payments/{paymentId}
paymentAllocations/{allocationId}
maintenanceRequests/{requestId}
notifications/{notificationId}
passwordResetRequests/{requestId}  (server-only)
vehicles/{vehicleId}
reviews/{reviewId}
chatMessages/{messageId}
auditLogs/{auditLogId}
```

For your first test record, create an accommodation using the Firebase Console.

1. In **Firestore Database → Data**, click **Start collection**.
2. For **Collection ID**, enter `accommodations`.
3. For **Document ID**, enter `demo-accommodation-001`.
4. Add these fields exactly:

| Field | Firestore type | Value |
|---|---|---|
| `name` | string | `Demo Residence` |
| `address` | string | `Replace with the real address` |
| `description` | string | `Starter record for Bricks And Books` |
| `imageUrls` | array | `[]` |
| `createdAt` | timestamp | Select the current date and time |
| `updatedAt` | timestamp | Select the current date and time |

5. Click **Save**.

The `accommodations` collection will immediately appear. Once the web or Android application is connected to Firestore, this same document can be read by both applications.

## Part 6 — Configure the Android app

1. In Firebase Console, open **Project settings → General**.
2. Under **Your apps**, click the Android icon.
3. Enter the Android app package name exactly as it appears in the Android project, for example `com.example.bricksandbooks`.
4. Download `google-services.json`.
5. Put it inside the Android project’s `app` folder: `app/google-services.json`.
6. Add Firebase Authentication and Firestore libraries using the Firebase Android BoM. The Android app must sign users in with Firebase Authentication before it calls Firestore.

## Part 7 — Connect the existing web project

The MVC application already uses Firestore as its durable data store. It keeps Entity Framework Core's in-memory provider only as a short-lived compatibility cache for existing MVC and ASP.NET Identity code; on startup, the cache is rebuilt from Firestore and every subsequent save is persisted to Firestore.

Password-reset requests are stored as `passwordResetRequests/{requestId}`. Each record contains the requester’s internal user ID, request timestamp, status, and—once completed—the resolver’s user ID and resolution timestamp. The collection is intentionally **server-only**: the browser and mobile-client rules deny every operation, while the application accesses it through server credentials. Do not store temporary passwords or password-reset tokens in Firestore.

For local development, set `GOOGLE_APPLICATION_CREDENTIALS` to an uncommitted Firebase service-account JSON file and configure `Firebase:ProjectId` (plus `Firebase:DatabaseId` when a non-default database is used).

## Final checks

| Check | Expected result |
|---|---|
| Firebase Authentication → Users | Your administrator account appears and has a UID. |
| Firestore → Data | `settings`, `schemaMetadata`, and `users` collections are visible. |
| Firestore → Data → users | The administrator document ID equals the Authentication UID and the role is `admin`. |
| Firestore → Rules | The supplied rules are published. |
| Firestore → Indexes | The requested indexes are building or enabled. |
| Firestore → Data → accommodations | The `demo-accommodation-001` record is visible after Part 5. |
| Password reset workflow | Submit **Forgot password?** for a real account. A `passwordResetRequests` document appears after the application starts with valid server credentials; the document moves from `Pending` to `Completed` when a reviewer issues a temporary password. |

## References

[1] Firebase, [Get started with Cloud Firestore](https://firebase.google.com/docs/firestore/quickstart).

[2] Firebase, [Get started with Cloud Firestore Security Rules](https://firebase.google.com/docs/firestore/security/get-started).

[3] Firebase, [Firebase CLI reference](https://firebase.google.com/docs/cli).

[4] Firebase, [Cloud Firestore index definition reference](https://firebase.google.com/docs/reference/firestore/indexes).
