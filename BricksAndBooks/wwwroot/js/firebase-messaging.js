import { initializeApp } from "https://www.gstatic.com/firebasejs/12.17.1/firebase-app.js";
import { getAnalytics, isSupported as analyticsIsSupported } from "https://www.gstatic.com/firebasejs/12.17.1/firebase-analytics.js";
import { getMessaging, getToken, isSupported as messagingIsSupported, onMessage } from "https://www.gstatic.com/firebasejs/12.17.1/firebase-messaging.js";

const config = window.bricksAndBooksFirebaseConfig;

if (config?.apiKey && config?.projectId && config?.messagingSenderId && config?.appId) {
    const app = initializeApp(config);

    analyticsIsSupported().then((supported) => {
        if (supported) {
            getAnalytics(app);
        }
    }).catch(() => undefined);

    messagingIsSupported().then(async (supported) => {
        if (!supported || !("serviceWorker" in navigator) || !("Notification" in window)) {
            return;
        }

        const registration = await navigator.serviceWorker.register("/firebase-messaging-sw.js");
        const messaging = getMessaging(app);

        // The VAPID key is intentionally optional until one is generated in Firebase Console.
        // Without it, the app can still receive foreground messages when configured by the host.
        if (config.vapidKey) {
            const permission = await Notification.requestPermission();
            if (permission === "granted") {
                const token = await getToken(messaging, {
                    vapidKey: config.vapidKey,
                    serviceWorkerRegistration: registration
                });

                if (token) {
                    window.dispatchEvent(new CustomEvent("bricksandbooks:fcm-token", { detail: token }));
                    console.debug("Firebase messaging token ready.");
                }
            }
        }

        onMessage(messaging, (payload) => {
            const title = payload.notification?.title || "BricksAndBooks update";
            const body = payload.notification?.body || "You have a new notification.";
            window.dispatchEvent(new CustomEvent("bricksandbooks:fcm-message", { detail: payload }));

            if (Notification.permission === "granted" && document.visibilityState !== "visible") {
                new Notification(title, { body, icon: "/favicon.ico" });
            }
        });
    }).catch((error) => {
        console.warn("Firebase Cloud Messaging could not be initialized.", error);
    });
}
