(function () {
    "use strict";

    const root = document.getElementById("stItineraryWorkspace");
    if (!root) {
        return;
    }

    const tripId = root.getAttribute("data-trip-id") || "";
    const currentUserId = (root.getAttribute("data-user-id") || "").toLowerCase();
    let canEdit = root.getAttribute("data-can-edit") === "true";
    let activeFilterDayId = "all";
    let scheduleState = null;
    let latestAiPreview = null;
    let draggedItemId = null;

    const initialJsonEl = document.getElementById("stInitialScheduleJson");
    if (initialJsonEl && initialJsonEl.textContent) {
        try {
            scheduleState = JSON.parse(initialJsonEl.textContent);
            if (typeof scheduleState.canEdit === "boolean") {
                canEdit = scheduleState.canEdit;
            }
        } catch (err) {
            scheduleState = null;
        }
    }

    const timelineContainer = document.getElementById("stDaysTimelineContainer");
    const dayFilterContainer = document.getElementById("stDayFilterPills");

    const stopModalEl = document.getElementById("stStopModal");
    const dayModalEl = document.getElementById("stDayModal");
    const aiModalEl = document.getElementById("stAiPlannerModal");

    const stopModal = stopModalEl && typeof bootstrap !== "undefined" ? new bootstrap.Modal(stopModalEl) : null;
    const dayModal = dayModalEl && typeof bootstrap !== "undefined" ? new bootstrap.Modal(dayModalEl) : null;
    const aiModal = aiModalEl && typeof bootstrap !== "undefined" ? new bootstrap.Modal(aiModalEl) : null;

    function getAntiForgeryToken() {
        const input = root.querySelector('input[name="__RequestVerificationToken"]')
            || document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    }

    function formatVnd(amount) {
        const num = Math.round(Number(amount) || 0);
        return num.toLocaleString("vi-VN") + "đ";
    }

    function formatDateVi(isoDate) {
        if (!isoDate) return "";
        const parts = String(isoDate).split("-");
        if (parts.length === 3) {
            return parts[2] + "/" + parts[1] + "/" + parts[0];
        }
        return isoDate;
    }

    function formatShortDateVi(isoDate) {
        if (!isoDate) return "";
        const parts = String(isoDate).split("-");
        if (parts.length === 3) {
            return parts[2] + "/" + parts[1];
        }
        return isoDate;
    }

    function escapeHtml(value) {
        const div = document.createElement("div");
        div.textContent = value == null ? "" : String(value);
        return div.innerHTML;
    }

    async function postJsonHandler(handlerName, bodyObj) {
        const url = "/Trips/Itinerary?tripId=" + encodeURIComponent(tripId) + "&handler=" + encodeURIComponent(handlerName);
        const res = await fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": getAntiForgeryToken()
            },
            body: JSON.stringify(bodyObj || {})
        });

        const data = await res.json().catch(function () { return {}; });
        if (!res.ok || data.ok === false) {
            throw new Error(data.error || "Không thể thực hiện thao tác lúc này.");
        }
        return data;
    }

    async function fetchScheduleLive(toastTitle, toastMessage) {
        try {
            const url = "/Trips/Itinerary?tripId=" + encodeURIComponent(tripId) + "&handler=ScheduleJson";
            const res = await fetch(url, { headers: { "Accept": "application/json" } });
            if (!res.ok) {
                if (res.status === 403) {
                    window.location.href = "/Index";
                }
                return;
            }
            const freshSchedule = await res.json();
            applyScheduleState(freshSchedule);
            if (toastMessage && window.SplitTrip) {
                window.SplitTrip.showToast(toastTitle || "Lịch trình trực tiếp", toastMessage);
            }
        } catch (e) {
            // Ignore transient network hiccups
        }
    }

    function findItemById(itemId) {
        if (!scheduleState || !Array.isArray(scheduleState.days)) return null;
        for (let d = 0; d < scheduleState.days.length; d++) {
            const items = scheduleState.days[d].items || [];
            for (let i = 0; i < items.length; i++) {
                if (String(items[i].id).toLowerCase() === String(itemId).toLowerCase()) {
                    return items[i];
                }
            }
        }
        return null;
    }

    function findDayById(dayId) {
        if (!scheduleState || !Array.isArray(scheduleState.days)) return null;
        return scheduleState.days.find(function (d) {
            return String(d.id).toLowerCase() === String(dayId).toLowerCase();
        }) || null;
    }

    function applyScheduleState(newSchedule) {
        if (!newSchedule || !Array.isArray(newSchedule.days)) {
            return;
        }
        scheduleState = newSchedule;
        canEdit = Boolean(newSchedule.canEdit);
        root.setAttribute("data-can-edit", canEdit ? "true" : "false");

        updateKpisAndDaySelect();
        renderDayFilterPills();
        renderDaysTimeline();
    }

    function updateKpisAndDaySelect() {
        const stopsEl = document.getElementById("stKpiTotalStops");
        if (stopsEl) stopsEl.textContent = (scheduleState.totalItemsCount || 0) + " điểm";

        const costEl = document.getElementById("stKpiTotalCost");
        if (costEl) costEl.textContent = formatVnd(scheduleState.totalEstimatedCost || 0);

        const aiEl = document.getElementById("stKpiAiStops");
        if (aiEl) aiEl.textContent = (scheduleState.aiSuggestedItemsCount || 0) + " gợi ý";

        const budgetEl = document.getElementById("stKpiGroupBudget");
        if (budgetEl) {
            const groupBudget = (Number(scheduleState.estimatedBudgetPerPerson) || 0) * (Number(scheduleState.activeMemberCount) || 1);
            budgetEl.textContent = groupBudget > 0 ? formatVnd(groupBudget) : "Chưa đặt";
        }

        const selectEl = document.getElementById("stStopDaySelect");
        if (selectEl) {
            const prevVal = selectEl.value;
            selectEl.innerHTML = "";
            scheduleState.days.forEach(function (day) {
                const opt = document.createElement("option");
                opt.value = day.id;
                opt.textContent = "Ngày " + day.dayNumber + " — " + formatDateVi(day.date) + " (" + (day.title || ("Ngày " + day.dayNumber)) + ")";
                selectEl.appendChild(opt);
            });
            if (prevVal && findDayById(prevVal)) {
                selectEl.value = prevVal;
            }
        }
    }

    function renderDayFilterPills() {
        if (!dayFilterContainer || !scheduleState) return;
        dayFilterContainer.innerHTML = "";

        const allBtn = document.createElement("button");
        allBtn.type = "button";
        allBtn.className = "st-day-pill" + (activeFilterDayId === "all" ? " active" : "");
        allBtn.setAttribute("data-filter-day", "all");
        allBtn.textContent = "Tất cả (" + scheduleState.days.length + " ngày)";
        dayFilterContainer.appendChild(allBtn);

        scheduleState.days.forEach(function (day) {
            const btn = document.createElement("button");
            btn.type = "button";
            const isAct = String(activeFilterDayId).toLowerCase() === String(day.id).toLowerCase();
            btn.className = "st-day-pill" + (isAct ? " active" : "");
            btn.setAttribute("data-filter-day", day.id);
            btn.textContent = "Ngày " + day.dayNumber + " (" + formatShortDateVi(day.date) + ")";
            dayFilterContainer.appendChild(btn);
        });
    }

    function normalizeVoteNum(voteVal) {
        if (voteVal === 1 || voteVal === "Upvote") return 1;
        if (voteVal === -1 || voteVal === "Downvote") return -1;
        return 0;
    }

    function renderDaysTimeline() {
        if (!timelineContainer || !scheduleState) return;
        timelineContainer.innerHTML = "";

        scheduleState.days.forEach(function (day) {
            if (activeFilterDayId !== "all" && String(day.id).toLowerCase() !== String(activeFilterDayId).toLowerCase()) {
                return;
            }

            const section = document.createElement("section");
            section.className = "st-day-section";
            section.setAttribute("data-day-section", day.id);

            const dayTitle = day.title || ("Ngày " + day.dayNumber);
            const notesHtml = day.notes
                ? '<div class="small text-muted mt-1">' + escapeHtml(day.notes) + '</div>'
                : "";

            const headerActionsHtml = canEdit
                ? '<div class="d-flex align-items-center gap-2">' +
                    '<button type="button" class="btn btn-sm st-btn-ghost py-1 px-2 small js-edit-day-btn" data-day-id="' + escapeHtml(day.id) + '">✎ Sửa ngày</button>' +
                    '<button type="button" class="btn btn-sm btn-outline-teal st-link-teal border py-1 px-2 small js-add-stop-day-btn" data-day-id="' + escapeHtml(day.id) + '">+ Điểm dừng</button>' +
                  '</div>'
                : "";

            const header = document.createElement("div");
            header.className = "st-day-header d-flex flex-column flex-sm-row justify-content-between align-items-sm-center gap-2";
            header.innerHTML =
                '<div class="d-flex align-items-start align-items-sm-center gap-3">' +
                    '<span class="st-day-badge">D' + day.dayNumber + '</span>' +
                    '<div>' +
                        '<div class="d-flex flex-wrap align-items-center gap-2">' +
                            '<h2 class="h6 fw-bold mb-0">' + escapeHtml(dayTitle) + '</h2>' +
                            '<span class="badge bg-light text-secondary border fw-normal">' + escapeHtml(formatDateVi(day.date)) + '</span>' +
                            '<span class="small text-muted">&middot; Dự kiến: <strong>' + escapeHtml(formatVnd(day.dayEstimatedCost || 0)) + '</strong></span>' +
                        '</div>' +
                        notesHtml +
                    '</div>' +
                '</div>' +
                headerActionsHtml;

            const dropzone = document.createElement("div");
            dropzone.className = "st-day-dropzone";
            dropzone.setAttribute("data-day-id", day.id);

            const items = day.items || [];
            if (items.length === 0) {
                dropzone.innerHTML =
                    '<div class="st-day-empty">Chưa có điểm dừng nào trong Ngày ' + day.dayNumber + '. Kéo thả điểm dừng vào đây hoặc bấm <strong>+ Điểm dừng</strong>.</div>';
            } else {
                items.forEach(function (item, idx) {
                    dropzone.appendChild(buildStopCardElement(day, item, idx, items.length));
                });
            }

            section.appendChild(header);
            section.appendChild(dropzone);
            timelineContainer.appendChild(section);
        });

        bindDropzoneDragEvents();
    }

    function buildStopCardElement(day, item, idx, totalInDay) {
        const article = document.createElement("article");
        article.id = "item-" + item.id;
        article.className = "st-stop-card";
        article.setAttribute("draggable", canEdit ? "true" : "false");
        article.setAttribute("data-item-id", item.id);
        article.setAttribute("data-day-id", day.id);

        const timePillHtml = item.startTimeFormatted
            ? '<span class="st-time-pill">🕒 ' + escapeHtml(item.startTimeFormatted + (item.endTimeFormatted ? " – " + item.endTimeFormatted : "")) + '</span>'
            : "";

        const aiBadgeHtml = item.isAiSuggested
            ? '<span class="st-ai-badge">✨ AI gợi ý</span>'
            : "";

        const reminderBadgeHtml = item.reminderSentAtUtc
            ? '<span class="badge rounded-pill bg-success-subtle text-success border border-success-subtle">🔔 Đã nhắc khởi hành</span>'
            : "";

        let locationHtml = "";
        if (item.locationName || item.address) {
            locationHtml = '<div class="small text-secondary mb-1">📍 <strong>' + escapeHtml(item.locationName || item.address) + '</strong>';
            if (item.locationName && item.address) {
                locationHtml += ' <span class="text-muted">&mdash; ' + escapeHtml(item.address) + '</span>';
            }
            locationHtml += '</div>';
        }

        const notesHtml = item.notes
            ? '<p class="small text-muted mb-2">' + escapeHtml(item.notes) + '</p>'
            : "";

        const costText = (Number(item.estimatedCost) || 0) > 0 ? formatVnd(item.estimatedCost) : "Miễn phí";
        const userVote = normalizeVoteNum(item.currentUserVote);
        const net = Number(item.netScore) || 0;
        const netClass = net > 0 ? "st-vote-net-pos" : (net < 0 ? "st-vote-net-neg" : "st-vote-net-zero");
        const netText = net > 0 ? ("+" + net) : String(net);

        const moveButtonsHtml = canEdit
            ? '<div class="d-flex flex-column gap-1 mt-1">' +
                '<button type="button" class="st-move-mini-btn js-move-step-btn" data-item-id="' + escapeHtml(item.id) + '" data-dir="-1" ' + (idx === 0 ? "disabled" : "") + ' title="Chuyển lên">▲</button>' +
                '<button type="button" class="st-move-mini-btn js-move-step-btn" data-item-id="' + escapeHtml(item.id) + '" data-dir="1" ' + (idx === totalInDay - 1 ? "disabled" : "") + ' title="Chuyển xuống">▼</button>' +
              '</div>'
            : "";

        const editBtnsHtml = canEdit
            ? '<div class="d-flex align-items-center gap-1">' +
                '<button type="button" class="btn btn-sm btn-light border py-0 px-2 small js-edit-stop-btn" data-item-id="' + escapeHtml(item.id) + '">Sửa</button>' +
                '<button type="button" class="btn btn-sm btn-outline-danger py-0 px-2 small js-delete-stop-btn" data-item-id="' + escapeHtml(item.id) + '">Xóa</button>' +
              '</div>'
            : "";

        article.innerHTML =
            '<div class="st-drag-rail">' +
                (canEdit ? '<span class="st-drag-handle" title="Kéo thả để đổi thứ tự hoặc chuyển ngày">⋮⋮</span>' : '') +
                '<span class="st-order-num">' + (idx + 1) + '</span>' +
                moveButtonsHtml +
            '</div>' +
            '<div class="flex-grow-1">' +
                '<div class="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-1">' +
                    '<div class="d-flex flex-wrap align-items-center gap-2">' +
                        timePillHtml +
                        '<h3 class="h6 fw-bold mb-0 text-dark">' + escapeHtml(item.title) + '</h3>' +
                        aiBadgeHtml +
                        reminderBadgeHtml +
                    '</div>' +
                    '<span class="st-cost-chip">' + escapeHtml(costText) + '</span>' +
                '</div>' +
                locationHtml +
                notesHtml +
                '<div class="d-flex flex-wrap align-items-center justify-content-between gap-2 mt-2 pt-2 border-top border-light">' +
                    '<div class="d-flex align-items-center gap-2">' +
                        '<div class="st-vote-group" data-vote-box="' + escapeHtml(item.id) + '">' +
                            '<button type="button" class="st-vote-btn js-vote-btn ' + (userVote === 1 ? "st-vote-up-active" : "") + '" data-item-id="' + escapeHtml(item.id) + '" data-vote="1" ' + (canEdit ? "" : "disabled") + ' title="Tán thành điểm dừng này">' +
                                '👍 <span class="js-up-count">' + (Number(item.upvotes) || 0) + '</span>' +
                            '</button>' +
                            '<span class="st-vote-net ' + netClass + ' js-net-score">' + netText + '</span>' +
                            '<button type="button" class="st-vote-btn js-vote-btn ' + (userVote === -1 ? "st-vote-down-active" : "") + '" data-item-id="' + escapeHtml(item.id) + '" data-vote="-1" ' + (canEdit ? "" : "disabled") + ' title="Không tán thành điểm dừng này">' +
                                '👎 <span class="js-down-count">' + (Number(item.downvotes) || 0) + '</span>' +
                            '</button>' +
                        '</div>' +
                        '<span class="text-muted" style="font-size: 0.76rem;">Thêm bởi ' + escapeHtml(item.createdByName || "Thành viên") + '</span>' +
                    '</div>' +
                    editBtnsHtml +
                '</div>' +
            '</div>';

        return article;
    }

    function updateVoteDomInPlace(voteResult) {
        if (!voteResult || !voteResult.itineraryItemId) return;
        const itemId = voteResult.itineraryItemId;
        const itemInState = findItemById(itemId);
        const isCurrentActor = String(voteResult.userId || "").toLowerCase() === currentUserId;

        if (itemInState) {
            itemInState.upvotes = voteResult.upvotes;
            itemInState.downvotes = voteResult.downvotes;
            itemInState.netScore = voteResult.netScore;
            if (isCurrentActor) {
                itemInState.currentUserVote = voteResult.currentUserVote;
            }
        }

        const box = document.querySelector('[data-vote-box="' + itemId + '"]');
        if (!box) return;

        const upCountEl = box.querySelector(".js-up-count");
        const downCountEl = box.querySelector(".js-down-count");
        const netEl = box.querySelector(".js-net-score");

        if (upCountEl) upCountEl.textContent = String(voteResult.upvotes || 0);
        if (downCountEl) downCountEl.textContent = String(voteResult.downvotes || 0);
        if (netEl) {
            const net = Number(voteResult.netScore) || 0;
            netEl.className = "st-vote-net js-net-score " + (net > 0 ? "st-vote-net-pos" : (net < 0 ? "st-vote-net-neg" : "st-vote-net-zero"));
            netEl.textContent = net > 0 ? ("+" + net) : String(net);
        }

        if (isCurrentActor) {
            const voteNum = normalizeVoteNum(voteResult.currentUserVote);
            const upBtn = box.querySelector('[data-vote="1"]');
            const downBtn = box.querySelector('[data-vote="-1"]');
            if (upBtn) upBtn.classList.toggle("st-vote-up-active", voteNum === 1);
            if (downBtn) downBtn.classList.toggle("st-vote-down-active", voteNum === -1);
        }
    }

    // -------------------------------------------------------------------------
    // HTML5 Drag & Drop Reordering (within day & across days)
    // -------------------------------------------------------------------------
    function getDragAfterElement(container, mouseY) {
        const cards = Array.prototype.slice.call(
            container.querySelectorAll(".st-stop-card:not(.st-dragging)")
        );

        let closest = { offset: Number.NEGATIVE_INFINITY, element: null };
        cards.forEach(function (child) {
            const box = child.getBoundingClientRect();
            const offset = mouseY - box.top - box.height / 2;
            if (offset < 0 && offset > closest.offset) {
                closest = { offset: offset, element: child };
            }
        });
        return closest.element;
    }

    function bindDropzoneDragEvents() {
        if (!canEdit || !timelineContainer) return;

        timelineContainer.querySelectorAll(".st-stop-card").forEach(function (card) {
            card.addEventListener("dragstart", function (e) {
                draggedItemId = card.getAttribute("data-item-id");
                card.classList.add("st-dragging");
                if (e.dataTransfer) {
                    e.dataTransfer.effectAllowed = "move";
                    e.dataTransfer.setData("text/plain", draggedItemId || "");
                }
            });

            card.addEventListener("dragend", function () {
                card.classList.remove("st-dragging");
                timelineContainer.querySelectorAll(".st-day-dropzone.st-drop-active").forEach(function (dz) {
                    dz.classList.remove("st-drop-active");
                });
            });
        });

        timelineContainer.querySelectorAll(".st-day-dropzone").forEach(function (dropzone) {
            dropzone.addEventListener("dragover", function (e) {
                if (!canEdit || !draggedItemId) return;
                e.preventDefault();
                if (e.dataTransfer) {
                    e.dataTransfer.dropEffect = "move";
                }
                dropzone.classList.add("st-drop-active");

                const draggingEl = timelineContainer.querySelector(".st-stop-card.st-dragging");
                if (!draggingEl) return;

                const emptyHint = dropzone.querySelector(".st-day-empty");
                if (emptyHint) emptyHint.remove();

                const afterEl = getDragAfterElement(dropzone, e.clientY);
                if (!afterEl) {
                    dropzone.appendChild(draggingEl);
                } else {
                    dropzone.insertBefore(draggingEl, afterEl);
                }
            });

            dropzone.addEventListener("dragleave", function (e) {
                if (!dropzone.contains(e.relatedTarget)) {
                    dropzone.classList.remove("st-drop-active");
                }
            });

            dropzone.addEventListener("drop", async function (e) {
                e.preventDefault();
                dropzone.classList.remove("st-drop-active");
                const targetDayId = dropzone.getAttribute("data-day-id");
                if (!targetDayId) return;

                const orderedIds = Array.prototype.slice
                    .call(dropzone.querySelectorAll(".st-stop-card"))
                    .map(function (el) { return el.getAttribute("data-item-id"); })
                    .filter(Boolean);

                try {
                    const resp = await postJsonHandler("Reorder", {
                        targetTripDayId: targetDayId,
                        orderedItemIds: orderedIds
                    });
                    if (resp && resp.schedule) {
                        applyScheduleState(resp.schedule);
                    }
                } catch (err) {
                    if (window.SplitTrip) {
                        window.SplitTrip.showToast("Không thể sắp xếp", err.message || "Lỗi cập nhật thứ tự.");
                    }
                    await fetchScheduleLive();
                } finally {
                    draggedItemId = null;
                }
            });
        });
    }

    // Re-render initial state with interactive handlers
    if (scheduleState) {
        renderDayFilterPills();
        renderDaysTimeline();
    } else {
        bindDropzoneDragEvents();
    }

    // -------------------------------------------------------------------------
    // Delegated Click Actions (Filter, Add/Edit/Delete Stop, Edit Day, Vote, Move)
    // -------------------------------------------------------------------------
    if (dayFilterContainer) {
        dayFilterContainer.addEventListener("click", function (e) {
            const btn = e.target.closest("[data-filter-day]");
            if (!btn) return;
            activeFilterDayId = btn.getAttribute("data-filter-day") || "all";
            renderDayFilterPills();
            renderDaysTimeline();
        });
    }

    function openStopModalForCreate(defaultDayId) {
        if (!stopModal) return;
        document.getElementById("stStopModalLabel").textContent = "Thêm điểm dừng lịch trình";
        document.getElementById("stStopFormError").classList.add("d-none");
        document.getElementById("stStopItemId").value = "";
        document.getElementById("stStopTitle").value = "";
        document.getElementById("stStopStartTime").value = "";
        document.getElementById("stStopEndTime").value = "";
        document.getElementById("stStopLocation").value = "";
        document.getElementById("stStopCost").value = "0";
        document.getElementById("stStopAddress").value = "";
        document.getElementById("stStopNotes").value = "";

        const daySelect = document.getElementById("stStopDaySelect");
        if (daySelect && defaultDayId) {
            daySelect.value = defaultDayId;
        } else if (daySelect && activeFilterDayId !== "all") {
            daySelect.value = activeFilterDayId;
        }

        stopModal.show();
    }

    function openStopModalForEdit(itemId) {
        const item = findItemById(itemId);
        if (!item || !stopModal) return;

        document.getElementById("stStopModalLabel").textContent = "Chỉnh sửa điểm dừng";
        document.getElementById("stStopFormError").classList.add("d-none");
        document.getElementById("stStopItemId").value = item.id;
        document.getElementById("stStopDaySelect").value = item.tripDayId;
        document.getElementById("stStopTitle").value = item.title || "";
        document.getElementById("stStopStartTime").value = item.startTimeFormatted || "";
        document.getElementById("stStopEndTime").value = item.endTimeFormatted || "";
        document.getElementById("stStopLocation").value = item.locationName || "";
        document.getElementById("stStopCost").value = String(Math.round(Number(item.estimatedCost) || 0));
        document.getElementById("stStopAddress").value = item.address || "";
        document.getElementById("stStopNotes").value = item.notes || "";

        stopModal.show();
    }

    const topAddBtn = document.getElementById("stOpenAddStopBtn");
    if (topAddBtn) {
        topAddBtn.addEventListener("click", function () {
            openStopModalForCreate(null);
        });
    }

    const topAiBtn = document.getElementById("stOpenAiModalBtn");
    if (topAiBtn && aiModal) {
        topAiBtn.addEventListener("click", function () {
            document.getElementById("stAiModalError").classList.add("d-none");
            aiModal.show();
        });
    }

    if (timelineContainer) {
        timelineContainer.addEventListener("click", async function (e) {
            const addDayBtn = e.target.closest(".js-add-stop-day-btn");
            if (addDayBtn) {
                openStopModalForCreate(addDayBtn.getAttribute("data-day-id"));
                return;
            }

            const editStopBtn = e.target.closest(".js-edit-stop-btn");
            if (editStopBtn) {
                openStopModalForEdit(editStopBtn.getAttribute("data-item-id"));
                return;
            }

            const deleteStopBtn = e.target.closest(".js-delete-stop-btn");
            if (deleteStopBtn) {
                const itemId = deleteStopBtn.getAttribute("data-item-id");
                const item = findItemById(itemId);
                const label = item ? item.title : "điểm dừng này";
                if (!window.confirm('Bạn chắc chắn muốn xóa "' + label + '" khỏi lịch trình?')) {
                    return;
                }
                deleteStopBtn.disabled = true;
                try {
                    const resp = await postJsonHandler("DeleteItem", { itemId: itemId });
                    if (resp && resp.schedule) {
                        applyScheduleState(resp.schedule);
                    }
                } catch (err) {
                    if (window.SplitTrip) {
                        window.SplitTrip.showToast("Lỗi xóa điểm dừng", err.message);
                    }
                } finally {
                    deleteStopBtn.disabled = false;
                }
                return;
            }

            const editDayBtn = e.target.closest(".js-edit-day-btn");
            if (editDayBtn && dayModal) {
                const day = findDayById(editDayBtn.getAttribute("data-day-id"));
                if (!day) return;
                document.getElementById("stDayFormError").classList.add("d-none");
                document.getElementById("stEditDayId").value = day.id;
                document.getElementById("stEditDayTitle").value = day.title || ("Ngày " + day.dayNumber);
                document.getElementById("stEditDayNotes").value = day.notes || "";
                dayModal.show();
                return;
            }

            const moveBtn = e.target.closest(".js-move-step-btn");
            if (moveBtn && scheduleState) {
                const itemId = moveBtn.getAttribute("data-item-id");
                const dir = parseInt(moveBtn.getAttribute("data-dir") || "0", 10);
                const item = findItemById(itemId);
                if (!item || !dir) return;
                const day = findDayById(item.tripDayId);
                if (!day || !Array.isArray(day.items)) return;

                const ids = day.items.map(function (x) { return x.id; });
                const curIdx = ids.findIndex(function (x) { return String(x).toLowerCase() === String(itemId).toLowerCase(); });
                const targetIdx = curIdx + dir;
                if (curIdx < 0 || targetIdx < 0 || targetIdx >= ids.length) return;

                const tmp = ids[curIdx];
                ids[curIdx] = ids[targetIdx];
                ids[targetIdx] = tmp;

                try {
                    const resp = await postJsonHandler("Reorder", {
                        targetTripDayId: day.id,
                        orderedItemIds: ids
                    });
                    if (resp && resp.schedule) {
                        applyScheduleState(resp.schedule);
                    }
                } catch (err) {
                    if (window.SplitTrip) {
                        window.SplitTrip.showToast("Lỗi đổi thứ tự", err.message);
                    }
                }
                return;
            }

            const voteBtn = e.target.closest(".js-vote-btn");
            if (voteBtn && canEdit) {
                const itemId = voteBtn.getAttribute("data-item-id");
                const voteValue = parseInt(voteBtn.getAttribute("data-vote") || "1", 10);
                voteBtn.disabled = true;
                try {
                    if (window.SplitTrip) {
                        try {
                            const conn = await window.SplitTrip.getHub();
                            const hubResult = await conn.invoke("ToggleItineraryVote", tripId, itemId, voteValue);
                            if (hubResult) {
                                updateVoteDomInPlace(hubResult);
                            }
                            return;
                        } catch (hubErr) {
                            // Fallback to HTTP handler if SignalR invoke fails
                        }
                    }

                    const resp = await postJsonHandler("Vote", {
                        itemId: itemId,
                        voteType: voteValue
                    });
                    if (resp && resp.vote) {
                        updateVoteDomInPlace(resp.vote);
                    }
                } catch (err) {
                    if (window.SplitTrip) {
                        window.SplitTrip.showToast("Không thể bình chọn", err.message);
                    }
                } finally {
                    voteBtn.disabled = false;
                }
            }
        });
    }

    // -------------------------------------------------------------------------
    // Stop Form & Day Form Submissions
    // -------------------------------------------------------------------------
    const stopForm = document.getElementById("stStopForm");
    if (stopForm) {
        stopForm.addEventListener("submit", async function (e) {
            e.preventDefault();
            const errEl = document.getElementById("stStopFormError");
            const saveBtn = document.getElementById("stSaveStopBtn");
            errEl.classList.add("d-none");
            saveBtn.disabled = true;

            const rawItemId = document.getElementById("stStopItemId").value;
            const payload = {
                itemId: rawItemId ? rawItemId : null,
                tripDayId: document.getElementById("stStopDaySelect").value,
                title: document.getElementById("stStopTitle").value,
                startTime: document.getElementById("stStopStartTime").value || null,
                endTime: document.getElementById("stStopEndTime").value || null,
                locationName: document.getElementById("stStopLocation").value || null,
                estimatedCost: Number(document.getElementById("stStopCost").value) || 0,
                address: document.getElementById("stStopAddress").value || null,
                notes: document.getElementById("stStopNotes").value || null
            };

            try {
                const resp = await postJsonHandler("SaveItem", payload);
                if (resp && resp.schedule) {
                    applyScheduleState(resp.schedule);
                }
                if (stopModal) stopModal.hide();
                if (window.SplitTrip) {
                    window.SplitTrip.showToast(
                        "Đã lưu lịch trình",
                        rawItemId ? "Điểm dừng đã được cập nhật." : "Đã thêm điểm dừng mới vào lịch trình."
                    );
                }
            } catch (err) {
                errEl.textContent = err.message || "Vui lòng kiểm tra lại thông tin điểm dừng.";
                errEl.classList.remove("d-none");
            } finally {
                saveBtn.disabled = false;
            }
        });
    }

    const dayForm = document.getElementById("stDayForm");
    if (dayForm) {
        dayForm.addEventListener("submit", async function (e) {
            e.preventDefault();
            const errEl = document.getElementById("stDayFormError");
            const saveBtn = document.getElementById("stSaveDayBtn");
            errEl.classList.add("d-none");
            saveBtn.disabled = true;

            try {
                const resp = await postJsonHandler("UpdateDay", {
                    tripDayId: document.getElementById("stEditDayId").value,
                    title: document.getElementById("stEditDayTitle").value,
                    notes: document.getElementById("stEditDayNotes").value
                });
                if (resp && resp.schedule) {
                    applyScheduleState(resp.schedule);
                }
                if (dayModal) dayModal.hide();
            } catch (err) {
                errEl.textContent = err.message || "Không thể cập nhật thông tin ngày.";
                errEl.classList.remove("d-none");
            } finally {
                saveBtn.disabled = false;
            }
        });
    }

    // -------------------------------------------------------------------------
    // AI Trip Planner Modal (Preview + Apply)
    // -------------------------------------------------------------------------
    const generateAiBtn = document.getElementById("stGenerateAiPreviewBtn");
    const applyAiBtn = document.getElementById("stApplyAiPlanBtn");

    function renderAiPreview(preview) {
        const wrapper = document.getElementById("stAiPreviewWrapper");
        const container = document.getElementById("stAiPreviewContainer");
        const sourceBadge = document.getElementById("stAiSourceBadge");
        const costSummary = document.getElementById("stAiPreviewCostSummary");
        const fallbackHint = document.getElementById("stAiFallbackHint");

        if (!wrapper || !container) return;

        sourceBadge.textContent = "✨ " + (preview.sourceLabel || "AI Planner");
        costSummary.textContent = "Tổng dự kiến gợi ý: " + formatVnd(preview.totalEstimatedCost || 0);
        fallbackHint.textContent = preview.fallbackReason || "";

        container.innerHTML = "";
        (preview.days || []).forEach(function (d) {
            const dayBox = document.createElement("div");
            dayBox.className = "st-ai-preview-day";

            let rowsHtml = "";
            (d.items || []).forEach(function (it) {
                const timeRange = it.startTime ? (it.startTime + (it.endTime ? " – " + it.endTime : "")) : "--:--";
                rowsHtml +=
                    '<div class="d-flex justify-content-between align-items-start gap-2 py-2 border-top small">' +
                        '<div>' +
                            '<span class="st-time-pill me-2">' + escapeHtml(timeRange) + '</span>' +
                            '<strong>' + escapeHtml(it.title) + '</strong>' +
                            (it.locationName ? ' <span class="text-muted">(' + escapeHtml(it.locationName) + ')</span>' : '') +
                            (it.notes ? '<div class="text-muted mt-1">' + escapeHtml(it.notes) + '</div>' : '') +
                        '</div>' +
                        '<span class="st-cost-chip flex-shrink-0">' + escapeHtml((Number(it.estimatedCost) || 0) > 0 ? formatVnd(it.estimatedCost) : "Miễn phí") + '</span>' +
                    '</div>';
            });

            dayBox.innerHTML =
                '<div class="fw-bold text-dark mb-1">' + escapeHtml(d.title || ("Ngày " + d.dayNumber)) +
                ' <span class="text-muted fw-normal small">(' + escapeHtml(formatDateVi(d.date)) + ')</span></div>' +
                (d.notes ? '<div class="small text-secondary mb-2">' + escapeHtml(d.notes) + '</div>' : '') +
                rowsHtml;

            container.appendChild(dayBox);
        });

        wrapper.classList.remove("d-none");
        if (applyAiBtn) applyAiBtn.disabled = false;
    }

    if (generateAiBtn) {
        generateAiBtn.addEventListener("click", async function () {
            const errEl = document.getElementById("stAiModalError");
            errEl.classList.add("d-none");
            generateAiBtn.disabled = true;
            const origText = generateAiBtn.textContent;
            generateAiBtn.textContent = "⏳ Đang thiết kế lịch trình...";

            try {
                const resp = await postJsonHandler("AiPreview", {
                    travelStyle: document.getElementById("stAiStyleSelect").value,
                    budgetPerPerson: Number(document.getElementById("stAiBudgetInput").value) || null,
                    preferencesPrompt: document.getElementById("stAiPromptInput").value || null
                });
                if (resp && resp.preview) {
                    latestAiPreview = resp.preview;
                    renderAiPreview(resp.preview);
                }
            } catch (err) {
                errEl.textContent = err.message || "Không thể tạo gợi ý lịch trình.";
                errEl.classList.remove("d-none");
            } finally {
                generateAiBtn.disabled = false;
                generateAiBtn.textContent = origText;
            }
        });
    }

    if (applyAiBtn) {
        applyAiBtn.addEventListener("click", async function () {
            if (!latestAiPreview || !Array.isArray(latestAiPreview.days)) return;
            const errEl = document.getElementById("stAiModalError");
            errEl.classList.add("d-none");
            applyAiBtn.disabled = true;
            const origText = applyAiBtn.textContent;
            applyAiBtn.textContent = "Đang áp dụng...";

            try {
                const replaceOld = Boolean(document.getElementById("stAiReplaceExistingCheck").checked);
                const resp = await postJsonHandler("AiApply", {
                    days: latestAiPreview.days,
                    clearExistingAiItems: replaceOld
                });
                if (resp && resp.schedule) {
                    applyScheduleState(resp.schedule);
                }
                if (aiModal) aiModal.hide();
                if (window.SplitTrip) {
                    window.SplitTrip.showToast("AI Trip Planner", "Đã thêm các điểm dừng gợi ý vào lịch trình chuyến đi!");
                }
            } catch (err) {
                errEl.textContent = err.message || "Không thể áp dụng lịch trình gợi ý.";
                errEl.classList.remove("d-none");
                applyAiBtn.disabled = false;
            } finally {
                applyAiBtn.textContent = origText;
            }
        });
    }

    // -------------------------------------------------------------------------
    // SignalR Real-Time Synchronization
    // -------------------------------------------------------------------------
    if (window.SplitTrip && tripId) {
        window.SplitTrip.joinTripRoom(tripId)
            .then(function (conn) {
                function isOtherActor(payload) {
                    return !payload || !payload.actorUserId || String(payload.actorUserId).toLowerCase() !== currentUserId;
                }

                conn.on("ItineraryVoteUpdated", function (voteDto) {
                    updateVoteDomInPlace(voteDto);
                });

                conn.on("ItineraryItemAdded", function (payload) {
                    if (isOtherActor(payload)) {
                        fetchScheduleLive("Điểm dừng mới", payload && payload.message ? payload.message : "Lịch trình vừa có điểm dừng mới.");
                    }
                });

                conn.on("ItineraryItemUpdated", function (payload) {
                    if (isOtherActor(payload)) {
                        fetchScheduleLive("Cập nhật lịch trình", payload && payload.message ? payload.message : "Một điểm dừng vừa được chỉnh sửa.");
                    }
                });

                conn.on("ItineraryItemDeleted", function (payload) {
                    if (isOtherActor(payload)) {
                        fetchScheduleLive("Xóa điểm dừng", payload && payload.message ? payload.message : "Một điểm dừng vừa bị xóa.");
                    }
                });

                conn.on("ItineraryReordered", function (payload) {
                    if (isOtherActor(payload)) {
                        fetchScheduleLive("Sắp xếp lịch trình", payload && payload.message ? payload.message : "Thứ tự điểm dừng vừa được cập nhật.");
                    }
                });

                conn.on("ItineraryDayUpdated", function (payload) {
                    if (isOtherActor(payload)) {
                        fetchScheduleLive("Cập nhật ngày", payload && payload.message ? payload.message : "Tiêu đề ngày vừa được cập nhật.");
                    }
                });

                conn.on("AiItineraryGenerated", function (payload) {
                    if (isOtherActor(payload)) {
                        fetchScheduleLive("AI Trip Planner", payload && payload.message ? payload.message : "Lịch trình gợi ý từ AI vừa được áp dụng!");
                    }
                });

                conn.on("ItineraryChanged", function () {
                    fetchScheduleLive();
                });

                conn.on("DepartureReminderTriggered", function (payload) {
                    const msg = payload && payload.title
                        ? ('Điểm dừng "' + payload.title + '" sẽ khởi hành lúc ' + (payload.startTime || "") + '!')
                        : "Có điểm dừng sắp đến giờ khởi hành!";
                    fetchScheduleLive("🔔 Sắp đến giờ khởi hành", msg);
                });

                conn.on("TripStatusChanged", function (payload) {
                    fetchScheduleLive("Trạng thái chuyến đi", payload && payload.message ? payload.message : "Trạng thái chuyến đi vừa thay đổi.");
                });
            })
            .catch(function () { /* ignore transient disconnect */ });
    }
})();
