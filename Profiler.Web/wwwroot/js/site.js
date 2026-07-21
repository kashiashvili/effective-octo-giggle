// Progressive enhancement: give slow form posts (connecting sources fetches remote data)
// visible feedback and prevent accidental double-submits. Everything still works without JS.
(function () {
  "use strict";

  document.addEventListener("submit", function (event) {
    var form = event.target;
    if (!(form instanceof HTMLFormElement)) return;

    var button = form.querySelector('button[type="submit"], input[type="submit"]');
    if (!button || button.hasAttribute("data-no-busy")) return;

    // Defer so the button's value is still serialized into the request before we disable it.
    window.setTimeout(function () {
      button.disabled = true;
      button.classList.add("is-busy");
      if (button.tagName === "BUTTON") {
        button.setAttribute("data-original-html", button.innerHTML);
        button.innerHTML = "Working…";
      }
    }, 0);
  });
})();
