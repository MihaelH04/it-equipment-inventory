/* IT Equipment Inventory — vizualne interakcije bez promjene poslovne logike. */
(function () {
    'use strict';

    const reducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function setupTopbar() {
        const topbar = document.querySelector('.app-topbar, .topbar');
        if (!topbar) return;

        const sync = () => topbar.classList.toggle('is-scrolled', window.scrollY > 8);
        window.addEventListener('scroll', sync, { passive: true });
        sync();
    }

    function setupRevealAnimations() {
        const selectors = [
            '.hero-panel-v4',
            '.dashboard-card-v4',
            '.mini-stat-card-v4',
            '.stat-card',
            '.consumables-stat-card',
            '.consumable-image-card',
            '.history-summary-card',
            '.soft-card',
            '.single-search-card',
            '.history-filter-card',
            '.profile-card',
            '.consumable-form-card',
            '.recycle-group',
            '.empty-state-modern',
            '.admin-page-header',
            '.consumables-page-header',
            '.consumable-form-header',
            '.user-dashboard-card-v4'
        ];

        const items = Array.from(document.querySelectorAll(selectors.join(',')))
            .filter((item, index, all) => all.indexOf(item) === index);

        if (!items.length) return;

        items.forEach((item, index) => {
            item.classList.add('reveal-item');
            item.style.setProperty('--rd-reveal-delay', `${Math.min(index % 6, 5) * 55}ms`);
        });

        if (reducedMotion || !('IntersectionObserver' in window)) {
            items.forEach(item => item.classList.add('is-visible'));
            return;
        }

        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            });
        }, { rootMargin: '0px 0px -6% 0px', threshold: 0.06 });

        items.forEach(item => observer.observe(item));
    }

    function setupButtonRipples() {
        if (reducedMotion) return;

        document.addEventListener('pointerdown', event => {
            const button = event.target.closest('.btn, .menu-toggle-btn, .theme-toggle-btn, .topbar-icon-btn, .consumable-image-card-button');
            if (!button || button.disabled) return;

            const rect = button.getBoundingClientRect();
            const ripple = document.createElement('span');
            ripple.className = 'rd-ripple';
            ripple.style.left = `${event.clientX - rect.left}px`;
            ripple.style.top = `${event.clientY - rect.top}px`;
            button.appendChild(ripple);
            ripple.addEventListener('animationend', () => ripple.remove(), { once: true });
        });
    }

    function parseNumericText(text) {
        const normalized = String(text).trim().replace(/\s+/g, '').replace(',', '.');
        if (!/^-?\d+(?:\.\d+)?$/.test(normalized)) return null;
        const value = Number(normalized);
        return Number.isFinite(value) ? value : null;
    }

    function setupNumberAnimations() {
        if (reducedMotion) return;

        const elements = Array.from(document.querySelectorAll(
            '.dashboard-value-v4, .stat-number, .stat-broj, .consumables-stat-value, .history-summary-value'
        ));

        const animate = element => {
            if (element.dataset.rdAnimated === '1') return;
            const target = parseNumericText(element.textContent);
            if (target === null || Math.abs(target) > 1000000) return;

            element.dataset.rdAnimated = '1';
            const decimals = String(element.textContent).includes(',') || String(element.textContent).includes('.') ? 1 : 0;
            const duration = 620;
            const start = performance.now();

            const frame = now => {
                const progress = Math.min((now - start) / duration, 1);
                const eased = 1 - Math.pow(1 - progress, 4);
                const current = target * eased;
                element.textContent = decimals
                    ? current.toLocaleString('hr-HR', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })
                    : Math.round(current).toLocaleString('hr-HR');

                if (progress < 1) requestAnimationFrame(frame);
            };

            requestAnimationFrame(frame);
        };

        if (!('IntersectionObserver' in window)) {
            elements.forEach(animate);
            return;
        }

        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                animate(entry.target);
                observer.unobserve(entry.target);
            });
        }, { threshold: 0.45 });

        elements.forEach(element => observer.observe(element));
    }

    function setupNavigationProgress() {
        const progress = document.createElement('div');
        progress.className = 'rd-navigation-progress';
        progress.setAttribute('aria-hidden', 'true');
        document.body.appendChild(progress);

        const start = () => progress.classList.add('is-active');
        const stop = () => {
            progress.classList.remove('is-active');
            document.querySelectorAll('form.is-busy').forEach(form => form.classList.remove('is-busy'));
        };

        const isFileDownload = (element, destination) => {
            if (element?.dataset.fileDownload === 'true' || element?.classList.contains('app-export-excel'))
                return true;

            const path = destination?.pathname || '';
            return /\/(ExportExcel|ExportHistoryExcel|GeneratePdf)\/?$/i.test(path);
        };

        window.addEventListener('pageshow', stop);
        window.addEventListener('focus', () => window.setTimeout(stop, 150));
        window.addEventListener('beforeunload', start);

        document.addEventListener('click', event => {
            const link = event.target.closest('a[href]');
            if (!link || event.defaultPrevented || event.button !== 0) return;
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            if (link.hasAttribute('download') || link.target === '_blank') return;
            if (link.dataset.bsToggle || link.dataset.bsDismiss) return;

            const href = link.getAttribute('href');
            if (!href || href.startsWith('#') || href.startsWith('javascript:') || href.startsWith('mailto:') || href.startsWith('tel:')) return;

            let destination;
            try { destination = new URL(link.href, window.location.href); }
            catch (_) { return; }
            if (destination.origin !== window.location.origin) return;
            if (isFileDownload(link, destination)) return;

            // Pričekaj da postojeći AJAX handleri dobiju priliku pozvati preventDefault().
            window.setTimeout(() => {
                if (!event.defaultPrevented) start();
            }, 0);
        });

        document.addEventListener('submit', event => {
            window.setTimeout(() => {
                if (event.defaultPrevented) return;
                const form = event.target;
                if (!(form instanceof HTMLFormElement)) return;
                let destination;
                try { destination = new URL(form.action || window.location.href, window.location.href); }
                catch (_) { destination = null; }
                if (isFileDownload(form, destination)) return;
                form.classList.add('is-busy');
                start();
            }, 0);
        });
    }

    function setupDismissibleAlerts() {
        document.querySelectorAll('.alert:not(.validation-summary-errors)').forEach(alert => {
            if (alert.querySelector('.btn-close, .rd-alert-close')) return;

            const close = document.createElement('button');
            close.type = 'button';
            close.className = 'rd-alert-close';
            close.setAttribute('aria-label', 'Zatvori poruku');
            close.textContent = '×';
            close.addEventListener('click', () => {
                alert.style.transition = 'opacity .2s ease, transform .2s ease';
                alert.style.opacity = '0';
                alert.style.transform = 'translateY(-6px)';
                window.setTimeout(() => alert.remove(), 210);
            });
            alert.appendChild(close);
        });
    }

    function setupThemeAccessibility() {
        const button = document.getElementById('themeToggleBtn');
        if (!button) return;

        const sync = () => {
            const dark = document.documentElement.getAttribute('data-theme') === 'dark';
            button.setAttribute('aria-label', dark ? 'Uključi svijetlu temu' : 'Uključi tamnu temu');
            button.setAttribute('title', dark ? 'Svijetla tema' : 'Tamna tema');
            button.setAttribute('aria-pressed', dark ? 'true' : 'false');
        };

        button.addEventListener('click', () => window.setTimeout(sync, 0));
        sync();
    }

    function setupSidebarAutoClose() {
        const sidebar = document.getElementById('appSidebar');
        if (!sidebar || !window.bootstrap) return;

        sidebar.querySelectorAll('a.sidebar-link').forEach(link => {
            link.addEventListener('click', () => {
                const instance = window.bootstrap.Offcanvas.getInstance(sidebar);
                if (instance) instance.hide();
            });
        });
    }

    function setupTableEnhancements() {
        document.querySelectorAll('.table tbody tr').forEach(row => {
            if (!row.hasAttribute('tabindex') && row.querySelector('a, button, input, select')) {
                row.classList.add('rd-interactive-row');
            }
        });

        document.querySelectorAll('.table-responsive').forEach(wrapper => {
            const sync = () => {
                wrapper.classList.toggle('has-horizontal-overflow', wrapper.scrollWidth > wrapper.clientWidth + 2);
                wrapper.classList.toggle('is-at-end', wrapper.scrollLeft + wrapper.clientWidth >= wrapper.scrollWidth - 2);
            };
            wrapper.addEventListener('scroll', sync, { passive: true });
            window.addEventListener('resize', sync, { passive: true });
            sync();
        });
    }

    onReady(() => {
        document.body.classList.add('ui-ready');
        setupTopbar();
        setupRevealAnimations();
        setupButtonRipples();
        setupNumberAnimations();
        setupNavigationProgress();
        setupDismissibleAlerts();
        setupThemeAccessibility();
        setupSidebarAutoClose();
        setupTableEnhancements();
    });
})();
