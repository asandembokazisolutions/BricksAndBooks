(() => {
    const sidebar = document.getElementById('appSidebar');
    const scrim = document.querySelector('.sidebar-scrim');
    const openButtons = document.querySelectorAll('[data-sidebar-open]');
    const closeButtons = document.querySelectorAll('[data-sidebar-close]');
    const setSidebar = (open) => {
        if (!sidebar || !scrim) return;
        sidebar.classList.toggle('open', open);
        scrim.classList.toggle('open', open);
        document.body.classList.toggle('overflow-hidden', open);
    };
    openButtons.forEach(button => button.addEventListener('click', () => setSidebar(true)));
    closeButtons.forEach(button => button.addEventListener('click', () => setSidebar(false)));
    window.addEventListener('resize', () => { if (window.innerWidth >= 992) setSidebar(false); });
})();


// Foreground notification pop-up: keep important updates visible without interrupting work.
(() => {
    const showNotificationPopup = (payload) => {
        const notification = payload?.notification || payload?.data || payload || {};
        const title = String(notification.title || 'Bricks & Books update');
        const body = String(notification.body || notification.message || 'You have a new notification.');
        const link = notification.url || '/Notification';
        const type = String(notification.type || '').toLowerCase();
        let popup = document.getElementById('notification-popup');

        if (!popup) {
            popup = document.createElement('aside');
            popup.id = 'notification-popup';
            popup.className = 'notification-popup';
            popup.setAttribute('role', 'status');
            popup.setAttribute('aria-live', 'polite');
            popup.innerHTML = `
        <div class="notification-popup-accent" aria-hidden="true"></div>
        <div class="notification-popup-content">
          <div class="notification-popup-heading">
            <span class="notification-popup-icon" aria-hidden="true"><i class="fa-regular fa-bell"></i></span>
            <span class="notification-popup-label">New notification</span>
            <button type="button" class="notification-popup-close" aria-label="Dismiss notification">&times;</button>
          </div>
          <strong class="notification-popup-title"></strong>
          <p class="notification-popup-message"></p>
          <a class="notification-popup-link" href="/Notification">Open notifications</a>
        </div>`;
            document.body.appendChild(popup);
            popup.querySelector('.notification-popup-close').addEventListener('click', () => popup.classList.remove('is-visible'));
        }

        popup.classList.toggle('is-important', ['paymentfailed', 'rentdue', 'maintenanceupdate'].includes(type));
        popup.querySelector('.notification-popup-title').textContent = title;
        popup.querySelector('.notification-popup-message').textContent = body;
        const popupLink = popup.querySelector('.notification-popup-link');
        popupLink.href = link;
        popupLink.textContent = link && link !== '/Notification' ? 'View related details' : 'Open notifications';
        popup.classList.remove('is-visible');
        window.requestAnimationFrame(() => popup.classList.add('is-visible'));
        window.clearTimeout(window.bricksAndBooksNotificationTimer);
        window.bricksAndBooksNotificationTimer = window.setTimeout(() => popup.classList.remove('is-visible'), 9000);
    };

    window.addEventListener('bricksandbooks:fcm-message', event => showNotificationPopup(event.detail));
    window.bricksAndBooksShowNotification = showNotificationPopup;
})();


// Stored notification fallback: surfaces application messages even without an FCM token.
(() => {
    const seenKey = 'bricksandbooks:seen-notification-ids';
    const readSeenIds = () => {
        try { return new Set(JSON.parse(window.sessionStorage.getItem(seenKey) || '[]')); }
        catch { return new Set(); }
    };
    const writeSeenIds = (ids) => {
        try { window.sessionStorage.setItem(seenKey, JSON.stringify([...ids].slice(-50))); }
        catch { /* Storage can be unavailable in private browsing. */ }
    };
    const checkUnread = async () => {
        if (typeof window.bricksAndBooksShowNotification !== 'function') return;
        try {
            const response = await fetch('/Notification/Unread', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (!response.ok || !(response.headers.get('content-type') || '').includes('application/json')) return;
            const notifications = await response.json();
            const seen = readSeenIds();
            const next = notifications.find(item => item && item.Id != null && !seen.has(String(item.Id)));
            if (!next) return;
            seen.add(String(next.Id));
            writeSeenIds(seen);
            window.bricksAndBooksShowNotification({
                notification: {
                    title: next.Title,
                    body: next.Message,
                    type: next.Type,
                    url: next.Url || '/Notification'
                }
            });
        } catch { /* A temporary network failure should not disrupt the page. */ }
    };
    window.setTimeout(checkUnread, 1200);
    window.setInterval(checkUnread, 20000);
})();
