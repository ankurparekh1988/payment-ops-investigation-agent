// The signed-in page's side of the session: the person's own input counts as activity, and the confirm-it's-you
// window is opened by the browser's click handler, since one opened after a server round trip would be blocked.
window.poSession = (() => {
    const reportEvery = 30000;
    const pollEvery = 500;

    // Long enough to finish signing in at Entra; after that the person chooses Confirm again.
    const recheckFor = 10 * 60 * 1000;
    const activityEvents = ["keydown", "pointerdown"];

    // flow -> { ref, owner, attempt }, one per ConfirmIdentity on this page.
    const confirms = new Map();

    // owner -> { flow, listener, timer }: at most one per component, for the window it last saw closed. It outlives that
    // flow's entry, since the component moves to a new flow straight away.
    const rechecks = new Map();
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
            if (attempt.onReturn) {
                window.removeEventListener("focus", attempt.onReturn);
                document.removeEventListener("visibilitychange", attempt.onReturn);
            }
        }
    }

    // A link-opened window can't be polled, and its message can be lost; coming back to this tab makes the
    // component ask the server, which alone knows whether the sign-in happened.
    function watchReturn(attempt, flow) {
        if (attempt.onReturn) {
            return;
        }

        attempt.onReturn = () => {
            if (document.visibilityState === "visible") {
                finish(flow, "closed");
            }
        };
        window.addEventListener("focus", attempt.onReturn);
        document.addEventListener("visibilitychange", attempt.onReturn);
    }

    // Entra's pages can cut this tab off from the window, which then looks closed while the person is still signing in;
    // each return to this tab asks the component to re-read the server until it has nothing left to wait for.
    function recheckOnReturn(entry, flow) {
        const owner = entry.owner;
        dropRecheck(owner);
        let asking = false;
        const recheck = { flow };
        recheck.listener = () => {
            if (document.visibilityState !== "visible" || asking) {
                return;
            }

            asking = true;
            entry.ref.invokeMethodAsync("RecheckConfirmation", flow).then(
                done => {
                    asking = false;
                    if (done !== false) {
                        dropRecheck(owner, flow);
                    }
                },
                () => dropRecheck(owner, flow));
        };
        recheck.timer = setTimeout(() => dropRecheck(owner, flow), recheckFor);
        rechecks.set(owner, recheck);
        window.addEventListener("focus", recheck.listener);
        document.addEventListener("visibilitychange", recheck.listener);
    }

    // With a flow, only that flow's recheck: a later one for the same owner stays.
    function dropRecheck(owner, flow) {
        const recheck = rechecks.get(owner);
        if (recheck && (flow === undefined || recheck.flow === flow)) {
            window.removeEventListener("focus", recheck.listener);
            document.removeEventListener("visibilitychange", recheck.listener);
            clearTimeout(recheck.timer);
            rechecks.delete(owner);
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
        if (result === "closed" && entry.attempt.popup) {
            recheckOnReturn(entry, flow);
        }

        report(entry, flow, result);
    }

    // A new attempt supersedes the component's earlier closed window.
    function begin(entry, flow, popup) {
        clear(entry.attempt);
        dropRecheck(entry.owner);
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
        const link = event.target instanceof Element ? event.target.closest("[data-po-confirm-link]") : null;
        if (link) {
            const attempt = confirms.get(link.dataset.poConfirmLink)?.attempt;
            if (attempt && !attempt.popup) {
                watchReturn(attempt, link.dataset.poConfirmLink);
            }

            return;
        }

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

    function register(flow, dotNetRef, owner) {
        const entry = confirms.get(flow);
        if (entry) {
            entry.ref = dotNetRef;
        } else {
            confirms.set(flow, { ref: dotNetRef, owner, attempt: null });
        }
    }

    function cancel(flow) {
        const entry = confirms.get(flow);
        if (entry) {
            clear(entry.attempt);
            confirms.delete(flow);
        }
    }

    // Everything held for a component that has gone: its attempts and its recheck.
    function release(owner) {
        for (const [flow, entry] of confirms) {
            if (entry.owner === owner) {
                clear(entry.attempt);
                confirms.delete(flow);
            }
        }

        dropRecheck(owner);
    }

    // For checking from the console that nothing is left behind.
    function pending() {
        return { confirms: confirms.size, rechecks: rechecks.size };
    }

    document.addEventListener("click", onClick);

    return { start, stop, warning: setWarning, register, cancel, release, pending, accepts };
})();
