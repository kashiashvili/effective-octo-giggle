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

  // Copy-to-clipboard for the invite link. Without JS the link stays visible and selectable, so
  // nothing is lost — this just saves a manual select-and-copy.
  document.addEventListener("click", function (event) {
    var button = event.target.closest("[data-copy]");
    if (!button) return;

    var text = button.getAttribute("data-copy");
    var restore = function () {
      button.textContent = "Copy link";
    };
    var done = function () {
      button.textContent = "Copied ✓";
      window.setTimeout(restore, 1500);
    };

    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done, restore);
    } else {
      var input = button.parentElement.querySelector(".invite-input");
      if (input) { input.select(); try { document.execCommand("copy"); done(); } catch (e) { restore(); } }
    }
  });
})();
