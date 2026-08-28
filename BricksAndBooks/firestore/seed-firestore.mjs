import { applicationDefault, getApps, initializeApp } from 'firebase-admin/app';
import { FieldValue, getFirestore } from 'firebase-admin/firestore';

const projectId = 'bricks-and-books';
const adminUid = process.argv[2];
const adminEmail = process.argv[3] ?? '';
const firstName = process.argv[4] ?? 'System';
const lastName = process.argv[5] ?? 'Administrator';

if (!adminUid) {
  console.error('Usage: node seed-firestore.mjs <FIREBASE_AUTH_UID> [email] [firstName] [lastName]');
  process.exit(1);
}

if (!getApps().length) {
  initializeApp({
    credential: applicationDefault(),
    projectId,
  });
}

const db = getFirestore();
const serverTimestamp = FieldValue.serverTimestamp();
const batch = db.batch();

batch.set(db.collection('settings').doc('app'), {
  appName: 'Bricks And Books',
  schemaVersion: 1,
  supportedRoles: ['student', 'staff', 'financialOfficer', 'admin'],
  createdAt: serverTimestamp,
  updatedAt: serverTimestamp,
});

batch.set(db.collection('schemaMetadata').doc('v1'), {
  version: 1,
  projectId,
  collections: [
    'users',
    'accommodations',
    'units',
    'tariffs',
    'accommodationApplications',
    'occupancies',
    'bills',
    'payments',
    'maintenanceRequests',
    'notifications',
    'vehicles',
    'reviews',
    'chatThreads',
    'auditLogs',
  ],
  createdAt: serverTimestamp,
});

batch.set(db.collection('users').doc(adminUid), {
  email: adminEmail,
  firstName,
  lastName,
  role: 'admin',
  isStudent: false,
  isStaff: true,
  isFinancialOfficer: true,
  isAdmin: true,
  status: 'active',
  createdAt: serverTimestamp,
  updatedAt: serverTimestamp,
});

await batch.commit();
console.log(`Firestore starter data created for ${projectId}.`);
console.log(`Administrator profile: users/${adminUid}`);
console.log('Open Firebase Console > Firestore Database > Data to confirm the documents.');
