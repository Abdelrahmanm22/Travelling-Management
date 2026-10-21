/* Mirrors Skote's light/dark switch onto <html data-theme="light|dark"> so app-extras.css can
   redefine its --mt-* tokens per theme.

   back/assets/js/app.js (vendor, never edited) implements the switch by rewriting the href of
   #bootstrap-style / #app-style, and remembers the choice in sessionStorage["is_visited"]
   ("light-mode-switch" / "dark-mode-switch" / "rtl-mode-switch"). It sets no attribute and runs at
   the end of <body>, so this script runs synchronously in <head>, after both links:

   1. sets data-theme from the stored choice before the first paint;
   2. for a dark choice, points the two stylesheets at their dark files straight away, so the page
      doesn't paint light first (app.js later writes the same hrefs, a no-op);
   3. follows #app-style's href from then on, so every switch app.js makes — RTL included, which
      loads the light files — is reflected without listening to its checkboxes. */
(function () {
    var root = document.documentElement;
    var bootstrapStyle = document.getElementById('bootstrap-style');
    var appStyle = document.getElementById('app-style');

    function apply(dark) {
        root.setAttribute('data-theme', dark ? 'dark' : 'light');
    }

    var stored = null;
    try { stored = window.sessionStorage.getItem('is_visited'); } catch (e) { /* storage blocked */ }

    var dark = stored === 'dark-mode-switch';
    if (dark && bootstrapStyle && appStyle) {
        bootstrapStyle.setAttribute('href', bootstrapStyle.getAttribute('href').replace(/bootstrap\.min\.css$/, 'bootstrap-dark.min.css'));
        appStyle.setAttribute('href', appStyle.getAttribute('href').replace(/app\.min\.css$/, 'app-dark.min.css'));
    }
    apply(dark);

    if (appStyle && window.MutationObserver) {
        new MutationObserver(function () {
            apply(/app-dark(\.min)?\.css$/.test(appStyle.getAttribute('href') || ''));
        }).observe(appStyle, { attributes: true, attributeFilter: ['href'] });
    }
})();
