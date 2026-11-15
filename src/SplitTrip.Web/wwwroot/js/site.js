(function () {
    "use strict";

    let hubConnection = null;
    let hubStartPromise = null;

    function showToast(title, message, actionUrl) {
        const container = document.getElementById("stToastContainer");
        if (!container || typeof bootstrap === "undefined") {
            return;
        }

        const toastEl = document.createElement("div");
        toastEl.className = "toast align-items-center border-0 shadow-lg mb-2";
        toastEl.setAttribute("role", "alert");
        toastEl.setAttribute("aria-live", "assertive");
        toastEl.setAttribute("aria-atomic", "true");

        const headerDiv = document.createElement("div");
        headerDiv.className = "toast-header bg-white border-bottom";
        const strong = document.createElement("strong");
        strong.className = "me-auto text-dark small";
        strong.textContent = title || "SplitTrip Live";
        const closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.className = "btn-close";
        closeBtn.setAttribute("data-bs-dismiss", "toast");
        closeBtn.setAttribute("aria-label", "Đóng");
        headerDiv.appendChild(strong);
        headerDiv.appendChild(closeBtn);

        const bodyDiv = document.createElement("div");
        bodyDiv.className = "toast-body small text-secondary bg-white rounded-bottom";
        bodyDiv.textContent = message || "";

        if (actionUrl) {
            const linkWrap = document.createElement("div");
            linkWrap.className = "mt-2";
            const link = document.createElement("a");
            link.href = actionUrl;
            link.className = "st-link-teal small text-decoration-none";
            link.textContent = "Xem chi tiết →";
            linkWrap.appendChild(link);
            bodyDiv.appendChild(linkWrap);
        }

        toastEl.appendChild(headerDiv);
        toastEl.appendChild(bodyDiv);
        container.appendChild(toastEl);

        const toast = new bootstrap.Toast(toastEl, { delay: 5000 });
        toastEl.addEventListener("hidden.bs.toast", function () {
            toastEl.remove();
        });
        toast.show();
    }

    function updateNotificationBadge(count) {
        const badge = document.getElementById("stNotifBadge");
        if (!badge) return;
        const safeCount = Number(count) || 0;
        badge.textContent = String(safeCount);
        badge.classList.toggle("d-none", safeCount <= 0);
    }

    function prependNotificationItem(payload) {
        const list = document.getElementById("stNotifList");
        if (!list) return;

        const empty = document.getElementById("stNotifEmpty");
        if (empty) empty.remove();

        const link = document.createElement("a");
        link.href = payload.actionUrl || "#";
        link.className = "st-notif-item st-notif-unread";
        if (payload.id) {
            link.setAttribute("data-notif-id", payload.id);
        }

        const titleEl = document.createElement("div");
        titleEl.className = "fw-semibold small text-dark";
        titleEl.textContent = payload.title || "Thông báo mới";

        const msgEl = document.createElement("div");
        msgEl.className = "small text-secondary";
        msgEl.textContent = payload.message || "";

        const timeEl = document.createElement("div");
        timeEl.className = "st-notif-time";
        timeEl.textContent = "Vừa xong";

        link.appendChild(titleEl);
        link.appendChild(msgEl);
        link.appendChild(timeEl);
        list.prepend(link);

        const badge = document.getElementById("stNotifBadge");
        if (badge) {
            const current = badge.classList.contains("d-none") ? 0 : (parseInt(badge.textContent || "0", 10) || 0);
            updateNotificationBadge(current + 1);
        }
    }

    function ensureHubStarted() {
        if (typeof signalR === "undefined") {
            return Promise.reject(new Error("SignalR client library not loaded."));
        }

        if (!hubConnection) {
            hubConnection = new signalR.HubConnectionBuilder()
                .withUrl("/hubs/trip")
                .withAutomaticReconnect()
                .build();

            hubConnection.on("ReceiveNotification", function (payload) {
                if (!payload) return;
                prependNotificationItem(payload);
                showToast(payload.title || "Thông báo mới", payload.message || "", payload.actionUrl);
            });

            hubConnection.on("NotificationBadgeUpdated", function (unreadCount) {
                updateNotificationBadge(unreadCount);
            });

            window.splitTripHub = hubConnection;
        }

        if (hubConnection.state === signalR.HubConnectionState.Connected) {
            return Promise.resolve(hubConnection);
        }

        if (!hubStartPromise) {
            hubStartPromise = hubConnection.start()
                .then(function () {
                    return hubConnection;
                })
                .catch(function (err) {
                    hubStartPromise = null;
                    throw err;
                });
        }

        return hubStartPromise;
    }

    window.SplitTrip = {
        getHub: ensureHubStarted,
        showToast: showToast,
        joinTripRoom: async function (tripId) {
            const conn = await ensureHubStarted();
            await conn.invoke("JoinTripRoom", String(tripId));
            return conn;
        }
    };

    document.addEventListener("DOMContentLoaded", function () {
        const isAuthenticated = document.body && document.body.getAttribute("data-authenticated") === "true";
        if (isAuthenticated && typeof signalR !== "undefined") {
            ensureHubStarted().catch(function () { /* ignore background connect transient error */ });
        }

        const markAllBtn = document.getElementById("stMarkAllReadBtn");
        if (markAllBtn) {
            markAllBtn.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                ensureHubStarted()
                    .then(function (conn) {
                        return conn.invoke("MarkAllNotificationsRead");
                    })
                    .then(function () {
                        updateNotificationBadge(0);
                        document.querySelectorAll(".st-notif-unread").forEach(function (el) {
                            el.classList.remove("st-notif-unread");
                        });
                    })
                    .catch(function () { });
            });
        }

        document.addEventListener("click", function (e) {
            const notifLink = e.target.closest && e.target.closest("[data-notif-id]");
            if (notifLink && notifLink.classList.contains("st-notif-unread")) {
                const notifId = notifLink.getAttribute("data-notif-id");
                if (notifId) {
                    ensureHubStarted()
                        .then(function (conn) {
                            return conn.invoke("MarkNotificationRead", notifId);
                        })
                        .catch(function () { });
                }
            }
        });
    });
})();
