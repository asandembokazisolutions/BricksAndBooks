# Bricks & Books

Bricks & Books is an ASP.NET Core MVC accommodation and residence-operations application for the University of the Free State context. It supports accommodation discovery and applications, resident accounts, billing and payments, maintenance requests, messaging, notifications, reviews, reporting, audit history, and administrator workflows.

## Technology

| Area | Technology |
| --- | --- |
| Web application | ASP.NET Core MVC on .NET 8 |
| Authentication | ASP.NET Core Identity with role-based authorization |
| Durable data | Google Cloud Firestore |
| Compatibility cache | Entity Framework Core InMemory provider |
| Payments | Stripe integration |
| Front-end | Razor views, Bootstrap 5, custom CSS and JavaScript |
| Push messaging | Firebase Cloud Messaging |
| Firestore tooling | Firebase CLI and Node.js seed script |

## Prerequisites

Install the following before running the application:

- .NET 8 SDK
- Node.js and npm, required for the Firestore seed script
- A Firebase project with Authentication and Firestore enabled
- A service-account key stored outside this repository
- Stripe test or live credentials, depending on the environment

The repository includes detailed Firebase instructions in `firestore/SETUP_WINDOWS.md` and supporting migration and validation notes in `docs/`.

## Configuration

Do not commit private credentials to the repository. Store local secrets with .NET User Secrets, environment variables, or an untracked `appsettings.Development.json` file.

The application expects the following configuration sections:

```json
{
  "Stripe": {
    "SecretKey": "<stripe-secret-key>",
    "PublishableKey": "<stripe-publishable-key>"
  },
  "Firebase": {
    "ProjectId": "<firebase-project-id>",
    "DatabaseId": "(default)",
    "ApiKey": "<firebase-web-api-key>",
    "AuthDomain": "<firebase-auth-domain>",
    "StorageBucket": "<firebase-storage-bucket>",
    "MessagingSenderId": "<firebase-messaging-sender-id>",
    "AppId": "<firebase-web-app-id>",
    "MeasurementId": "<firebase-measurement-id>",
    "VapidKey": "<firebase-cloud-messaging-vapid-key>"
  }
}
```

Set the Google Application Default Credentials path to a service-account JSON file kept outside the project directory. In PowerShell:

```powershell
$env:GOOGLE_APPLICATION_CREDENTIALS = "C:\FirebaseSecrets\bricks-and-books-service-account.json"
```

For local development, User Secrets are preferable:

```powershell
dotnet user-secrets init
dotnet user-secrets set "Stripe:SecretKey" "<stripe-secret-key>"
dotnet user-secrets set "Stripe:PublishableKey" "<stripe-publishable-key>"
dotnet user-secrets set "Firebase:VapidKey" "<firebase-vapid-key>"
```

Firebase web configuration values may be present in browser configuration because they identify the Firebase project, but service-account private keys and Stripe secret keys must remain server-side.

## Running locally

From the project directory:

```powershell
dotnet restore
dotnet build
dotnet run
```

Open the HTTPS URL printed by `dotnet run`. The application uses the default MVC route:

```text
/{controller=Home}/{action=Index}/{id?}
```

The startup process creates the compatibility cache, hydrates it from Firestore, initializes application seed data, and ensures Firestore metadata is available. The application requires valid Firebase configuration and Application Default Credentials during startup.

## Firebase setup

Use the following sequence for a new environment:

1. Create or select the Firebase project.
2. Enable Firebase Authentication and configure the required sign-in provider.
3. Create the Firestore database in the intended region.
4. Deploy the security rules and composite indexes from the `firestore/` directory.
5. Create the first administrator account.
6. Keep the service-account key outside the repository and set `GOOGLE_APPLICATION_CREDENTIALS` only in the local environment or deployment secret store.
7. Run the Firestore seed script described in `firestore/SETUP_WINDOWS.md`.

The Firestore rules are the security boundary for application data. Review them before deploying to a new Firebase project.

## Main workflows

| User area | Typical capabilities |
| --- | --- |
| Public visitor | Browse accommodation and begin account access or application workflows |
| Student or staff resident | Submit accommodation requests, view bills and payment history, manage vehicles, request maintenance, send messages, and leave feedback |
| Finance officer | Review customers, bills, payments, transaction details, reports, and payment anomalies |
| Administrator | Manage users, accommodations, units, tariffs, applications, audit records, and operational settings |

Accommodation applications remain pending until an administrator reviews them. Approval assigns a unit only after the server verifies that the unit belongs to the requested accommodation and has capacity. Termination requests close approved occupancies only after administrator approval.

## Payment and receipt behavior

Payments are processed through the payment workflow and can produce itemised digital receipts. Keep Stripe secret keys on the server and use test keys for local development. Never place a secret key in Razor views, JavaScript, or a committed configuration file.

## Notifications and messaging

The application supports stored notifications and Firebase Cloud Messaging. Authenticated users can view their notification history. Foreground messages can be shown in the in-app notification pop-up, while browser notifications depend on permission and Firebase Cloud Messaging configuration. The Firebase service worker is located at `wwwroot/firebase-messaging-sw.js`.

## Project layout

```text
Controllers/       MVC controllers and application workflows
Data/              Repositories, persistence, Firestore hydration, and seed data
Infrastructure/    Payments, notifications, auditing, tariffs, and anomaly detection
Models/            Domain entities and view models
Views/             Razor MVC views
wwwroot/           CSS, JavaScript, images, libraries, and service worker
firestore/         Firestore rules, indexes, setup notes, and seed tooling
docs/              Migration and validation documentation
```

## Security checklist

Before committing or deploying, confirm that service-account JSON files, Stripe secret keys, private certificates, local environment files, development settings, database files, logs, and IDE-specific files are not tracked. The root `.gitignore` contains the project’s default protections, but files that were committed previously must also be removed from Git history or rotated as appropriate.

Never commit a Firebase Admin SDK key. If a credential is exposed, revoke it in the provider console, create a replacement, and update the deployment secret rather than only deleting the local file.

## Validation

Run the following checks before opening a pull request:

```powershell
dotnet restore
dotnet build
dotnet run
```

Then verify registration and login, role-based navigation, accommodation application approval, payment and receipt flows, notification delivery, maintenance requests, messaging, and the relevant Firestore rules and indexes.

## Existing documentation

- `firestore/SETUP_WINDOWS.md` — Firebase and Firestore setup procedure
- `docs/FIRESTORE_MIGRATION.md` — Firestore migration notes
- `docs/VALIDATION.md` — validation status and environment requirements
- `ACCOMMODATION_WORKFLOW.md` — accommodation application and termination workflow
- `SEEDING.md` — application seed-data guidance
- `INTEGRATIONS.md` — integration notes
