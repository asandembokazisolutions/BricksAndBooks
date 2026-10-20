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
    window.addEventListener('resize', () => {
        if (window.innerWidth >= 992) setSidebar(false);
    });
})();

// Shared helper used by both the pop-up renderer and the polling fallback below.
const readValue = (object, ...keys) => {
    for (const key of keys) {
        if (object && object[key] !== undefined && object[key] !== null) return object[key];
    }
    return '';
};

// Shared in-app pop-up for both notifications and messages.
(() => {
    const importantTypes = new Set(['paymentfailed', 'rentdue', 'maintenanceupdate', 'message', 'chat']);

    const showNotificationPopup = (payload = {}) => {
        const source = payload.notification || payload.data || payload;
        const title = String(readValue(source, 'title', 'Title') || 'Bricks & Books update');
        const body = String(readValue(source, 'body', 'message', 'Message') || 'You have a new notification.');
        const link = String(readValue(source, 'url', 'Url', 'link', 'Link') || '/Notification');
        const type = String(readValue(source, 'type', 'Type') || '').replace(/\s+/g, '').toLowerCase();
        let popup = document.getElementById('notification-popup');

        if (!popup) {
            popup = document.createElement('aside');
            popup.id = 'notification-popup';
            popup.className = 'notification-popup';
            popup.setAttribute('role', 'status');
            popup.setAttribute('aria-live', 'polite');
            popup.setAttribute('aria-atomic', 'true');
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
            const toastRegion = document.getElementById('notification-toast-region');
            (toastRegion || document.body).appendChild(popup);
            popup.querySelector('.notification-popup-close').addEventListener('click', () => {
                popup.classList.remove('is-visible');
                window.clearTimeout(window.bricksAndBooksNotificationTimer);
            });
        }

        popup.classList.toggle('is-important', importantTypes.has(type));
        popup.querySelector('.notification-popup-title').textContent = title;
        popup.querySelector('.notification-popup-message').textContent = body;
        const popupLink = popup.querySelector('.notification-popup-link');
        popupLink.href = link || '/Notification';
        popupLink.textContent = link && link !== '/Notification' ? 'View related details' : 'Open notifications';
        popup.classList.remove('is-visible');
        window.requestAnimationFrame(() => popup.classList.add('is-visible'));
        window.clearTimeout(window.bricksAndBooksNotificationTimer);
        window.bricksAndBooksNotificationTimer = window.setTimeout(() => popup.classList.remove('is-visible'), 9000);
    };

    window.addEventListener('bricksandbooks:fcm-message', event => showNotificationPopup(event.detail));
    window.addEventListener('bricksandbooks:message', event => showNotificationPopup(event.detail));
    window.bricksAndBooksShowNotification = showNotificationPopup;
})();

// Stored notification fallback: works when Firebase foreground delivery is unavailable.
(() => {
    const seenKey = 'bricksandbooks:seen-notification-ids';
    const readSeenIds = () => {
        try {
            return new Set(JSON.parse(window.sessionStorage.getItem(seenKey) || '[]').map(String));
        } catch {
            return new Set();
        }
    };
    const writeSeenIds = (ids) => {
        try {
            window.sessionStorage.setItem(seenKey, JSON.stringify([...ids].slice(-50)));
        } catch {
            // Storage can be unavailable in private browsing.
        }
    };
    const checkUnread = async () => {
        if (typeof window.bricksAndBooksShowNotification !== 'function') return;
        try {
            const response = await fetch('/Notification/Unread', {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' },
                cache: 'no-store'
            });
            if (!response.ok || !(response.headers.get('content-type') || '').includes('application/json')) return;
            const notifications = await response.json();
            const seen = readSeenIds();
            const next = notifications.find(item => {
                const id = readValue(item, 'id', 'Id');
                return id !== '' && !seen.has(String(id));
            });
            if (!next) return;

            const id = readValue(next, 'id', 'Id');
            seen.add(String(id));
            writeSeenIds(seen);
            window.bricksAndBooksShowNotification({ notification: next });
        } catch {
            // A temporary network failure must not disrupt the page.
        }
    };

    const startPolling = () => {
        checkUnread();
        window.setInterval(checkUnread, 5000);
    };
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startPolling, { once: true });
    } else {
        startPolling();
    }
})();

// Topbar badge counts: shows unread notifications and unread messages, "0" when empty.
(() => {
    const badges = [
        { el: document.getElementById('notification-unread-badge'), url: '/Notification/UnreadCount' },
        { el: document.getElementById('chat-unread-badge'), url: '/Chat/UnreadCount' }
    ].filter(entry => entry.el);

    if (!badges.length) return;

    const setBadgeCount = (el, count) => {
        const safeCount = Number.isFinite(count) && count > 0 ? count : 0;
        el.textContent = safeCount > 99 ? '99+' : String(safeCount);
        el.dataset.count = String(safeCount);
        el.classList.toggle('has-unread', safeCount > 0);
    };

    const refreshBadge = async ({ el, url }) => {
        try {
            const response = await fetch(url, {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' },
                cache: 'no-store'
            });
            if (!response.ok || !(response.headers.get('content-type') || '').includes('application/json')) return;
            const payload = await response.json();
            setBadgeCount(el, Number(readValue(payload, 'count', 'Count')) || 0);
        } catch {
            // A temporary network failure must not disrupt the page.
        }
    };

    const refreshAll = () => badges.forEach(refreshBadge);

    const startPolling = () => {
        refreshAll();
        window.setInterval(refreshAll, 5000);
    };
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startPolling, { once: true });
    } else {
        startPolling();
    }

    // Refresh immediately whenever a new toast comes in, instead of waiting for the next poll tick.
    window.addEventListener('bricksandbooks:fcm-message', refreshAll);
    window.addEventListener('bricksandbooks:message', refreshAll);
})();


// Ask for confirmation on destructive or approval actions marked with data-confirm.
(() => {
    document.addEventListener('submit', event => {
        const submitter = event.submitter;
        const message = submitter?.dataset.confirm || event.target.querySelector('[data-confirm]')?.dataset.confirm;
        if (message && !window.confirm(message)) event.preventDefault();
    });
    document.querySelectorAll('[data-relative-time]').forEach(element => {
        const value = Date.parse(element.dataset.relativeTime || '');
        if (Number.isNaN(value)) return;
        const minutes = Math.max(0, Math.floor((Date.now() - value) / 60000));
        element.textContent = minutes < 1 ? 'Updated just now' : minutes < 60 ? `Updated ${minutes} minute(s) ago` : minutes < 1440 ? `Updated ${Math.floor(minutes / 60)} hour(s) ago` : `Updated ${Math.floor(minutes / 1440)} day(s) ago`;
    });
})();

// Password visibility controls: every password field gets an accessible eye button.
(() => {
    const setupPasswordToggle = (input) => {
        if (!input || input.dataset.passwordToggleReady === 'true') return;
        input.dataset.passwordToggleReady = 'true';
        const existingGroup = input.closest('.input-group');
        let container = existingGroup;
        if (!container) {
            container = document.createElement('div');
            container.className = 'password-field';
            input.parentNode.insertBefore(container, input);
            container.appendChild(input);
        } else {
            container.classList.add('password-input-group');
        }
        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'password-toggle';
        toggle.setAttribute('aria-label', 'Show password');
        toggle.setAttribute('aria-pressed', 'false');
        toggle.innerHTML = '<i class="fa-regular fa-eye" aria-hidden="true"></i>';
        container.appendChild(toggle);
        toggle.addEventListener('click', () => {
            const showing = input.type === 'text';
            input.type = showing ? 'password' : 'text';
            toggle.setAttribute('aria-label', showing ? 'Show password' : 'Hide password');
            toggle.setAttribute('aria-pressed', String(!showing));
            toggle.innerHTML = showing
                ? '<i class="fa-regular fa-eye" aria-hidden="true"></i>'
                : '<i class="fa-regular fa-eye-slash" aria-hidden="true"></i>';
        });
    };
    const initializePasswordToggles = (root = document) => {
        root.querySelectorAll('input[type="password"]').forEach(setupPasswordToggle);
    };
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => initializePasswordToggles(), { once: true });
    } else {
        initializePasswordToggles();
    }
})();
