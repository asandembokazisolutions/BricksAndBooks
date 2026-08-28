importScripts("https://www.gstatic.com/firebasejs/12.17.1/firebase-app-compat.js");
importScripts("https://www.gstatic.com/firebasejs/12.17.1/firebase-messaging-compat.js");

firebase.initializeApp({
    apiKey: "AIzaSyA9SylRwUPcwa0mdKMebVMeqbneX0r9Ay8",
    authDomain: "bricks-and-books.firebaseapp.com",
    projectId: "bricks-and-books",
    storageBucket: "bricks-and-books.firebasestorage.app",
    messagingSenderId: "451241035676",
    appId: "1:451241035676:web:e835c70977c22d62942a59",
    measurementId: "G-2ZK8VLC0LG"
});

const messaging = firebase.messaging();

messaging.onBackgroundMessage((payload) => {
    const title = payload.notification?.title || "BricksAndBooks update";
    const options = {
        body: payload.notification?.body || "You have a new notification.",
        icon: "/favicon.ico",
        data: payload.data || {}
    };

    self.registration.showNotification(title, options);
});

self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    event.waitUntil(clients.matchAll({ type: "window", includeUncontrolled: true }).then((clientList) => {
        const existing = clientList.find((client) => "focus" in client);
        if (existing) {
            return existing.focus();
        }
        return clients.openWindow("/");
    }));
});
