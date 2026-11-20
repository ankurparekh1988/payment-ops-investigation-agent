// The signed-in page's side of the session: the person's own input counts as activity, and the confirm-it's-you
// window is opened by the browser's click handler, since one opened after a server round trip would be blocked.
window.poSession = (() => {
    const reportEvery = 30000;
    const pollEvery = 500;
    const activityEvents = ["keydown", "pointerdown"];

    // flow -> { ref, attempt }, one per ConfirmIdentity on this page.
    const confirms = new Map();
    let reporter = null;
    let lastReport = 0;
    let warning = false;

    function onActivity(event) {
        if (!event.isTrusted || !reporter) {
            return;
        }

        const now = Date.now();
        if (!warning && now - lastReport < reportEvery) {
            return;
        }

        lastReport = now;
        reporter.invokeMethodAsync("ReportActivity").then(
            live => {
                if (live === false) {
                    location.assign("/signed-out?reason=inactive");
                }
            },
            () => { });
    }

    function start(dotNetRef) {
        stop();
        reporter = dotNetRef;
        lastReport = 0;
        activityEvents.forEach(type => document.addEventListener(type, onActivity, true));
    }

    function stop() {
        activityEvents.forEach(type => document.removeEventListener(type, onActivity, true));
        reporter = null;
        warning = false;
    }

    function setWarning(on) {
        warning = on === true;
    }

    const windowName = flow => `paymentops-confirm-${flow}`;

    // A window opened by the fallback link has no reference here, so it is known by the name only this flow uses.
    function isPopup(source, popup, flow) {
        if (popup) {
            return source === popup;
        }

        try {
            return source !== null && source.opener === window && source.name === windowName(flow);
        } catch {
            return false;
        }
    }

    function accepts(event, popup, flow) {
        const data = event.data;
        return event.origin === location.origin
            && isPopup(event.source, popup, flow)
            && data !== null && typeof data === "object"
            && data.type === "paymentops.confirm"
            && data.flow === flow;
    }

    function clear(attempt) {
        if (attempt) {
            clearInterval(attempt.timer);
            window.removeEventListener("message", attempt.listener);
        }
    }

    function report(entry, flow, result) {
        entry.ref.invokeMethodAsync("OnConfirmResult", flow, result).catch(() => { });
    }

    function finish(flow, result) {
        const entry = confirms.get(flow);
        if (!entry || !entry.attempt) {
            return;
        }

        clear(entry.attempt);
        confirms.delete(flow);
        report(entry, flow, result);
    }

    function begin(entry, flow, popup) {
        clear(entry.attempt);
        const attempt = { popup, closedSeen: false };
        attempt.listener = event => {
            if (accepts(event, popup, flow)) {
                finish(flow, String(event.data.result));
            }
        };
        window.addEventListener("message", attempt.listener);
        if (popup) {
            // Closed is reported a poll late, so a message posted just before closing arrives first.
            attempt.timer = setInterval(() => {
                if (popup.closed) {
                    if (attempt.closedSeen) {
                        finish(flow, "closed");
                    }

                    attempt.closedSeen = true;
                }
            }, pollEvery);
        }

        entry.attempt = attempt;
    }

    function onClick(event) {
        const button = event.target instanceof Element ? event.target.closest("[data-po-confirm]") : null;
        const flow = button?.dataset.poConfirm;
        const entry = flow ? confirms.get(flow) : undefined;
        if (!entry) {
            return;
        }

        const open = entry.attempt?.popup;
        if (open && !open.closed) {
            open.focus();
            return;
        }

        // Synchronous, in the click's own tick: the browser allows a window opened from the person's click.
        const popup = window.open(`/account/confirm?flow=${flow}`, windowName(flow), "popup,width=520,height=640");
        begin(entry, flow, popup);
        if (!popup) {
            // Stays listening: the fallback link opens the same named window, which reports the same way.
            report(entry, flow, "blocked");
        }
    }

    function register(flow, dotNetRef) {
        const entry = confirms.get(flow);
        if (entry) {
            entry.ref = dotNetRef;
        } else {
            confirms.set(flow, { ref: dotNetRef, attempt: null });
        }
    }

    function cancel(flow) {
        const entry = confirms.get(flow);
        if (entry) {
            clear(entry.attempt);
            confirms.delete(flow);
        }
    }

    document.addEventListener("click", onClick);

    return { start, stop, warning: setWarning, register, cancel, accepts };
})();
