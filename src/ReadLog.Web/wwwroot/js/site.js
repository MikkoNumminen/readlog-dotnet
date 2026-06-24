// Unobtrusive confirmation for destructive actions. Any form carrying a
// data-confirm="message" attribute prompts before it submits. Keeping this out of inline
// markup lets the Content-Security-Policy use a strict script-src 'self' (no unsafe-inline).
(function () {
    "use strict";
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (form instanceof HTMLFormElement && form.hasAttribute("data-confirm")) {
            if (!window.confirm(form.getAttribute("data-confirm"))) {
                event.preventDefault();
            }
        }
    });
})();
