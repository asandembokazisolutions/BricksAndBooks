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
    const data = payload.notification || payload.data || {};
    const title = data.title || data.Title || "Bricks & Books update";
    const body = data.body || data.message || data.Message || "You have a new notification.";
    const url = data.url || data.Url || data.link || data.Link || "/Notification";

    self.registration.showNotification(title, {
        body,
        icon: "/images/ufs-logo.png",
        badge: "/images/ufs-logo.png",
        data: { url }
    });
});

self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    const url = event.notification.data?.url || "/Notification";
    event.waitUntil(clients.matchAll({ type: "window", includeUncontrolled: true }).then((clientList) => {
        const existing = clientList.find((client) => "focus" in client);
        if (existing) {
            existing.navigate(url);
            return existing.focus();
        }
        return clients.openWindow(url);
    }));
});
