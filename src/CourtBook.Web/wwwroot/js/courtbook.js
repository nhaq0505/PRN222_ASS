(function () {
    'use strict';

    const BOOKING_STATUS = {
        Held: 1,
        Confirmed: 2,
        CheckedIn: 3,
        Cancelled: 4,
        Expired: 5
    };

    const SLOT_STATE_CLASSES = [
        'cb-slot--available',
        'cb-slot--held-me',
        'cb-slot--held-other',
        'cb-slot--booked-me',
        'cb-slot--booked-other',
        'cb-slot--maintenance',
        'cb-slot--past'
    ];

    function normalizeStatus(status) {
        if (typeof status === 'number') {
            return status;
        }
        if (typeof status === 'string') {
            const trimmed = status.trim();
            if (/^\d+$/.test(trimmed)) {
                return parseInt(trimmed, 10);
            }
            const matchKey = Object.keys(BOOKING_STATUS).find(
                (k) => k.toLowerCase() === trimmed.toLowerCase()
            );
            if (matchKey) {
                return BOOKING_STATUS[matchKey];
            }
        }
        return 0;
    }

    function formatShortPrice(priceVnd) {
        const numeric = Number(priceVnd || 0);
        return `${Math.round(numeric / 1000)}k`;
    }

    function formatVnd(amount) {
        const numeric = Math.round(Number(amount || 0));
        return `${numeric.toLocaleString('vi-VN')}đ`;
    }

    function parseUtcMs(utcIso) {
        if (!utcIso || typeof utcIso !== 'string') {
            return Number.NaN;
        }
        const raw = utcIso.trim();
        if (!raw) {
            return Number.NaN;
        }
        const hasTz = /(?:Z|[+-]\d{2}(?::?\d{2})?)$/i.test(raw);
        const isoLike = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(raw)
            ? raw.replace(' ', 'T')
            : raw;
        const normalized = !hasTz && /^\d{4}-\d{2}-\d{2}T/.test(isoLike)
            ? `${isoLike}Z`
            : isoLike;
        return Date.parse(normalized);
    }

    function formatCountdown(utcIso) {
        if (!utcIso) {
            return '--:--';
        }
        const expiresMs = parseUtcMs(utcIso);
        if (Number.isNaN(expiresMs)) {
            return '--:--';
        }
        const diffSec = Math.max(0, Math.floor((expiresMs - Date.now()) / 1000));
        const mins = String(Math.floor(diffSec / 60)).padStart(2, '0');
        const secs = String(diffSec % 60).padStart(2, '0');
        return `${mins}:${secs}`;
    }

    function isVietnamSlotPast(playDateStr, startHour) {
        if (!playDateStr) {
            return false;
        }
        const vnNow = new Date(Date.now() + 7 * 3600 * 1000);
        const vnToday = vnNow.toISOString().slice(0, 10);
        const vnHour = vnNow.getUTCHours();

        if (playDateStr < vnToday) {
            return true;
        }
        if (playDateStr === vnToday && Number(startHour) <= vnHour) {
            return true;
        }
        return false;
    }

    // =========================================================
    // Toast Notification Manager
    // =========================================================
    function dismissToast(toastEl) {
        if (!toastEl || toastEl.classList.contains('is-leaving')) {
            return;
        }
        toastEl.classList.add('is-leaving');
        window.setTimeout(() => {
            toastEl.remove();
        }, 180);
    }

    function wireToastElement(toastEl, autoDismissMs) {
        const closeBtn = toastEl.querySelector('.cb-toast__close');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => dismissToast(toastEl));
        }
        if (autoDismissMs > 0) {
            window.setTimeout(() => dismissToast(toastEl), autoDismissMs);
        }
    }

    function showToast(message, type = 'info') {
        const container = document.getElementById('cbToastContainer');
        if (!container || !message) {
            return;
        }

        const toast = document.createElement('div');
        const variantClass = type === 'error'
            ? 'cb-toast--error'
            : type === 'success'
                ? 'cb-toast--success'
                : 'cb-toast--info';

        toast.className = `cb-toast ${variantClass}`;
        toast.setAttribute('role', type === 'error' ? 'alert' : 'status');

        const iconSpan = document.createElement('span');
        iconSpan.className = 'cb-toast__icon';
        iconSpan.setAttribute('aria-hidden', 'true');

        if (type === 'success') {
            iconSpan.innerHTML = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="7" fill="#16A34A"/><path d="M5 8.2L7 10.2L11.2 6" stroke="#FFFFFF" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
        } else if (type === 'error') {
            iconSpan.innerHTML = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="7" fill="#DC2626"/><path d="M8 4.8V8.4M8 10.8V11.2" stroke="#FFFFFF" stroke-width="1.6" stroke-linecap="round"/></svg>';
        } else {
            iconSpan.innerHTML = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="7" fill="#2563EB"/><path d="M8 7.2V11.2M8 4.8V5.3" stroke="#FFFFFF" stroke-width="1.6" stroke-linecap="round"/></svg>';
        }

        const msgSpan = document.createElement('span');
        msgSpan.className = 'cb-toast__msg';
        msgSpan.textContent = message;

        const closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'cb-toast__close';
        closeBtn.setAttribute('aria-label', 'Đóng');
        closeBtn.innerHTML = '&times;';

        toast.appendChild(iconSpan);
        toast.appendChild(msgSpan);
        toast.appendChild(closeBtn);
        container.appendChild(toast);

        wireToastElement(toast, type === 'error' ? 5500 : 4200);
    }

    function initExistingToasts() {
        const container = document.getElementById('cbToastContainer');
        if (!container) {
            return;
        }
        container.querySelectorAll('.cb-toast').forEach((el) => {
            const ms = parseInt(el.dataset.autoDismiss || '4500', 10);
            wireToastElement(el, Number.isNaN(ms) ? 4500 : ms);
        });
    }

    window.CourtBook = {
        toast: showToast
    };

    // =========================================================
    // Schedule Matrix & Real-time SignalR Client
    // =========================================================
    const scheduleApp = document.getElementById('scheduleApp');
    const venueId = Number(scheduleApp?.dataset.venueId || 0);

    function getScheduleContext() {
        if (!scheduleApp) {
            return null;
        }
        const rawUserId = scheduleApp.dataset.currentUserId;
        const parsedUserId = rawUserId && /^\d+$/.test(rawUserId) ? parseInt(rawUserId, 10) : null;

        return {
            venueId: venueId,
            playDate: scheduleApp.dataset.playDate || '',
            currentUserId: parsedUserId,
            isAuthenticated: scheduleApp.dataset.isAuthenticated === 'true',
            toggleUrl: scheduleApp.dataset.toggleUrl || '/?handler=ToggleHold',
            myHoldsUrl: scheduleApp.dataset.myholdsUrl || '/?handler=MyHolds'
        };
    }

    function isMatchingVenue(payload) {
        if (!payload) {
            return false;
        }
        if (venueId <= 0) {
            return false;
        }
        return !payload.venueId || Number(payload.venueId) === venueId;
    }

    function setSlotState(btn, config) {
        SLOT_STATE_CLASSES.forEach((cls) => btn.classList.remove(cls));
        btn.classList.add(config.className);
        btn.dataset.state = config.state;
        btn.dataset.expiresUtc = config.expiresUtc || '';
        delete btn.dataset.expiredHandled;
        btn.disabled = Boolean(config.disabled);

        const primaryEl = btn.querySelector('.cb-slot__primary');
        const subEl = btn.querySelector('[data-countdown]') || btn.querySelector('.cb-slot__sub');
        if (primaryEl) {
            primaryEl.textContent = config.primaryText;
        }
        if (subEl) {
            subEl.textContent = config.subText || '';
        }
    }

    function applySlotUpdate(update) {
        const ctx = getScheduleContext();
        if (!ctx || !update || update.playDate !== ctx.playDate || !isMatchingVenue(update)) {
            return;
        }

        const selector = `[data-slot-cell][data-court-id="${update.courtId}"][data-start-hour="${update.startHour}"]`;
        const btn = document.querySelector(selector);
        if (!btn) {
            return;
        }

        const status = normalizeStatus(update.status);
        const updateUserId = update.userId !== null && update.userId !== undefined
            ? Number(update.userId)
            : null;
        const isMine = ctx.currentUserId !== null && updateUserId !== null && updateUserId === ctx.currentUserId;
        const isPeak = Number(update.startHour) >= 17 && Number(update.startHour) < 21;
        const isPast = isVietnamSlotPast(update.playDate, update.startHour);

        if (status === BOOKING_STATUS.Held) {
            const expiresUtc = update.holdExpiresAtUtc || '';
            const timerLabel = expiresUtc ? formatCountdown(expiresUtc) : '02:00';
            if (isMine) {
                setSlotState(btn, {
                    state: 'HeldByMe',
                    className: 'cb-slot--held-me',
                    primaryText: 'Đang chọn',
                    subText: timerLabel,
                    expiresUtc: expiresUtc,
                    disabled: false
                });
            } else {
                setSlotState(btn, {
                    state: 'HeldByOther',
                    className: 'cb-slot--held-other',
                    primaryText: 'Giữ chỗ',
                    subText: timerLabel,
                    expiresUtc: expiresUtc,
                    disabled: true
                });
            }
            return;
        }

        if (status === BOOKING_STATUS.Confirmed || status === BOOKING_STATUS.CheckedIn) {
            setSlotState(btn, {
                state: isMine ? 'BookedByMe' : 'BookedByOther',
                className: isMine ? 'cb-slot--booked-me' : 'cb-slot--booked-other',
                primaryText: isMine ? 'Của bạn' : 'Đã đặt',
                subText: '',
                expiresUtc: '',
                disabled: true
            });
            return;
        }

        // Cancelled / Expired -> revert to Maintenance, Past, or Available
        if (update.isCourtActive === false) {
            setSlotState(btn, {
                state: 'Maintenance',
                className: 'cb-slot--maintenance',
                primaryText: 'Bảo trì',
                subText: '',
                expiresUtc: '',
                disabled: true
            });
            return;
        }

        if (isPast) {
            setSlotState(btn, {
                state: 'Past',
                className: 'cb-slot--past',
                primaryText: '—',
                subText: '',
                expiresUtc: '',
                disabled: true
            });
            return;
        }

        const priceVnd = Number(update.priceVnd || btn.dataset.price || 0);
        setSlotState(btn, {
            state: 'Available',
            className: 'cb-slot--available',
            primaryText: formatShortPrice(priceVnd),
            subText: isPeak ? 'Giờ vàng' : '',
            expiresUtc: '',
            disabled: false
        });
    }

    function renderHoldBar(myHolds) {
        const bar = document.getElementById('holdStickyBar');
        if (!bar) {
            return;
        }

        const count = myHolds && typeof myHolds.count === 'number' ? myHolds.count : 0;
        const countEl = document.getElementById('holdCountBadge');
        const summaryEl = document.getElementById('holdSummaryText');
        const timerEl = document.getElementById('holdEarliestTimer');
        const totalEl = document.getElementById('holdTotalPrice');

        if (count <= 0) {
            bar.classList.remove('is-visible');
            if (countEl) countEl.textContent = '0';
            if (summaryEl) summaryEl.textContent = '';
            if (timerEl) {
                timerEl.dataset.expiresUtc = '';
                delete timerEl.dataset.expiredHandled;
                timerEl.textContent = '--:--';
            }
            if (totalEl) totalEl.textContent = '0đ';
            return;
        }

        bar.classList.add('is-visible');
        if (countEl) countEl.textContent = String(count);
        if (summaryEl) summaryEl.textContent = myHolds.summaryText || '';
        if (timerEl) {
            const earliest = myHolds.earliestExpiresAtUtc || '';
            timerEl.dataset.expiresUtc = earliest;
            delete timerEl.dataset.expiredHandled;
            timerEl.textContent = formatCountdown(earliest);
        }
        if (totalEl) {
            totalEl.textContent = formatVnd(myHolds.totalPriceVnd);
        }
    }

    let refreshHoldsInFlight = false;
    async function refreshMyHolds() {
        const ctx = getScheduleContext();
        if (!ctx || !ctx.isAuthenticated || refreshHoldsInFlight) {
            return;
        }
        refreshHoldsInFlight = true;
        try {
            const response = await fetch(ctx.myHoldsUrl, {
                method: 'GET',
                headers: { 'Accept': 'application/json' }
            });
            if (response.ok) {
                const summary = await response.json();
                renderHoldBar(summary);
            }
        } catch {
            // Silent background refresh fallback
        } finally {
            refreshHoldsInFlight = false;
        }
    }

    function getAntiForgeryToken() {
        const tokenInput = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')
            || document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    function redirectToLogin(loginUrl) {
        const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
        window.location.href = `${loginUrl || '/Account/Login'}?ReturnUrl=${returnUrl}`;
    }

    function initScheduleClickHandlers() {
        if (!scheduleApp) {
            return;
        }

        scheduleApp.addEventListener('click', async (event) => {
            const btn = event.target.closest('[data-slot-cell]');
            if (!btn || btn.disabled || btn.classList.contains('is-pending')) {
                return;
            }

            const state = btn.dataset.state || '';
            if (state !== 'Available' && state !== 'HeldByMe') {
                return;
            }

            const ctx = getScheduleContext();
            if (!ctx || !ctx.isAuthenticated) {
                redirectToLogin('/Account/Login');
                return;
            }

            const courtId = parseInt(btn.dataset.courtId || '0', 10);
            const startHour = parseInt(btn.dataset.startHour || '-1', 10);
            if (!courtId || startHour < 0) {
                return;
            }

            btn.classList.add('is-pending');
            try {
                const response = await fetch(ctx.toggleUrl, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Accept': 'application/json',
                        'RequestVerificationToken': getAntiForgeryToken()
                    },
                    body: JSON.stringify({
                        courtId: courtId,
                        playDate: ctx.playDate,
                        startHour: startHour
                    })
                });

                if (response.status === 401) {
                    redirectToLogin('/Account/Login');
                    return;
                }

                const payload = await response.json().catch(() => null);
                if (payload && payload.requiresLogin) {
                    redirectToLogin(payload.loginUrl);
                    return;
                }

                if (!response.ok || !payload || !payload.ok) {
                    showToast((payload && payload.error) || 'Không thể giữ chỗ khung giờ này.', 'error');
                    return;
                }

                if (payload.data) {
                    if (payload.data.updatedSlot) {
                        applySlotUpdate(payload.data.updatedSlot);
                    }
                    if (payload.data.myHolds) {
                        renderHoldBar(payload.data.myHolds);
                    }
                }
            } catch {
                showToast('Kết nối tạm gián đoạn. Vui lòng thử lại.', 'error');
            } finally {
                btn.classList.remove('is-pending');
            }
        });
    }

    function tickCountdowns() {
        const ctx = getScheduleContext();
        document.querySelectorAll('[data-expires-utc]').forEach((el) => {
            const utcIso = el.dataset.expiresUtc;
            if (!utcIso) {
                return;
            }

            const expiresMs = parseUtcMs(utcIso);
            if (Number.isNaN(expiresMs)) {
                return;
            }

            const remainingSec = Math.max(0, Math.floor((expiresMs - Date.now()) / 1000));
            const formatted = formatCountdown(utcIso);

            if (el.hasAttribute('data-slot-cell')) {
                const subEl = el.querySelector('[data-countdown]') || el.querySelector('.cb-slot__sub');
                if (subEl) {
                    subEl.textContent = formatted;
                }

                if (remainingSec <= 0 && !el.dataset.expiredHandled) {
                    el.dataset.expiredHandled = '1';
                    const playDate = ctx ? ctx.playDate : '';
                    const startHour = Number(el.dataset.startHour || 0);
                    const wasMine = el.dataset.state === 'HeldByMe';

                    window.setTimeout(() => {
                        if (el.dataset.state !== 'HeldByMe' && el.dataset.state !== 'HeldByOther') {
                            return;
                        }
                        const currentExpiryMs = parseUtcMs(el.dataset.expiresUtc || '');
                        const stillExpired = !Number.isNaN(currentExpiryMs) && currentExpiryMs <= Date.now();
                        if (!stillExpired) {
                            return;
                        }
                        if (isVietnamSlotPast(playDate, startHour)) {
                            setSlotState(el, {
                                state: 'Past',
                                className: 'cb-slot--past',
                                primaryText: '—',
                                subText: '',
                                expiresUtc: '',
                                disabled: true
                            });
                        } else {
                            const isPeak = startHour >= 17 && startHour < 21;
                            setSlotState(el, {
                                state: 'Available',
                                className: 'cb-slot--available',
                                primaryText: formatShortPrice(el.dataset.price),
                                subText: isPeak ? 'Giờ vàng' : '',
                                expiresUtc: '',
                                disabled: false
                            });
                        }
                        if (wasMine) {
                            refreshMyHolds();
                        }
                    }, 1800);
                }
            } else {
                el.textContent = formatted;
                if (remainingSec <= 0 && !el.dataset.expiredHandled) {
                    el.dataset.expiredHandled = '1';
                    window.setTimeout(() => {
                        refreshMyHolds();
                    }, 1500);
                }
            }
        });
    }

    function updateSignalRStatus(statusClass, label) {
        const pill = document.getElementById('signalrStatus');
        if (!pill) {
            return;
        }
        pill.classList.remove('is-online', 'is-connecting', 'is-reconnecting', 'is-offline');
        pill.classList.add(statusClass);
        const textEl = pill.querySelector('.cb-live-pill__text');
        if (textEl) {
            textEl.textContent = label;
        }
    }

    function initSignalR() {
        if (!window.signalR) {
            updateSignalRStatus('is-offline', 'Ngoại tuyến');
            return;
        }

        const connection = new window.signalR.HubConnectionBuilder()
            .withUrl('/hubs/court')
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .build();

        function joinCurrentVenueGroup() {
            if (venueId > 0) {
                return connection.invoke('JoinVenueGroup', venueId).catch(() => {});
            }
            return Promise.resolve();
        }

        connection.on('SlotUpdated', (update) => {
            if (!isMatchingVenue(update)) {
                return;
            }
            const ctx = getScheduleContext();
            const existingBtn = update && ctx && update.playDate === ctx.playDate
                ? document.querySelector(`[data-slot-cell][data-court-id="${update.courtId}"][data-start-hour="${update.startHour}"]`)
                : null;
            const wasHeldByMe = existingBtn && existingBtn.dataset.state === 'HeldByMe';

            applySlotUpdate(update);

            if (ctx && ctx.isAuthenticated) {
                const updateUserId = update && update.userId !== null && update.userId !== undefined
                    ? Number(update.userId)
                    : null;
                const st = normalizeStatus(update && update.status);
                if (wasHeldByMe || updateUserId === ctx.currentUserId || st === BOOKING_STATUS.Expired) {
                    refreshMyHolds();
                }
            }
        });

        connection.on('SlotsUpdated', (updates) => {
            if (!Array.isArray(updates)) {
                return;
            }
            const ctx = getScheduleContext();
            let shouldRefreshHolds = false;
            updates.forEach((u) => {
                if (!isMatchingVenue(u)) {
                    return;
                }
                const existingBtn = u && ctx && u.playDate === ctx.playDate
                    ? document.querySelector(`[data-slot-cell][data-court-id="${u.courtId}"][data-start-hour="${u.startHour}"]`)
                    : null;
                const wasHeldByMe = existingBtn && existingBtn.dataset.state === 'HeldByMe';

                applySlotUpdate(u);

                if (ctx && ctx.isAuthenticated) {
                    const uid = u && u.userId !== null && u.userId !== undefined ? Number(u.userId) : null;
                    const st = normalizeStatus(u && u.status);
                    if (wasHeldByMe || uid === ctx.currentUserId || st === BOOKING_STATUS.Expired) {
                        shouldRefreshHolds = true;
                    }
                }
            });
            if (shouldRefreshHolds) {
                refreshMyHolds();
            }
        });

        connection.on('CourtStatusChanged', (payload) => {
            if (scheduleApp && (!payload || !payload.venueId || Number(payload.venueId) === venueId)) {
                showToast('Trạng thái sân vừa được cập nhật.', 'info');
                window.setTimeout(() => {
                    window.location.reload();
                }, 400);
            }
        });

        connection.onreconnecting(() => {
            updateSignalRStatus('is-reconnecting', 'Đang kết nối lại...');
        });

        connection.onreconnected(() => {
            updateSignalRStatus('is-online', 'Trực tuyến');
            joinCurrentVenueGroup();
            refreshMyHolds();
        });

        connection.onclose(() => {
            updateSignalRStatus('is-offline', 'Ngoại tuyến');
        });

        connection.start()
            .then(() => {
                updateSignalRStatus('is-online', 'Trực tuyến');
                joinCurrentVenueGroup();
            })
            .catch(() => {
                updateSignalRStatus('is-offline', 'Ngoại tuyến');
            });
    }

    document.addEventListener('DOMContentLoaded', () => {
        initExistingToasts();
        initScheduleClickHandlers();
        tickCountdowns();
        window.setInterval(tickCountdowns, 1000);
        initSignalR();
    });
})();
