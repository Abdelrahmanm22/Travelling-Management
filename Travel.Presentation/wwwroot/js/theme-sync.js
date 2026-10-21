/* Light / dark theme. The signed-in account's choice (AppUser.Theme) is the source of truth;
   localStorage["mt-theme"] is only a cache of it, for pages that have no user to ask.

   Loaded synchronously in <head>, right after the app-extras.css link, in both layouts:

   1. <html data-theme-source="account"> — _Layout rendered the account's theme server-side
      (data-theme + the matching #bootstrap-style / #app-style hrefs), so the page is already right.
      Just refresh the cache, so the login page shows this theme after sign-out.
   2. No source (the login page, an anonymous error page) — apply the cached theme before first paint.
   3. The topbar #theme-form: apply at once, save with fetch, revert if the save fails. With
      scripting off the form posts normally and AccountController.SetTheme redirects back.
   4. A change in one tab reaches the others through the `storage` event.

   The stylesheet names are repeated in Helpers/ThemeAssets.cs — change both together. Vendor
   back/assets/js/app.js still carries Skote's own switcher, but it only acts on the right-bar
   checkboxes, which no longer exist, so it never touches the hrefs. */
(function () {
    var KEY = 'mt-theme';
    var root = document.documentElement;

    function isTheme(value) {
        return value === 'light' || value === 'dark';
    }

    function readCache() {
        try { return window.localStorage.getItem(KEY); } catch (e) { return null; }
    }

    function writeCache(theme) {
        try { window.localStorage.setItem(KEY, theme); } catch (e) { /* storage blocked: the DB still has it */ }
    }

    function swapHref(id, pattern, file) {
        var link = document.getElementById(id);
        if (link) link.setAttribute('href', link.getAttribute('href').replace(pattern, file));
    }

    function syncButton(theme) {
        var button = document.getElementById('theme-toggle');
        if (!button) return;
        var label = theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode';
        button.value = theme === 'dark' ? 'Light' : 'Dark';
        button.setAttribute('aria-label', label);
        button.setAttribute('title', label);
    }

    // Visual only — sets the attribute app-extras.css keys its tokens on and points the two Skote
    // stylesheets at the matching files.
    function paint(theme) {
        var dark = theme === 'dark';
        root.setAttribute('data-theme', theme);
        swapHref('bootstrap-style', /bootstrap(-dark)?\.min\.css$/, dark ? 'bootstrap-dark.min.css' : 'bootstrap.min.css');
        swapHref('app-style', /app(-dark)?\.min\.css$/, dark ? 'app-dark.min.css' : 'app.min.css');
        syncButton(theme);
    }

    function apply(theme) {
        paint(theme);
        writeCache(theme);
    }

    if (root.getAttribute('data-theme-source') === 'account') {
        writeCache(root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light');
    } else {
        var cached = readCache();
        paint(isTheme(cached) ? cached : 'light');
    }

    window.addEventListener('storage', function (e) {
        if (e.key === KEY && isTheme(e.newValue) && e.newValue !== root.getAttribute('data-theme'))
            paint(e.newValue);
    });

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || form.id !== 'theme-form' || !window.fetch) return;
        e.preventDefault();

        var button = document.getElementById('theme-toggle');
        if (!button || button.disabled) return;

        var previous = root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
        var next = previous === 'dark' ? 'light' : 'dark';

        var body = new FormData(form);
        body.set('theme', next === 'dark' ? 'Dark' : 'Light');

        apply(next);
        button.disabled = true;   // one save in flight at a time, so responses can't land out of order

        fetch(form.action, {
            method: 'POST',
            body: body,
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (response) { return response.ok ? response.json() : { success: false }; })
            .catch(function () { return { success: false }; })   // network error, or a login page instead of JSON
            .then(function (result) {
                if (result && result.success) return;
                apply(previous);
                if (window.toastr) window.toastr.error((result && result.message) || 'Your theme could not be saved. Please try again.');
            })
            .then(function () { button.disabled = false; });
    });

    window.mtTheme = { apply: apply };
})();
