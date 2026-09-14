(function () {
  "use strict";

  var tabs = Array.prototype.slice.call(document.querySelectorAll(".ajax-tab"));
  var panel = document.getElementById("process-panel");
  var cache = {};

  function escapeHtml(value) {
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#039;");
  }

  function pretty(value) {
    return JSON.stringify(value, null, 2);
  }

  function renderStep(step) {
    return (
      '<article class="process-step">' +
      '<div class="process-step-marker">' +
      escapeHtml(step.number) +
      "</div>" +
      '<div class="process-step-body">' +
      '<div class="process-step-heading"><span class="eyebrow">Step ' +
      escapeHtml(step.number) +
      "</span><h2>" +
      escapeHtml(step.name) +
      "</h2></div>" +
      '<p class="process-boundary">' +
      escapeHtml(step.boundary) +
      "</p>" +
      "<p>" +
      escapeHtml(step.why) +
      "</p>" +
      '<div class="payload-grid">' +
      '<div class="payload-card"><div class="payload-label">Incoming payload</div><pre><code>' +
      escapeHtml(pretty(step.input)) +
      "</code></pre></div>" +
      '<div class="payload-card"><div class="payload-label">Produced payload</div><pre><code>' +
      escapeHtml(pretty(step.output)) +
      "</code></pre></div>" +
      "</div>" +
      "</div>" +
      "</article>"
    );
  }

  function renderProcess(data) {
    panel.innerHTML =
      '<div class="process-header">' +
      '<span class="eyebrow">Selected walkthrough</span>' +
      "<h2>" +
      escapeHtml(data.title) +
      "</h2>" +
      "<p>" +
      escapeHtml(data.summary) +
      "</p>" +
      '<div class="process-meta"><span><strong>Request</strong>' +
      escapeHtml(data.question) +
      "</span><span><strong>Shape</strong>" +
      escapeHtml(data.fixture) +
      "</span></div>" +
      "</div>" +
      '<div class="process-rail">' +
      data.steps
        .map(function (step) {
          return "<span>" + escapeHtml(step.number) + "</span>";
        })
        .join('<i aria-hidden="true">→</i>') +
      "</div>" +
      '<div class="process-steps">' +
      data.steps.map(renderStep).join("") +
      "</div>" +
      '<div class="process-takeaway"><strong>The key idea:</strong> the caller expresses intent once. Foundgine carries that intent through semantic resolution, validation, authorization, planning and provider execution without handing the caller direct control of the database.</div>';

    if (window.location.hash === "#process-panel") {
      panel.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  }

  function loadProcess(name) {
    panel.innerHTML =
      '<div class="process-loading"><span class="loading-dot"></span> Loading ' +
      escapeHtml(name) +
      " execution walkthrough…</div>";

    if (cache[name]) {
      renderProcess(cache[name]);
      return;
    }

    fetch("../assets/processes/" + encodeURIComponent(name) + ".json", {
      headers: { Accept: "application/json" },
      cache: "no-cache",
    })
      .then(function (response) {
        if (!response.ok) throw new Error("HTTP " + response.status);
        return response.json();
      })
      .then(function (data) {
        cache[name] = data;
        renderProcess(data);
      })
      .catch(function (error) {
        panel.innerHTML =
          '<div class="scenario scenario--deny"><span class="scenario-badge">Unable to load</span><p>The walkthrough could not be loaded. Refresh the page and try again.</p><pre>' +
          escapeHtml(error.message) +
          "</pre></div>";
      });
  }

  tabs.forEach(function (tab) {
    tab.addEventListener("click", function () {
      var name = tab.getAttribute("data-process");
      tabs.forEach(function (item) {
        var active = item === tab;
        item.classList.toggle("is-active", active);
        item.setAttribute("aria-selected", active ? "true" : "false");
      });
      loadProcess(name);
    });
  });

  loadProcess("simple");
})();
