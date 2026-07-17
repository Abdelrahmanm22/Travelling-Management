// In-app notifications: seeds the bell on load, then receives live pushes over SignalR.
(function () {
    "use strict";

    var badge = document.getElementById("notification-badge");
    var list = document.getElementById("notification-list");
    var empty = document.getElementById("notification-empty");
    if (!badge || !list) return; // bell not on this layout

    var ICONS = { 1: "bx-task", 2: "bx-message-square-dots", 3: "bx-check-double" };
    var COLORS = { 1: "bg-primary", 2: "bg-info", 3: "bg-success" };

    function escapeHtml(value) {
        if (value == null) return "";
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function setBadge(count) {
        if (count > 0) {
            badge.textContent = count > 99 ? "99+" : count;
            badge.classList.remove("d-none");
        } else {
            badge.classList.add("d-none");
        }
    }

    function toggleEmpty() {
        if (!empty) return;
        empty.classList.toggle("d-none", list.children.length > 0);
    }

    function itemHtml(n) {
        var unreadClass = n.isRead ? "" : "bg-light";
        var iconBlock = n.avatarSrc
            ? '<img src="' + escapeHtml(n.avatarSrc) + '" class="rounded-circle avatar-xs me-3" alt="">'
            : '<div class="avatar-xs me-3"><span class="avatar-title ' + escapeHtml(n.colorClass) +
              ' rounded-circle font-size-16"><i class="bx ' + escapeHtml(n.icon) + '"></i></span></div>';

        return '' +
            '<a href="/Notification/Open/' + n.id + '" class="text-reset notification-item d-block ' + unreadClass + '">' +
            '  <div class="d-flex p-3">' +
            '    ' + iconBlock +
            '    <div class="flex-grow-1">' +
            '      <h6 class="mb-1">' + escapeHtml(n.title) + '</h6>' +
            '      <div class="font-size-12 text-muted">' +
            '        <p class="mb-1">' + escapeHtml(n.message) + '</p>' +
            '        <p class="mb-0"><i class="mdi mdi-clock-outline"></i> ' + escapeHtml(n.timeAgo) + '</p>' +
            '      </div>' +
            '    </div>' +
            '  </div>' +
            '</a>';
    }

    function prepend(n) {
        list.insertAdjacentHTML("afterbegin", itemHtml(n));
        toggleEmpty();
    }

    function load() {
        fetch("/Notification/Recent", { headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (data) {
                if (!data) return;
                setBadge(data.unreadCount);
                list.innerHTML = (data.items || []).map(itemHtml).join("");
                toggleEmpty();
            })
            .catch(function () { /* offline / not logged in — ignore */ });
    }

    // Mark all as read
    var markAll = document.getElementById("mark-all-read");
    if (markAll) {
        markAll.addEventListener("click", function () {
            var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
            fetch("/Notification/MarkAllAsRead", {
                method: "POST",
                headers: {
                    "RequestVerificationToken": tokenEl ? tokenEl.value : "",
                    "X-Requested-With": "XMLHttpRequest"
                }
            }).then(function () {
                setBadge(0);
                list.querySelectorAll(".notification-item.bg-light")
                    .forEach(function (el) { el.classList.remove("bg-light"); });
            });
        });
    }

    // Real-time channel
    function connectHub() {
        if (typeof signalR === "undefined") return;
        var connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/notifications")
            .withAutomaticReconnect()
            .build();

        connection.on("ReceiveNotification", function (payload) {
            setBadge(payload.unreadCount);
            prepend({
                id: payload.id,
                title: payload.title,
                message: payload.message,
                url: payload.url,
                isRead: false,
                timeAgo: "just now",
                avatarSrc: null,
                icon: ICONS[payload.type] || "bx-bell",
                colorClass: COLORS[payload.type] || "bg-primary"
            });
            if (window.toastr) {
                toastr.info(payload.message, payload.title, { timeOut: 4000, closeButton: true, progressBar: true });
            }
        });

        connection.start().catch(function () { /* retry handled by withAutomaticReconnect */ });
    }

    load();
    connectHub();
})();
