// Tells the page that opened this popup how the step-up ended. Display only: that page asks the server before acting.
(() => {
    const element = document.getElementById("confirm-result");
    const flow = element?.dataset.flow;
    if (!flow || !window.opener) {
        return;
    }

    window.opener.postMessage({ type: "paymentops.confirm", flow, result: element.dataset.result }, location.origin);
    window.close();
})();
