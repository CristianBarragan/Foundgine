// Highlights the current section in the sticky table of contents.
// No animation — just active-state tracking, safe under reduced-motion.
(function () {
  var links = Array.prototype.slice.call(document.querySelectorAll(".toc a"));
  if (!links.length) return;

  var targets = links
    .map(function (link) {
      var id = link.getAttribute("href").replace("#", "");
      return document.getElementById(id);
    })
    .filter(Boolean);

  if (!("IntersectionObserver" in window) || !targets.length) return;

  var observer = new IntersectionObserver(
    function (entries) {
      entries.forEach(function (entry) {
        var link = links.find(function (l) {
          return l.getAttribute("href") === "#" + entry.target.id;
        });
        if (!link) return;
        if (entry.isIntersecting) {
          links.forEach(function (l) {
            l.classList.remove("active");
          });
          link.classList.add("active");
        }
      });
    },
    { rootMargin: "-10% 0px -75% 0px" },
  );

  targets.forEach(function (t) {
    observer.observe(t);
  });
})();
