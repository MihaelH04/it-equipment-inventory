// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Zajednički helperi za autocomplete, TAB odabir, export linkove, gumb za vrh stranice i tipkovničke prečace.
(function () {
    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function createSuggestionButton(text, index, onPick) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'list-group-item list-group-item-action autocomplete-suggestion-item';
        button.dataset.index = index;
        button.textContent = text;
        button.addEventListener('mousedown', function (e) {
            e.preventDefault();
            onPick(index);
        });
        return button;
    }

    function setActiveSuggestion(suggestions, activeIndex) {
        const buttons = suggestions.querySelectorAll('.autocomplete-suggestion-item');
        buttons.forEach(button => button.classList.remove('active'));

        if (activeIndex >= 0 && activeIndex < buttons.length) {
            buttons[activeIndex].classList.add('active');
            buttons[activeIndex].scrollIntoView({ block: 'nearest' });
        }
    }

    function showEmptyMessage(suggestions) {
        suggestions.innerHTML = '<div class="list-group-item text-muted">Nema rezultata</div>';
    }

    function normalizeText(value) {
        return String(value ?? '')
            .trim()
            .toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '');
    }

    function dispatchPickedEvent(input, item) {
        input.dispatchEvent(new CustomEvent('autocomplete:picked', { detail: item }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    window.setupRemoteEntityAutocomplete = function (inputId, hiddenId, suggestionsId, url, options) {
        const input = document.getElementById(inputId);
        const hidden = hiddenId ? document.getElementById(hiddenId) : null;
        const suggestions = document.getElementById(suggestionsId);
        const settings = Object.assign({ minLength: 1, debounceMs: 180, hideDelayMs: 900, onSelect: null, clearHiddenOnInput: true }, options || {});

        if (!input || !suggestions || input.dataset.autocompleteReady === '1') return;
        input.dataset.autocompleteReady = '1';

        let debounceTimer = null;
        let hideTimer = null;
        let activeIndex = -1;
        let currentItems = [];
        let lastRequestId = 0;

        function closeSuggestions() {
            suggestions.innerHTML = '';
            currentItems = [];
            activeIndex = -1;
        }

        function pick(index) {
            const item = currentItems[index];
            if (!item) return false;

            input.value = item.value ?? item.searchValue ?? item.text;
            if (hidden) hidden.value = item.id ?? '';
            closeSuggestions();

            if (typeof settings.onSelect === 'function') {
                settings.onSelect(item);
            }

            dispatchPickedEvent(input, item);
            return true;
        }

        function render(items) {
            currentItems = Array.isArray(items) ? items : [];
            activeIndex = currentItems.length > 0 ? 0 : -1;
            suggestions.innerHTML = '';

            if (currentItems.length === 0) {
                showEmptyMessage(suggestions);
                return;
            }

            currentItems.forEach((item, index) => {
                suggestions.appendChild(createSuggestionButton(item.text, index, pick));
            });

            setActiveSuggestion(suggestions, activeIndex);
        }

        async function loadSuggestions(term) {
            const cleanTerm = (term || '').trim();
            if (cleanTerm.length < settings.minLength) {
                closeSuggestions();
                return;
            }

            const requestId = ++lastRequestId;
            try {
                const response = await fetch(`${url}?term=${encodeURIComponent(cleanTerm)}`, {
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                if (!response.ok) return;

                const data = await response.json();
                if (requestId !== lastRequestId) return;

                render(data);
            } catch (error) {
                console.error(error);
            }
        }

        input.addEventListener('input', function () {
            if (hidden && settings.clearHiddenOnInput) hidden.value = '';
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => loadSuggestions(input.value), settings.debounceMs);
        });

        input.addEventListener('focus', function () {
            clearTimeout(hideTimer);
            loadSuggestions(input.value);
        });

        input.addEventListener('keydown', function (e) {
            if (currentItems.length === 0) {
                if (e.key === 'Escape') closeSuggestions();
                return;
            }

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                activeIndex = Math.min(activeIndex + 1, currentItems.length - 1);
                setActiveSuggestion(suggestions, activeIndex);
            }
            else if (e.key === 'ArrowUp') {
                e.preventDefault();
                activeIndex = Math.max(activeIndex - 1, 0);
                setActiveSuggestion(suggestions, activeIndex);
            }
            else if (e.key === 'Enter' || e.key === 'Tab') {
                e.preventDefault();
                pick(activeIndex >= 0 ? activeIndex : 0);
            }
            else if (e.key === 'Escape') {
                e.preventDefault();
                closeSuggestions();
            }
        });

        input.addEventListener('blur', function () {
            clearTimeout(hideTimer);
            hideTimer = setTimeout(closeSuggestions, settings.hideDelayMs);
        });

        document.addEventListener('mousedown', function (e) {
            if (!suggestions.contains(e.target) && e.target !== input) {
                closeSuggestions();
            }
        });
    };

    window.setupLocalTextAutocomplete = function (inputId, suggestionsId, items, options) {
        const input = document.getElementById(inputId);
        const suggestions = document.getElementById(suggestionsId);
        const settings = Object.assign({ hideDelayMs: 900, maxItems: 10, onSelect: null }, options || {});

        if (!input || !suggestions || !Array.isArray(items) || input.dataset.localAutocompleteReady === '1') return;
        input.dataset.localAutocompleteReady = '1';

        let activeIndex = -1;
        let currentItems = [];
        let hideTimer = null;

        function closeSuggestions() {
            suggestions.innerHTML = '';
            currentItems = [];
            activeIndex = -1;
        }

        function pick(index) {
            const item = currentItems[index];
            if (!item) return false;
            input.value = item.value ?? item.text;
            closeSuggestions();

            if (typeof settings.onSelect === 'function') {
                settings.onSelect(item);
            }

            dispatchPickedEvent(input, item);
            return true;
        }

        function render() {
            const term = normalizeText(input.value);
            currentItems = items
                .map(x => typeof x === 'string' ? { text: x, value: x } : x)
                .filter(x => !term || normalizeText(x.text).includes(term) || normalizeText(x.value).includes(term))
                .slice(0, settings.maxItems);

            activeIndex = currentItems.length > 0 ? 0 : -1;
            suggestions.innerHTML = '';

            if (currentItems.length === 0) {
                showEmptyMessage(suggestions);
                return;
            }

            currentItems.forEach((item, index) => {
                suggestions.appendChild(createSuggestionButton(item.text, index, pick));
            });

            setActiveSuggestion(suggestions, activeIndex);
        }

        input.addEventListener('focus', function () {
            clearTimeout(hideTimer);
            render();
        });

        input.addEventListener('input', render);

        input.addEventListener('keydown', function (e) {
            if (currentItems.length === 0) {
                if (e.key === 'Escape') closeSuggestions();
                return;
            }

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                activeIndex = Math.min(activeIndex + 1, currentItems.length - 1);
                setActiveSuggestion(suggestions, activeIndex);
            }
            else if (e.key === 'ArrowUp') {
                e.preventDefault();
                activeIndex = Math.max(activeIndex - 1, 0);
                setActiveSuggestion(suggestions, activeIndex);
            }
            else if (e.key === 'Enter' || e.key === 'Tab') {
                e.preventDefault();
                pick(activeIndex >= 0 ? activeIndex : 0);
            }
            else if (e.key === 'Escape') {
                e.preventDefault();
                closeSuggestions();
            }
        });

        input.addEventListener('blur', function () {
            clearTimeout(hideTimer);
            hideTimer = setTimeout(closeSuggestions, settings.hideDelayMs);
        });
    };

    window.setupExportLinksFromForms = function () {
        document.querySelectorAll('[data-export-form][data-export-base-url]').forEach(link => {
            if (link.dataset.exportReady === '1') return;
            link.dataset.exportReady = '1';

            function updateHref() {
                const form = document.getElementById(link.dataset.exportForm);
                const baseUrl = link.dataset.exportBaseUrl;
                if (!form || !baseUrl) return;

                const params = new URLSearchParams(new FormData(form));
                link.href = `${baseUrl}?${params.toString()}`;
            }

            link.addEventListener('click', updateHref);
            const form = document.getElementById(link.dataset.exportForm);
            if (form) {
                form.addEventListener('change', updateHref);
                form.addEventListener('input', updateHref);
            }
            updateHref();
        });
    };

    function isEditableTarget(target) {
        if (!target) return false;
        const tagName = target.tagName;
        return target.isContentEditable || tagName === 'INPUT' || tagName === 'TEXTAREA' || tagName === 'SELECT';
    }

    function focusFirstSearch() {
        const search = document.querySelector('[data-main-search="true"], input[name="searchString"], input[id*="Search" i], input[placeholder*="Pretra" i]');
        if (search) {
            search.focus();
            if (typeof search.select === 'function') search.select();
            return true;
        }
        return false;
    }

    function clickFirstVisible(selector) {
        const elements = Array.from(document.querySelectorAll(selector));
        const element = elements.find(el => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length));
        if (element) {
            element.click();
            return true;
        }
        return false;
    }

    function setupBackToTopButton() {
        const button = document.getElementById('backToTopBtn') || document.getElementById('scrollToTopBtn');
        if (!button || button.dataset.backToTopReady === '1') return;

        button.dataset.backToTopReady = '1';

        function getScrollTop() {
            return window.scrollY || document.documentElement.scrollTop || document.body.scrollTop || 0;
        }

        function syncVisibility() {
            button.classList.toggle('show', getScrollTop() > 160);
        }

        button.addEventListener('click', function () {
            window.scrollTo({ top: 0, behavior: 'smooth' });
            document.documentElement.scrollTo({ top: 0, behavior: 'smooth' });
            document.body.scrollTo({ top: 0, behavior: 'smooth' });
        });

        window.addEventListener('scroll', syncVisibility, { passive: true });
        document.addEventListener('scroll', syncVisibility, { passive: true, capture: true });
        setTimeout(syncVisibility, 100);
        setTimeout(syncVisibility, 500);
        syncVisibility();
    }

    function setupKeyboardShortcuts() {
        document.addEventListener('keydown', function (e) {
            const key = e.key;
            const keyLower = key.toLowerCase();

            if (e.altKey && !e.ctrlKey && !e.shiftKey && /^[1-9]$/.test(key)) {
                const navLink = document.querySelector(`[data-nav-shortcut="Alt+${key}"]`);
                if (navLink) {
                    e.preventDefault();
                    navLink.click();
                    return;
                }
            }

            if ((key === 'F7') || (e.altKey && !e.ctrlKey && keyLower === 's')) {
                e.preventDefault();
                focusFirstSearch();
                return;
            }

            if ((key === 'F8') || (e.altKey && !e.ctrlKey && keyLower === 'e')) {
                if (!isEditableTarget(e.target)) {
                    e.preventDefault();
                    clickFirstVisible('[data-export-form], .app-export-excel');
                    return;
                }
            }

            if ((key === 'F9') || (e.altKey && !e.ctrlKey && keyLower === 'n')) {
                if (!isEditableTarget(e.target)) {
                    e.preventDefault();
                    clickFirstVisible('[data-add-new], a[href$="/Create"]');
                    return;
                }
            }

            if (e.ctrlKey && key === 'Enter') {
                const form = document.activeElement?.closest('form') || document.querySelector('form');
                if (form) {
                    e.preventDefault();
                    form.requestSubmit ? form.requestSubmit() : form.submit();
                    return;
                }
            }

            if (key === 'Escape' && isEditableTarget(e.target)) {
                const target = e.target;
                if (target.matches('[data-main-search="true"], input[name="searchString"]')) {
                    target.value = '';
                    target.dispatchEvent(new Event('input', { bubbles: true }));
                }
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        setupBackToTopButton();
        setupKeyboardShortcuts();
        window.setupExportLinksFromForms();
    });
})();

// Svijetla/tamna tema - pamti izbor u browseru.
document.addEventListener('DOMContentLoaded', function () {
    const button = document.getElementById('themeToggleBtn');
    const icon = button ? button.querySelector('.theme-toggle-icon') : null;

    function currentTheme() {
        return document.documentElement.getAttribute('data-theme') || 'light';
    }

    function setTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        try { localStorage.setItem('app-theme', theme); } catch (_) { }
        if (icon) icon.textContent = theme === 'dark' ? '☀️' : '🌙';
    }

    setTheme(currentTheme());

    if (button) {
        button.addEventListener('click', function () {
            setTheme(currentTheme() === 'dark' ? 'light' : 'dark');
        });
    }
});
