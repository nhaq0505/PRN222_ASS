(function () {
    "use strict";

    function formatVnd(amount) {
        const n = Math.round(Number(amount) || 0);
        return n.toLocaleString("vi-VN") + "đ";
    }

    function parseWholeVnd(val) {
        if (val === null || val === undefined || val === "") return 0;
        const cleaned = String(val).replace(/[^\d-]/g, "");
        const parsed = parseInt(cleaned, 10);
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    // =========================================================================
    // 1. Multi-Mode Expense Modal & Live Whole-VND Largest-Remainder Preview
    // =========================================================================
    function initExpensesPage() {
        const pageRoot = document.getElementById("stExpensesWorkspace");
        if (!pageRoot) return;

        const tripId = pageRoot.getAttribute("data-trip-id");
        const modalEl = document.getElementById("stUpsertExpenseModal");
        const formEl = document.getElementById("stUpsertExpenseForm");

        if (modalEl && formEl) {
            const expenseIdInput = document.getElementById("stFormExpenseId");
            const modalTitleEl = document.getElementById("stUpsertModalTitle");
            const titleInput = document.getElementById("stFormTitle");
            const categorySelect = document.getElementById("stFormCategory");
            const paidBySelect = document.getElementById("stFormPaidBy");
            const totalInput = document.getElementById("stFormTotalAmount");
            const notesInput = document.getElementById("stFormNotes");
            const splitModeInput = document.getElementById("stFormSplitMode");
            const modeButtons = Array.from(modalEl.querySelectorAll("[data-split-mode-btn]"));
            const participantRows = Array.from(modalEl.querySelectorAll("[data-participant-row]"));
            const validationBox = document.getElementById("stSplitPreviewStatus");
            const submitBtn = document.getElementById("stSaveExpenseSubmitBtn");
            const modeHintEl = document.getElementById("stSplitModeHint");

            function getCurrentMode() {
                return splitModeInput ? splitModeInput.value : "Equal";
            }

            function setMode(mode, resetDefaults) {
                if (splitModeInput) splitModeInput.value = mode;
                modeButtons.forEach(function (btn) {
                    btn.classList.toggle("active", btn.getAttribute("data-split-mode-btn") === mode);
                });

                if (modeHintEl) {
                    if (mode === "Equal") {
                        modeHintEl.textContent = "Chia đều số nguyên VNĐ cho các thành viên được tick (phần lẻ 1đ tự động ưu tiên người trả tiền).";
                    } else if (mode === "ExactAmount") {
                        modeHintEl.textContent = "Nhập chính xác số tiền VNĐ từng người phải trả. Tổng cộng phải khớp 100% tổng chi tiêu.";
                    } else if (mode === "Percentage") {
                        modeHintEl.textContent = "Nhập tỷ lệ % cho từng người tham gia. Tổng tỷ lệ bắt buộc bằng 100%.";
                    } else if (mode === "Shares") {
                        modeHintEl.textContent = "Nhập hệ số phần chia dương (ví dụ: 1, 2, 0.5) để chia theo trọng số.";
                    }
                }

                const activeRows = participantRows.filter(function (r) {
                    const cb = r.querySelector("[data-participant-check]");
                    return cb && cb.checked;
                });

                participantRows.forEach(function (row, idx) {
                    const check = row.querySelector("[data-participant-check]");
                    const valWrap = row.querySelector("[data-participant-input-wrap]");
                    const valInput = row.querySelector("[data-participant-val]");
                    const unitBadge = row.querySelector("[data-participant-unit]");
                    const isChecked = check ? check.checked : false;

                    if (valWrap) {
                        valWrap.classList.toggle("d-none", mode === "Equal");
                    }
                    if (valInput) {
                        valInput.disabled = !isChecked;
                        if (mode === "ExactAmount") {
                            valInput.step = "1";
                            valInput.min = "0";
                        } else if (mode === "Percentage") {
                            valInput.step = "0.01";
                            valInput.min = "0.01";
                        } else {
                            valInput.step = "0.5";
                            valInput.min = "0.1";
                        }

                        if (resetDefaults && isChecked) {
                            if (mode === "Equal") {
                                valInput.value = "1";
                            } else if (mode === "Shares") {
                                valInput.value = "1";
                            } else if (mode === "Percentage") {
                                const count = activeRows.length || 1;
                                const basePct = Math.floor((10000 / count)) / 100;
                                const activeIdx = activeRows.indexOf(row);
                                if (activeIdx === activeRows.length - 1) {
                                    const others = basePct * (count - 1);
                                    valInput.value = (100 - others).toFixed(2).replace(/\.00$/, "");
                                } else {
                                    valInput.value = basePct.toFixed(2).replace(/\.00$/, "");
                                }
                            } else if (mode === "ExactAmount") {
                                const total = parseWholeVnd(totalInput ? totalInput.value : 0);
                                const count = activeRows.length || 1;
                                const baseVnd = total > 0 ? Math.floor(total / count) : 0;
                                const rem = total > 0 ? total - (baseVnd * count) : 0;
                                const activeIdx = activeRows.indexOf(row);
                                valInput.value = String(baseVnd + (activeIdx < rem ? 1 : 0));
                            }
                        } else if (!isChecked && resetDefaults) {
                            valInput.value = "0";
                        }
                    }
                    if (unitBadge) {
                        unitBadge.textContent = mode === "ExactAmount" ? "VNĐ" : (mode === "Percentage" ? "%" : "phần");
                    }
                });

                recomputeLiveSplitPreview();
            }

            function allocateLargestRemainderJs(totalAmount, paidByUserId, entries) {
                const totalWeight = entries.reduce(function (acc, e) { return acc + e.weight; }, 0);
                if (totalWeight <= 0) return {};

                const provisional = entries.map(function (e, idx) {
                    const exact = (totalAmount * e.weight) / totalWeight;
                    const floored = Math.floor(exact);
                    return {
                        userId: e.userId,
                        orderIndex: idx,
                        floored: floored,
                        remainder: exact - floored
                    };
                });

                const flooredSum = provisional.reduce(function (acc, p) { return acc + p.floored; }, 0);
                const remainderVnd = Math.round(totalAmount - flooredSum);

                const sorted = provisional.slice().sort(function (a, b) {
                    if (Math.abs(b.remainder - a.remainder) > 1e-9) {
                        return b.remainder - a.remainder;
                    }
                    const aPayer = a.userId === paidByUserId ? 0 : 1;
                    const bPayer = b.userId === paidByUserId ? 0 : 1;
                    if (aPayer !== bPayer) return aPayer - bPayer;
                    return a.orderIndex - b.orderIndex;
                });

                const winners = new Set(sorted.slice(0, Math.max(0, remainderVnd)).map(function (w) { return w.userId; }));
                const out = {};
                provisional.forEach(function (p) {
                    out[p.userId] = p.floored + (winners.has(p.userId) ? 1 : 0);
                });
                return out;
            }

            function recomputeLiveSplitPreview() {
                const totalAmount = parseWholeVnd(totalInput ? totalInput.value : 0);
                const paidByUserId = paidBySelect ? paidBySelect.value : "";
                const mode = getCurrentMode();

                const selected = [];
                participantRows.forEach(function (row) {
                    const userId = row.getAttribute("data-user-id");
                    const check = row.querySelector("[data-participant-check]");
                    const valInput = row.querySelector("[data-participant-val]");
                    const owedLabel = row.querySelector("[data-participant-owed]");
                    const isChecked = check ? check.checked : false;

                    row.classList.toggle("is-inactive", !isChecked);
                    if (valInput) valInput.disabled = !isChecked;

                    if (isChecked) {
                        const rawVal = valInput ? parseFloat(valInput.value || "0") : 0;
                        selected.push({
                            userId: userId,
                            value: Number.isFinite(rawVal) ? rawVal : 0,
                            owedLabel: owedLabel
                        });
                    } else if (owedLabel) {
                        owedLabel.textContent = "0đ";
                    }
                });

                if (totalAmount <= 0) {
                    selected.forEach(function (s) { if (s.owedLabel) s.owedLabel.textContent = "0đ"; });
                    setValidationState(false, "Nhập tổng chi tiêu nguyên dương (VNĐ) để xem trước phần chia.");
                    return;
                }

                if (selected.length === 0) {
                    setValidationState(false, "Vui lòng chọn ít nhất 1 thành viên tham gia chia tiền.");
                    return;
                }

                if (mode === "Equal") {
                    const entries = selected.map(function (s) { return { userId: s.userId, weight: 1 }; });
                    const map = allocateLargestRemainderJs(totalAmount, paidByUserId, entries);
                    selected.forEach(function (s) {
                        if (s.owedLabel) s.owedLabel.textContent = formatVnd(map[s.userId] || 0);
                    });
                    setValidationState(true, "✓ Chia đều " + selected.length + " người — Tổng khớp chính xác " + formatVnd(totalAmount) + ".");
                } else if (mode === "ExactAmount") {
                    let sum = 0;
                    let hasInvalid = false;
                    selected.forEach(function (s) {
                        const v = Math.round(s.value);
                        if (s.value < 0 || Math.floor(s.value) !== s.value) {
                            hasInvalid = true;
                        }
                        sum += v;
                        if (s.owedLabel) s.owedLabel.textContent = formatVnd(Math.max(0, v));
                    });

                    if (hasInvalid) {
                        setValidationState(false, "Số tiền cụ thể phải là số nguyên không âm (VNĐ).");
                    } else if (sum !== totalAmount) {
                        const delta = totalAmount - sum;
                        setValidationState(false, "Tổng nhập tay: " + formatVnd(sum) + " — Lệch " + (delta > 0 ? "+" : "") + formatVnd(delta) + " so với tổng " + formatVnd(totalAmount) + ".");
                    } else {
                        setValidationState(true, "✓ Tổng số tiền cụ thể khớp 100% (" + formatVnd(totalAmount) + ").");
                    }
                } else if (mode === "Percentage") {
                    const sumPct = selected.reduce(function (acc, s) { return acc + s.value; }, 0);
                    const invalidPct = selected.some(function (s) { return s.value <= 0 || s.value > 100; });
                    if (invalidPct) {
                        selected.forEach(function (s) { if (s.owedLabel) s.owedLabel.textContent = "0đ"; });
                        setValidationState(false, "Mỗi thành viên được chọn phải có tỷ lệ > 0% và ≤ 100%.");
                    } else if (Math.abs(sumPct - 100) > 0.01) {
                        const entries = selected.map(function (s) { return { userId: s.userId, weight: s.value }; });
                        const map = allocateLargestRemainderJs(totalAmount, paidByUserId, entries);
                        selected.forEach(function (s) {
                            if (s.owedLabel) s.owedLabel.textContent = formatVnd(map[s.userId] || 0);
                        });
                        setValidationState(false, "Tổng tỷ lệ đang là " + sumPct.toFixed(2).replace(/\.00$/, "") + "% (cần đúng 100%).");
                    } else {
                        const entries = selected.map(function (s) { return { userId: s.userId, weight: s.value }; });
                        const map = allocateLargestRemainderJs(totalAmount, paidByUserId, entries);
                        selected.forEach(function (s) {
                            if (s.owedLabel) s.owedLabel.textContent = formatVnd(map[s.userId] || 0);
                        });
                        setValidationState(true, "✓ Tổng 100% — Đã làm tròn & bù lẻ chính xác " + formatVnd(totalAmount) + ".");
                    }
                } else if (mode === "Shares") {
                    const invalidWeight = selected.some(function (s) { return s.value <= 0; });
                    if (invalidWeight) {
                        selected.forEach(function (s) { if (s.owedLabel) s.owedLabel.textContent = "0đ"; });
                        setValidationState(false, "Hệ số phần chia (Shares) của mỗi người được chọn phải lớn hơn 0.");
                    } else {
                        const totalShares = selected.reduce(function (acc, s) { return acc + s.value; }, 0);
                        const entries = selected.map(function (s) { return { userId: s.userId, weight: s.value }; });
                        const map = allocateLargestRemainderJs(totalAmount, paidByUserId, entries);
                        selected.forEach(function (s) {
                            if (s.owedLabel) s.owedLabel.textContent = formatVnd(map[s.userId] || 0);
                        });
                        setValidationState(true, "✓ Chia theo tổng " + totalShares + " phần — Khớp chính xác " + formatVnd(totalAmount) + ".");
                    }
                }
            }

            function setValidationState(isValid, message) {
                if (validationBox) {
                    validationBox.className = "small px-3 py-2 rounded-3 fw-medium " + (isValid ? "st-alert-success" : "st-alert-danger");
                    validationBox.textContent = message;
                }
                if (submitBtn) {
                    submitBtn.disabled = !isValid;
                }
            }

            modeButtons.forEach(function (btn) {
                btn.addEventListener("click", function () {
                    setMode(btn.getAttribute("data-split-mode-btn"), true);
                });
            });

            if (totalInput) totalInput.addEventListener("input", recomputeLiveSplitPreview);
            if (paidBySelect) paidBySelect.addEventListener("change", recomputeLiveSplitPreview);

            participantRows.forEach(function (row) {
                const cb = row.querySelector("[data-participant-check]");
                const valInput = row.querySelector("[data-participant-val]");
                if (cb) {
                    cb.addEventListener("change", function () {
                        if (cb.checked && valInput && parseFloat(valInput.value || "0") <= 0 && getCurrentMode() !== "ExactAmount") {
                            valInput.value = "1";
                        }
                        recomputeLiveSplitPreview();
                    });
                }
                if (valInput) {
                    valInput.addEventListener("input", recomputeLiveSplitPreview);
                }
            });

            // Open Create Modal cleanly
            const openCreateBtn = document.getElementById("stOpenCreateExpenseBtn");
            if (openCreateBtn) {
                openCreateBtn.addEventListener("click", function () {
                    if (expenseIdInput) expenseIdInput.value = "";
                    if (modalTitleEl) modalTitleEl.textContent = "Thêm khoản chi tiêu mới";
                    if (titleInput) titleInput.value = "";
                    if (categorySelect) categorySelect.value = "Food";
                    if (totalInput) totalInput.value = "";
                    if (notesInput) notesInput.value = "";
                    participantRows.forEach(function (row) {
                        const cb = row.querySelector("[data-participant-check]");
                        const valInput = row.querySelector("[data-participant-val]");
                        if (cb) cb.checked = true;
                        if (valInput) valInput.value = "1";
                    });
                    setMode("Equal", true);
                });
            }

            // Open Edit Modal from existing expense card JSON payload
            document.addEventListener("click", function (e) {
                const editBtn = e.target.closest && e.target.closest("[data-edit-expense]");
                if (!editBtn) return;

                try {
                    const payload = JSON.parse(editBtn.getAttribute("data-edit-expense") || "{}");
                    if (expenseIdInput) expenseIdInput.value = payload.id || "";
                    if (modalTitleEl) modalTitleEl.textContent = "Chỉnh sửa khoản chi tiêu";
                    if (titleInput) titleInput.value = payload.title || "";
                    if (categorySelect) categorySelect.value = payload.category || "Other";
                    if (paidBySelect && payload.paidByUserId) paidBySelect.value = payload.paidByUserId;
                    if (totalInput) totalInput.value = String(Math.round(payload.totalAmount || 0));
                    if (notesInput) notesInput.value = payload.notes || "";

                    const splitMap = {};
                    (payload.splits || []).forEach(function (s) {
                        splitMap[s.userId] = s;
                    });

                    participantRows.forEach(function (row) {
                        const uid = row.getAttribute("data-user-id");
                        const cb = row.querySelector("[data-participant-check]");
                        const valInput = row.querySelector("[data-participant-val]");
                        const existingSplit = splitMap[uid];
                        if (existingSplit) {
                            if (cb) cb.checked = true;
                            if (valInput) {
                                valInput.value = payload.splitMode === "ExactAmount"
                                    ? String(Math.round(existingSplit.owedAmount))
                                    : String(existingSplit.shareRatioOrWeight);
                            }
                        } else {
                            if (cb) cb.checked = false;
                            if (valInput) valInput.value = "0";
                        }
                    });

                    setMode(payload.splitMode || "Equal", false);
                    if (typeof bootstrap !== "undefined") {
                        bootstrap.Modal.getOrCreateInstance(modalEl).show();
                    }
                } catch (err) {
                    // ignore malformed attribute
                }
            });

            setMode("Equal", true);
        }

        // Receipt upload modal toggle between file upload and Demo AI sample
        const demoToggle = document.getElementById("stUseDemoSampleSwitch");
        const fileInputWrap = document.getElementById("stReceiptFileInputWrap");
        const fileInput = document.getElementById("stReceiptImageInput");
        if (demoToggle && fileInputWrap && fileInput) {
            const syncDemoMode = function () {
                const useDemo = demoToggle.checked;
                fileInputWrap.classList.toggle("opacity-50", useDemo);
                fileInput.required = !useDemo;
            };
            demoToggle.addEventListener("change", syncDemoMode);
            syncDemoMode();
        }

        // Real-time SignalR sync for Expenses feed
        if (window.SplitTrip && tripId) {
            window.SplitTrip.joinTripRoom(tripId)
                .then(function (conn) {
                    const refreshExpensesLive = async function (toastTitle, toastMsg) {
                        if (toastMsg) {
                            window.SplitTrip.showToast(toastTitle || "Cập nhật chi tiêu", toastMsg);
                        }
                        try {
                            const url = new URL(window.location.href);
                            url.searchParams.set("handler", "LiveFeed");
                            const res = await fetch(url.toString(), { headers: { "Accept": "application/json" } });
                            if (!res.ok) return;
                            const data = await res.json();

                            const totalEl = document.getElementById("stKpiTotalConfirmed");
                            if (totalEl) totalEl.textContent = formatVnd(data.totalConfirmedSpend);
                            const avgEl = document.getElementById("stKpiAvgPerMember");
                            if (avgEl) avgEl.textContent = formatVnd(data.averagePerActiveMember);
                            const countEl = document.getElementById("stKpiConfirmedCount");
                            if (countEl) countEl.textContent = data.confirmedExpenseCount + " khoản đã xác thực";
                            const pendingEl = document.getElementById("stKpiAiPending");
                            if (pendingEl) pendingEl.textContent = data.openClaimingCount + " mở chọn · " + data.draftReceiptCount + " nháp";

                            // Reload softly only if no modal is currently open so the feed cards stay fresh
                            const anyModalOpen = document.querySelector(".modal.show");
                            if (!anyModalOpen) {
                                const feedRes = await fetch(window.location.href);
                                if (feedRes.ok) {
                                    const html = await feedRes.text();
                                    const doc = new DOMParser().parseFromString(html, "text/html");
                                    const nextFeed = doc.getElementById("stExpensesFeedContainer");
                                    const currFeed = document.getElementById("stExpensesFeedContainer");
                                    if (nextFeed && currFeed) {
                                        currFeed.innerHTML = nextFeed.innerHTML;
                                    }
                                }
                            }
                        } catch (e) {
                            // Ignore transient error
                        }
                    };

                    conn.on("ExpenseAdded", function (p) {
                        refreshExpensesLive("Chi tiêu mới", p && p.message ? p.message : "Một khoản chi tiêu mới vừa được thêm.");
                    });
                    conn.on("ExpenseUpdated", function (p) {
                        refreshExpensesLive("Đồng bộ chi tiêu", p && p.message ? p.message : "Số liệu chia tiền vừa cập nhật.");
                    });
                    conn.on("ExpenseDeleted", function (p) {
                        refreshExpensesLive("Xóa chi tiêu", p && p.message ? p.message : "Một khoản chi tiêu vừa được xóa.");
                    });
                    conn.on("ReceiptDraftPublished", function (p) {
                        refreshExpensesLive("Mở chọn món AI", p && p.message ? p.message : "Hóa đơn mới vừa mở nhận món trực tiếp!");
                    });
                    conn.on("TripStatusChanged", function () {
                        window.location.reload();
                    });
                })
                .catch(function () { });
        }
    }

    // =========================================================================
    // 2. Step 1: AI OCR Draft Review Table & Live Mismatch Banner
    // =========================================================================
    function initDraftReviewPage() {
        const draftForm = document.getElementById("stDraftReviewForm");
        if (!draftForm) return;

        const tbody = document.getElementById("stDraftItemsBody");
        const addItemBtn = document.getElementById("stAddDraftItemBtn");
        const autoSyncBtn = document.getElementById("stAutoReconcileTotalsBtn");
        const subtotalInput = document.getElementById("stDraftSubtotal");
        const taxInput = document.getElementById("stDraftTaxOrTip");
        const discountInput = document.getElementById("stDraftDiscount");
        const totalInput = document.getElementById("stDraftTotal");
        const mismatchBanner = document.getElementById("stLiveMismatchBanner");
        const mismatchText = document.getElementById("stLiveMismatchText");
        const reconciledBanner = document.getElementById("stLiveReconciledBanner");
        const publishBtn = document.getElementById("stPublishClaimingBtn");
        const itemsSumDisplay = document.getElementById("stDraftItemsSumDisplay");

        function reindexDraftRows() {
            if (!tbody) return;
            const rows = Array.from(tbody.querySelectorAll("tr[data-draft-item-row]"));
            rows.forEach(function (tr, idx) {
                const nameIn = tr.querySelector("[data-field='ItemName']");
                const qtyIn = tr.querySelector("[data-field='Quantity']");
                const unitIn = tr.querySelector("[data-field='UnitPrice']");
                const lineIn = tr.querySelector("[data-field='LineTotal']");
                if (nameIn) nameIn.name = "Input.Items[" + idx + "].ItemName";
                if (qtyIn) qtyIn.name = "Input.Items[" + idx + "].Quantity";
                if (unitIn) unitIn.name = "Input.Items[" + idx + "].UnitPrice";
                if (lineIn) lineIn.name = "Input.Items[" + idx + "].LineTotal";
            });
        }

        function evaluateDraftMismatch() {
            if (!tbody) return;
            const rows = Array.from(tbody.querySelectorAll("tr[data-draft-item-row]"));
            let itemsSum = 0;
            let validCount = 0;

            rows.forEach(function (tr) {
                const nameVal = (tr.querySelector("[data-field='ItemName']")?.value || "").trim();
                const lineVal = parseWholeVnd(tr.querySelector("[data-field='LineTotal']")?.value || 0);
                if (nameVal.length > 0) validCount++;
                itemsSum += Math.max(0, lineVal);
            });

            if (itemsSumDisplay) {
                itemsSumDisplay.textContent = formatVnd(itemsSum);
            }

            const subtotal = parseWholeVnd(subtotalInput ? subtotalInput.value : 0);
            const tax = parseWholeVnd(taxInput ? taxInput.value : 0);
            const discount = parseWholeVnd(discountInput ? discountInput.value : 0);
            const total = parseWholeVnd(totalInput ? totalInput.value : 0);
            const expectedTotal = itemsSum + tax - discount;

            const reasons = [];
            if (validCount === 0) {
                reasons.push("Cần ít nhất 1 dòng món ăn hợp lệ.");
            }
            if (subtotal <= 0 || total <= 0) {
                reasons.push("Tạm tính và Tổng thanh toán phải lớn hơn 0đ.");
            }
            if (itemsSum !== subtotal) {
                const d1 = itemsSum - subtotal;
                reasons.push("Tổng các món (" + formatVnd(itemsSum) + ") đang lệch " + (d1 > 0 ? "+" : "") + formatVnd(d1) + " so với Tạm tính (" + formatVnd(subtotal) + ").");
            }
            if (expectedTotal !== total || (subtotal + tax - discount) !== total) {
                const d2 = expectedTotal - total;
                reasons.push("Tổng món + Thuế/Phí - Giảm giá = " + formatVnd(expectedTotal) + ", lệch " + (d2 > 0 ? "+" : "") + formatVnd(d2) + " so với Tổng hóa đơn (" + formatVnd(total) + ").");
            }
            if (discount > subtotal + tax) {
                reasons.push("Giảm giá không được vượt quá Tạm tính + Thuế/Phí.");
            }

            const hasMismatch = reasons.length > 0;
            if (mismatchBanner) mismatchBanner.classList.toggle("d-none", !hasMismatch);
            if (reconciledBanner) reconciledBanner.classList.toggle("d-none", hasMismatch);
            if (mismatchText && hasMismatch) mismatchText.textContent = reasons.join(" ");
            if (publishBtn) publishBtn.disabled = hasMismatch;
        }

        function bindRowEvents(tr) {
            const qtyIn = tr.querySelector("[data-field='Quantity']");
            const unitIn = tr.querySelector("[data-field='UnitPrice']");
            const lineIn = tr.querySelector("[data-field='LineTotal']");
            const nameIn = tr.querySelector("[data-field='ItemName']");
            const removeBtn = tr.querySelector("[data-remove-draft-row]");

            const autoLine = function () {
                const q = Math.max(1, parseWholeVnd(qtyIn ? qtyIn.value : 1));
                const u = Math.max(0, parseWholeVnd(unitIn ? unitIn.value : 0));
                if (lineIn) lineIn.value = String(q * u);
                evaluateDraftMismatch();
            };

            if (qtyIn) qtyIn.addEventListener("input", autoLine);
            if (unitIn) unitIn.addEventListener("input", autoLine);
            if (lineIn) lineIn.addEventListener("input", evaluateDraftMismatch);
            if (nameIn) nameIn.addEventListener("input", evaluateDraftMismatch);
            if (removeBtn) {
                removeBtn.addEventListener("click", function () {
                    const rows = tbody.querySelectorAll("tr[data-draft-item-row]");
                    if (rows.length <= 1) return;
                    tr.remove();
                    reindexDraftRows();
                    evaluateDraftMismatch();
                });
            }
        }

        if (tbody) {
            Array.from(tbody.querySelectorAll("tr[data-draft-item-row]")).forEach(bindRowEvents);
        }

        if (addItemBtn && tbody) {
            addItemBtn.addEventListener("click", function () {
                const tr = document.createElement("tr");
                tr.setAttribute("data-draft-item-row", "true");
                tr.innerHTML =
                    '<td><input type="text" class="form-control form-control-sm" data-field="ItemName" placeholder="Tên món / đồ uống..." required /></td>' +
                    '<td style="width:90px;"><input type="number" min="1" step="1" class="form-control form-control-sm text-center" data-field="Quantity" value="1" required /></td>' +
                    '<td style="width:140px;"><input type="number" min="0" step="1" class="form-control form-control-sm text-end font-monospace" data-field="UnitPrice" value="0" required /></td>' +
                    '<td style="width:150px;"><input type="number" min="0" step="1" class="form-control form-control-sm text-end font-monospace fw-semibold" data-field="LineTotal" value="0" required /></td>' +
                    '<td class="text-end" style="width:52px;"><button type="button" class="btn btn-sm btn-outline-danger py-0 px-2" data-remove-draft-row title="Xóa dòng">&times;</button></td>';
                tbody.appendChild(tr);
                reindexDraftRows();
                bindRowEvents(tr);
                evaluateDraftMismatch();
            });
        }

        if (autoSyncBtn && tbody) {
            autoSyncBtn.addEventListener("click", function () {
                let itemsSum = 0;
                Array.from(tbody.querySelectorAll("tr[data-draft-item-row]")).forEach(function (tr) {
                    itemsSum += Math.max(0, parseWholeVnd(tr.querySelector("[data-field='LineTotal']")?.value || 0));
                });
                const tax = parseWholeVnd(taxInput ? taxInput.value : 0);
                const discount = parseWholeVnd(discountInput ? discountInput.value : 0);
                if (subtotalInput) subtotalInput.value = String(itemsSum);
                if (totalInput) totalInput.value = String(Math.max(0, itemsSum + tax - discount));
                evaluateDraftMismatch();
            });
        }

        [subtotalInput, taxInput, discountInput, totalInput].forEach(function (el) {
            if (el) el.addEventListener("input", evaluateDraftMismatch);
        });

        draftForm.addEventListener("submit", function () {
            reindexDraftRows();
        });

        reindexDraftRows();
        evaluateDraftMismatch();
    }

    // =========================================================================
    // 3. Step 2 & 3: Live SignalR Item Claiming Board
    // =========================================================================
    function initLiveClaimingBoard() {
        const boardRoot = document.getElementById("stLiveClaimingBoard");
        if (!boardRoot) return;

        const tripId = boardRoot.getAttribute("data-trip-id");
        const expenseId = boardRoot.getAttribute("data-expense-id");
        const currentUserId = (boardRoot.getAttribute("data-current-user-id") || "").toLowerCase();
        const isOwner = boardRoot.getAttribute("data-is-owner") === "true";
        let activeHub = null;

        function renderBoard(board) {
            if (!board) return;
            const isLocked = String(board.scanStatus) === "Locked" || board.scanStatus === 3;
            const isEditable = Boolean(board.isTripEditable) && !isLocked;
            const payerId = String(board.paidByUserId || "").toLowerCase();
            const canLock = Boolean(board.isTripEditable) && !isLocked && (currentUserId === payerId || isOwner);

            // Update status badges & step pills
            const statusPill = document.getElementById("stClaimScanStatusBadge");
            if (statusPill) {
                statusPill.className = "badge rounded-pill " + (isLocked ? "text-bg-success" : "text-bg-primary");
                statusPill.textContent = isLocked ? "🔒 Đã khóa & chốt sổ" : "🟢 Đang mở nhận món Live";
            }

            const step2 = document.getElementById("stStepPillClaiming");
            const step3 = document.getElementById("stStepPillLocked");
            if (step2) step2.className = "st-step-pill " + (isLocked ? "done" : "active");
            if (step3) step3.className = "st-step-pill " + (isLocked ? "active" : "");

            // Update unclaimed counter banner
            const unclaimedBox = document.getElementById("stUnclaimedBanner");
            if (unclaimedBox) {
                if (isLocked) {
                    unclaimedBox.className = "st-reconciled-banner mb-3";
                    unclaimedBox.innerHTML = "<strong>✓ Hóa đơn đã khóa & chốt sổ cái!</strong> Mọi thành viên đã được phân bổ chính xác theo tỷ lệ món + VAT/giảm giá.";
                } else if (board.unclaimedItemCount > 0) {
                    unclaimedBox.className = "st-mismatch-banner mb-3";
                    unclaimedBox.innerHTML = "<strong>Còn " + board.unclaimedItemCount + " món chưa có người nhận (" + formatVnd(board.unclaimedSubtotalAmount) + ").</strong> Hãy bấm vào món bạn đã dùng hoặc dùng chung để chia đều món đó!";
                } else {
                    unclaimedBox.className = "st-reconciled-banner mb-3";
                    unclaimedBox.innerHTML = "<strong>✓ Tất cả " + board.totalItemCount + " món đều đã có người nhận!</strong> Người trả tiền hoặc Trưởng đoàn có thể bấm <em>Khóa &amp; Chốt sổ</em> ngay bây giờ.";
                }
            }

            // Render dish cards
            const itemsContainer = document.getElementById("stClaimDishList");
            if (itemsContainer && Array.isArray(board.items)) {
                itemsContainer.innerHTML = "";
                board.items.forEach(function (item) {
                    const claimers = Array.isArray(item.claimers) ? item.claimers : [];
                    const claimedByMe = claimers.some(function (c) {
                        return String(c.userId || "").toLowerCase() === currentUserId;
                    });
                    const isUnclaimed = claimers.length === 0;

                    const card = document.createElement("div");
                    card.className = "st-dish-card mb-2"
                        + (claimedByMe ? " is-claimed-by-me" : "")
                        + (isUnclaimed ? " is-unclaimed" : "")
                        + (!isEditable ? " is-locked" : "")
                        + (board.lastToggledItemId && String(board.lastToggledItemId) === String(item.id) ? " st-dish-just-updated" : "");
                    card.setAttribute("data-dish-item-id", item.id);

                    let claimersHtml = "";
                    if (isUnclaimed) {
                        claimersHtml = '<span class="badge text-bg-warning-subtle text-warning-emphasis border border-warning-subtle">Chưa ai nhận</span>';
                    } else {
                        claimersHtml = claimers.map(function (c) {
                            const isMe = String(c.userId || "").toLowerCase() === currentUserId;
                            return '<span class="st-claimer-pill">' +
                                '<span class="st-claimer-avatar">' + escapeHtml(c.initials || "TV") + '</span>' +
                                '<span>' + escapeHtml(isMe ? "Bạn" : c.fullName) + '</span>' +
                                '<span class="text-muted font-monospace">(' + formatVnd(c.dishShareVnd) + ')</span>' +
                                '</span>';
                        }).join(" ");
                    }

                    card.innerHTML =
                        '<div class="d-flex justify-content-between align-items-start gap-3">' +
                            '<div class="d-flex align-items-start gap-3">' +
                                '<span class="st-dish-check mt-1">' + (claimedByMe ? "✓" : "") + '</span>' +
                                '<div>' +
                                    '<div class="fw-bold text-dark">' + escapeHtml(item.itemName) + '</div>' +
                                    '<div class="small text-muted">SL: ' + item.quantity + ' &times; ' + formatVnd(item.unitPrice) + '</div>' +
                                    '<div class="d-flex flex-wrap gap-1 mt-2">' + claimersHtml + '</div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="text-end flex-shrink-0">' +
                                '<div class="fw-bold font-monospace text-dark">' + formatVnd(item.lineTotal) + '</div>' +
                                (claimers.length > 1 ? '<div class="small text-teal fw-semibold" style="color:var(--st-primary);">Chia ' + claimers.length + ' người</div>' : '') +
                            '</div>' +
                        '</div>';

                    if (isEditable) {
                        card.addEventListener("click", function () {
                            toggleDishClaim(item.id);
                        });
                    }

                    itemsContainer.appendChild(card);
                });
            }

            // Render Member Live Pro-Rata Previews
            const membersContainer = document.getElementById("stMemberProRataList");
            if (membersContainer && Array.isArray(board.memberPreviews)) {
                membersContainer.innerHTML = "";
                board.memberPreviews.forEach(function (m) {
                    const isMe = String(m.userId || "").toLowerCase() === currentUserId;
                    const adj = Math.round(Number(m.proRataAdjustmentVnd) || 0);
                    const adjText = adj === 0 ? "0đ" : ((adj > 0 ? "+" : "") + formatVnd(adj));

                    const row = document.createElement("div");
                    row.className = "p-3 border rounded-3 mb-2 bg-white";
                    row.innerHTML =
                        '<div class="d-flex justify-content-between align-items-center mb-1">' +
                            '<div class="d-flex align-items-center gap-2">' +
                                '<span class="st-avatar-sm">' + escapeHtml(m.initials || "TV") + '</span>' +
                                '<div>' +
                                    '<span class="fw-semibold text-dark small">' + escapeHtml(m.fullName) + '</span> ' +
                                    (isMe ? '<span class="badge bg-light text-secondary border" style="font-size:0.68rem;">Bạn</span> ' : '') +
                                    (m.isPayer ? '<span class="badge text-bg-dark" style="font-size:0.68rem;">Đã trả bill</span>' : '') +
                                '</div>' +
                            '</div>' +
                            '<div class="fw-bold font-monospace text-dark">' + formatVnd(m.ledgerOwedAmount) + '</div>' +
                        '</div>' +
                        '<div class="d-flex flex-wrap justify-content-between small text-muted" style="font-size:0.78rem;">' +
                            '<span>Đã chọn ' + m.claimedItemCount + ' món (' + formatVnd(m.claimedDishSubtotal) + ')</span>' +
                            '<span>VAT/Giảm giá: <strong class="font-monospace">' + adjText + '</strong></span>' +
                        '</div>' +
                        (m.unclaimedFallbackSubtotal > 0
                            ? '<div class="small text-warning-emphasis mt-1" style="font-size:0.75rem;">* Đang tạm giữ ' + formatVnd(m.unclaimedFallbackSubtotal) + ' món chưa ai nhận</div>'
                            : '');

                    membersContainer.appendChild(row);
                });
            }

            // Update Lock & Finalize controls
            const lockWrap = document.getElementById("stLockClaimControls");
            const lockBtn = document.getElementById("stLockClaimsBtn");
            if (lockWrap) {
                lockWrap.classList.toggle("d-none", !canLock);
            }
            if (lockBtn) {
                lockBtn.disabled = !board.allItemsClaimed || isLocked;
                lockBtn.title = board.allItemsClaimed
                    ? "Khóa nhận món và chốt số liệu vào sổ cái"
                    : "Cần phân bổ hết tất cả các món trước khi khóa";
            }
        }

        async function toggleDishClaim(receiptItemId) {
            if (activeHub && activeHub.state === signalR.HubConnectionState.Connected) {
                try {
                    const updatedBoard = await activeHub.invoke("ToggleReceiptItemClaim", String(tripId), String(expenseId), String(receiptItemId));
                    renderBoard(updatedBoard);
                    return;
                } catch (err) {
                    if (window.SplitTrip) {
                        window.SplitTrip.showToast("Không thể chọn món", err && err.message ? err.message : "Vui lòng thử lại.");
                    }
                    return;
                }
            }

            // Fallback via HTTP POST JSON if WebSocket is reconnecting
            const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
            const formData = new FormData();
            formData.append("tripId", tripId);
            formData.append("expenseId", expenseId);
            formData.append("receiptItemId", receiptItemId);
            if (tokenInput) formData.append("__RequestVerificationToken", tokenInput.value);

            const res = await fetch("/Trips/ReceiptClaim?handler=ToggleClaim", {
                method: "POST",
                headers: { "Accept": "application/json" },
                body: formData
            });
            if (res.ok) {
                const board = await res.json();
                renderBoard(board);
            }
        }

        // Bind initial server-rendered dish cards before SignalR finishes connecting
        document.querySelectorAll("#stClaimDishList [data-dish-item-id]").forEach(function (card) {
            if (card.classList.contains("is-locked")) return;
            card.addEventListener("click", function () {
                const itemId = card.getAttribute("data-dish-item-id");
                if (itemId) toggleDishClaim(itemId);
            });
        });

        if (window.SplitTrip && tripId) {
            window.SplitTrip.joinTripRoom(tripId)
                .then(function (conn) {
                    activeHub = conn;

                    conn.on("ReceiptClaimUpdated", function (board) {
                        if (!board || String(board.expenseId) !== String(expenseId)) return;
                        renderBoard(board);
                    });

                    conn.on("ReceiptClaimLocked", function (board) {
                        if (!board || String(board.expenseId) !== String(expenseId)) return;
                        renderBoard(board);
                        window.SplitTrip.showToast("Đã khóa hóa đơn", board.lastActionMessage || "Hóa đơn đã được chốt sổ cái.");
                    });
                })
                .catch(function () { });
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        initExpensesPage();
        initDraftReviewPage();
        initLiveClaimingBoard();
    });
})();
