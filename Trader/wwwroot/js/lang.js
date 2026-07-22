// Language-preference cookie helpers, called from Blazor via IJSRuntime.
// Cookie is used (not localStorage) so it survives full-page reloads
// and is readable during SSR/interactive modes.

window.traderLang = {
    get: function () {
        const m = document.cookie.match(/(?:^|;\s*)traderLang=([^;]+)/);
        return m ? decodeURIComponent(m[1]) : null;
    },
    set: function (lang) {
        // 1 year, SameSite=Lax, works over http (dev) and https (prod).
        const maxAge = 60 * 60 * 24 * 365;
        document.cookie = `traderLang=${encodeURIComponent(lang)}; path=/; max-age=${maxAge}; SameSite=Lax`;
    }
};
