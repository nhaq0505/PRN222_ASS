(function () {
    "use strict";

    function formatVnd(amount) {
        const n = Number(amount) || 0;
        return n.toLocaleString("vi-VN") + "đ";
    }

    function copyTextWithFeedback(text, btn, toastTitle) {
        if (!text || !navigator.clipboard) return;
        const originalLabel = btn ? btn.textContent : "";
        navigator.clipboard.writeText(text).then(function () {
            if (btn) {
                btn.textContent = "Đã chép!";
                setTimeout(function () {
                    btn.textContent = originalLabel;
                }, 1800);
            }
            if (window.SplitTrip) {
                window.SplitTrip.showToast(toastTitle || "Đã sao chép", text);
            }
        }).catch(function () { });
    }

    document.addEventListener("DOMContentLoaded", function () {
        const root = document.getElementById("stSettlementWorkspace");
        if (!root) return;

        const tripId = root.getAttribute("data-trip-id");
        let pendingLiveRefresh = false;

        // Copy invite code or copyable fields
        document.addEventListener("click", function (e) {
            const copyBtn = e.target.closest && e.target.closest("[data-copy-value]");
            if (copyBtn) {
                e.preventDefault();
                const value = copyBtn.getAttribute("data-copy-value") || "";
                const label = copyBtn.getAttribute("data-copy-label") || "Đã sao chép";
                copyTextWithFeedback(value, copyBtn, label);
                return;
            }

            // Open VietQR Modal
            const qrTrigger = e.target.closest && e.target.closest("[data-open-vietqr]");
            if (qrTrigger) {
                e.preventDefault();
                openVietQrModal(qrTrigger);
                return;
            }

            // Open Dispute Modal
            const disputeTrigger = e.target.closest && e.target.closest("[data-open-dispute]");
            if (disputeTrigger) {
                e.preventDefault();
                openDisputeModal(disputeTrigger);
            }
        });

        function openVietQrModal(btn) {
            const modalEl = document.getElementById("stVietQrModal");
            if (!modalEl || typeof bootstrap === "undefined") return;

            const settlementId = btn.getAttribute("data-settlement-id") || "";
            const hasBank = btn.getAttribute("data-has-bank") === "true";
            const qrUrl = btn.getAttribute("data-qr-url") || "";
            const bankName = btn.getAttribute("data-bank-name") || "Ngân hàng thụ hưởng";
            const bankBin = btn.getAttribute("data-bank-bin") || "";
            const accountNo = btn.getAttribute("data-account-no") || "";
            const accountHolder = btn.getAttribute("data-account-holder") || "";
            const amount = btn.getAttribute("data-amount") || "0";
            const transferNote = btn.getAttribute("data-transfer-note") || "";
            const creditorName = btn.getAttribute("data-creditor-name") || "";
            const missingMsg = btn.getAttribute("data-missing-msg") || "Người nhận chưa cấu hình tài khoản ngân hàng.";
            const canMarkPaid = btn.getAttribute("data-can-mark-paid") === "true";

            const creditorEl = document.getElementById("stQrCreditorName");
            if (creditorEl) creditorEl.textContent = creditorName;

            const bankBadgeEl = document.getElementById("stQrBankBadge");
            if (bankBadgeEl) {
                bankBadgeEl.textContent = bankBin ? (bankName + " · BIN " + bankBin) : bankName;
            }

            const qrConfiguredBox = document.getElementById("stQrConfiguredBox");
            const qrMissingBox = document.getElementById("stQrMissingBox");
            const qrMissingText = document.getElementById("stQrMissingText");
            const qrImg = document.getElementById("stQrImage");
            const qrImgFallback = document.getElementById("stQrImgFallback");

            if (hasBank && qrUrl) {
                if (qrConfiguredBox) qrConfiguredBox.classList.remove("d-none");
                if (qrMissingBox) qrMissingBox.classList.add("d-none");
                if (qrImgFallback) qrImgFallback.classList.add("d-none");
                if (qrImg) {
                    qrImg.classList.remove("d-none");
                    qrImg.onerror = function () {
                        qrImg.classList.add("d-none");
                        if (qrImgFallback) qrImgFallback.classList.remove("d-none");
                    };
                    qrImg.src = qrUrl;
                }
            } else {
                if (qrConfiguredBox) qrConfiguredBox.classList.add("d-none");
                if (qrMissingBox) qrMissingBox.classList.remove("d-none");
                if (qrMissingText) qrMissingText.textContent = missingMsg;
            }

            setModalField("stQrAccountNo", "stQrCopyAccountBtn", accountNo || "Chưa cập nhật");
            setModalField("stQrAccountHolder", null, accountHolder || creditorName);
            setModalField("stQrAmountText", "stQrCopyAmountBtn", formatVnd(amount), String(Math.trunc(Number(amount) || 0)));
            setModalField("stQrTransferNote", "stQrCopyNoteBtn", transferNote);

            const markPaidForm = document.getElementById("stModalMarkPaidForm");
            const settlementInput = document.getElementById("stModalSettlementId");
            if (settlementInput) settlementInput.value = settlementId;
            if (markPaidForm) {
                markPaidForm.classList.toggle("d-none", !canMarkPaid);
            }

            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        }

        function setModalField(textId, copyBtnId, displayValue, rawCopyValue) {
            const el = document.getElementById(textId);
            if (el) el.textContent = displayValue || "";
            if (copyBtnId) {
                const btn = document.getElementById(copyBtnId);
                if (btn) {
                    btn.setAttribute("data-copy-value", rawCopyValue !== undefined ? rawCopyValue : (displayValue || ""));
                }
            }
        }

        function openDisputeModal(btn) {
            const modalEl = document.getElementById("stDisputeModal");
            if (!modalEl || typeof bootstrap === "undefined") return;

            const settlementId = btn.getAttribute("data-settlement-id") || "";
            const debtorName = btn.getAttribute("data-debtor-name") || "thành viên";
            const amount = btn.getAttribute("data-amount") || "0";

            const idInput = document.getElementById("stDisputeSettlementId");
            if (idInput) idInput.value = settlementId;

            const summaryEl = document.getElementById("stDisputeSummary");
            if (summaryEl) {
                summaryEl.textContent = "Khoản " + formatVnd(amount) + " từ " + debtorName;
            }

            const reasonInput = document.getElementById("stDisputeReasonInput");
            if (reasonInput) reasonInput.value = "";

            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        }

        async function refreshSettlementDomLive(toastTitle, toastMsg) {
            if (toastMsg && window.SplitTrip) {
                window.SplitTrip.showToast(toastTitle || "Đồng bộ tất toán", toastMsg);
            }

            // Defer DOM morph if user currently has VietQR or Dispute modal open
            if (document.querySelector(".modal.show")) {
                pendingLiveRefresh = true;
                return;
            }

            try {
                const response = await fetch(window.location.href, {
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });
                if (!response.ok) return;

                const html = await response.text();
                const doc = new DOMParser().parseFromString(html, "text/html");

                const nextDynamic = doc.getElementById("stSettlementDynamicZone");
                const currentDynamic = document.getElementById("stSettlementDynamicZone");
                if (nextDynamic && currentDynamic) {
                    currentDynamic.innerHTML = nextDynamic.innerHTML;
                }

                const nextBadge = doc.getElementById("stTripStatusBadge");
                const currentBadge = document.getElementById("stTripStatusBadge");
                if (nextBadge && currentBadge) {
                    currentBadge.className = nextBadge.className;
                    currentBadge.textContent = nextBadge.textContent;
                }

                const nextOwnerControls = doc.getElementById("stOwnerLifecycleStrip");
                const currentOwnerControls = document.getElementById("stOwnerLifecycleStrip");
                if (nextOwnerControls && currentOwnerControls) {
                    currentOwnerControls.innerHTML = nextOwnerControls.innerHTML;
                }
            } catch (err) {
                // Ignore transient reload error
            }
        }

        document.querySelectorAll(".modal").forEach(function (modalEl) {
            modalEl.addEventListener("hidden.bs.modal", function () {
                if (pendingLiveRefresh) {
                    pendingLiveRefresh = false;
                    refreshSettlementDomLive();
                }
            });
        });

        if (window.SplitTrip && tripId) {
            window.SplitTrip.joinTripRoom(tripId)
                .then(function (conn) {
                    conn.on("SettlementUpdated", function (payload) {
                        refreshSettlementDomLive(
                            "Cập nhật tất toán VietQR",
                            payload && payload.message ? payload.message : "Trạng thái tất toán vừa cập nhật trực tiếp.");
                    });
                    conn.on("TripCompleted", function (payload) {
                        refreshSettlementDomLive(
                            "Chuyến đi hoàn tất!",
                            payload && payload.message ? payload.message : "Tất cả công nợ đã tất toán 0đ!");
                    });
                    conn.on("TripStatusChanged", function (payload) {
                        refreshSettlementDomLive(
                            "Trạng thái chuyến đi",
                            payload && payload.message ? payload.message : "Trạng thái chuyến đi vừa thay đổi.");
                    });
                    conn.on("ExpenseAdded", function () {
                        refreshSettlementDomLive("Chi tiêu mới", "Kế hoạch tất toán Min-Cash-Flow vừa được tính lại.");
                    });
                    conn.on("ExpenseUpdated", function () {
                        refreshSettlementDomLive("Cập nhật chi tiêu", "Số dư công nợ vừa được tính lại.");
                    });
                    conn.on("ExpenseDeleted", function () {
                        refreshSettlementDomLive("Xóa chi tiêu", "Số dư công nợ vừa được tính lại.");
                    });
                    conn.on("TripMembersChanged", function () {
                        refreshSettlementDomLive("Thành viên chuyến đi", "Danh sách thành viên vừa thay đổi.");
                    });
                })
                .catch(function () { });
        }
    });
})();
