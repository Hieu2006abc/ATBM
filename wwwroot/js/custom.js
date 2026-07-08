/* ============================================================
   CUSTOM.JS - JobPortal UI Enhancement
   Scroll reveal, counter animation, skeleton loading,
   toast notifications, back-to-top, navbar scroll effect
   ============================================================ */

(function () {
    'use strict';

    // ──────────── SCROLL REVEAL ────────────
    function initScrollReveal() {
        var revealElements = document.querySelectorAll(
            '.scroll-reveal, .scroll-reveal-left, .scroll-reveal-right, .scroll-reveal-scale, .scroll-reveal-children'
        );

        if (!revealElements.length) return;

        // Check for reduced motion preference
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            revealElements.forEach(function (el) {
                el.classList.add('revealed');
            });
            return;
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('revealed');
                    observer.unobserve(entry.target);
                }
            });
        }, {
            threshold: 0.12,
            rootMargin: '0px 0px -40px 0px'
        });

        revealElements.forEach(function (el) {
            observer.observe(el);
        });
    }

    // ──────────── COUNTER ANIMATION ────────────
    function initCounterAnimation() {
        var counters = document.querySelectorAll('[data-counter]');
        if (!counters.length) return;

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    animateCounter(entry.target);
                    observer.unobserve(entry.target);
                }
            });
        }, {
            threshold: 0.3
        });

        counters.forEach(function (el) {
            observer.observe(el);
        });
    }

    function animateCounter(element) {
        var target = parseInt(element.getAttribute('data-counter'), 10);
        var suffix = element.getAttribute('data-counter-suffix') || '';
        var prefix = element.getAttribute('data-counter-prefix') || '';
        var duration = parseInt(element.getAttribute('data-counter-duration'), 10) || 2000;
        var useFormat = element.hasAttribute('data-counter-format');

        if (isNaN(target)) return;

        var startTime = null;
        var startValue = 0;

        element.classList.add('counting');

        function easeOutExpo(t) {
            return t === 1 ? 1 : 1 - Math.pow(2, -10 * t);
        }

        function step(timestamp) {
            if (!startTime) startTime = timestamp;
            var progress = Math.min((timestamp - startTime) / duration, 1);
            var easedProgress = easeOutExpo(progress);
            var currentValue = Math.floor(startValue + (target - startValue) * easedProgress);

            var displayValue = useFormat ? currentValue.toLocaleString('vi-VN') : currentValue.toString();
            element.textContent = prefix + displayValue + suffix;

            if (progress < 1) {
                requestAnimationFrame(step);
            } else {
                element.textContent = prefix + (useFormat ? target.toLocaleString('vi-VN') : target.toString()) + suffix;
                element.classList.remove('counting');
            }
        }

        requestAnimationFrame(step);
    }

    // ──────────── SKELETON LOADING ────────────
    window.JobPortalSkeleton = {
        show: function (containerId) {
            var container = document.getElementById(containerId);
            if (!container) return;

            var skeletonHTML = '<div class="skeleton-container">';
            for (var i = 0; i < 6; i++) {
                skeletonHTML += '<div class="skeleton-card">';
                skeletonHTML += '  <div class="skeleton-row">';
                skeletonHTML += '    <div class="skeleton-line h-52" style="margin-bottom:0"></div>';
                skeletonHTML += '    <div style="flex:1">';
                skeletonHTML += '      <div class="skeleton-line w-80 h-10"></div>';
                skeletonHTML += '      <div class="skeleton-line w-50 h-10" style="margin-bottom:0"></div>';
                skeletonHTML += '    </div>';
                skeletonHTML += '  </div>';
                skeletonHTML += '  <div class="skeleton-line h-20 w-80"></div>';
                skeletonHTML += '  <div class="skeleton-line w-60"></div>';
                skeletonHTML += '  <div class="skeleton-line w-50"></div>';
                skeletonHTML += '  <div class="skeleton-tags">';
                skeletonHTML += '    <div class="skeleton-tag"></div>';
                skeletonHTML += '    <div class="skeleton-tag"></div>';
                skeletonHTML += '    <div class="skeleton-tag"></div>';
                skeletonHTML += '  </div>';
                skeletonHTML += '  <div class="skeleton-line w-40"></div>';
                skeletonHTML += '</div>';
            }
            skeletonHTML += '</div>';

            container.innerHTML = skeletonHTML;
        },

        hide: function (containerId) {
            var container = document.getElementById(containerId);
            if (!container) return;
            var skeleton = container.querySelector('.skeleton-container');
            if (skeleton) {
                skeleton.style.opacity = '0';
                skeleton.style.transition = 'opacity 0.3s ease';
                setTimeout(function () { skeleton.remove(); }, 300);
            }
        }
    };

    // ──────────── TOAST NOTIFICATIONS ────────────
    window.JobPortalToast = {
        _container: null,

        _getContainer: function () {
            if (!this._container) {
                this._container = document.getElementById('toastContainer');
                if (!this._container) {
                    this._container = document.createElement('div');
                    this._container.id = 'toastContainer';
                    this._container.className = 'toast-container';
                    this._container.setAttribute('aria-live', 'polite');
                    document.body.appendChild(this._container);
                }
            }
            return this._container;
        },

        show: function (options) {
            var type = options.type || 'info'; // success, error, warning, info
            var title = options.title || '';
            var message = options.message || '';
            var duration = options.duration || 4000;

            var icons = {
                success: '<i class="fas fa-check"></i>',
                error: '<i class="fas fa-times"></i>',
                warning: '<i class="fas fa-exclamation"></i>',
                info: '<i class="fas fa-info"></i>'
            };

            var titles = {
                success: title || 'Thành công',
                error: title || 'Lỗi',
                warning: title || 'Cảnh báo',
                info: title || 'Thông báo'
            };

            var toast = document.createElement('div');
            toast.className = 'toast-item';
            toast.style.setProperty('--toast-duration', duration + 'ms');
            toast.innerHTML =
                '<div class="toast-icon toast-' + type + '">' + (icons[type] || icons.info) + '</div>' +
                '<div class="toast-content">' +
                '  <div class="toast-title">' + titles[type] + '</div>' +
                '  <div class="toast-message">' + message + '</div>' +
                '</div>' +
                '<button class="toast-close" aria-label="Đóng">&times;</button>' +
                '<div class="toast-progress toast-' + type + '"></div>';

            var container = this._getContainer();
            container.appendChild(toast);

            // Close on click
            var closeBtn = toast.querySelector('.toast-close');
            closeBtn.addEventListener('click', function () {
                removeToast(toast);
            });

            toast.addEventListener('click', function (e) {
                if (e.target !== closeBtn && !closeBtn.contains(e.target)) {
                    removeToast(toast);
                }
            });

            // Auto remove
            var autoRemoveTimer = setTimeout(function () {
                removeToast(toast);
            }, duration);

            function removeToast(el) {
                clearTimeout(autoRemoveTimer);
                el.classList.add('toast-removing');
                setTimeout(function () {
                    if (el.parentNode) el.parentNode.removeChild(el);
                }, 350);
            }
        },

        success: function (message, title) {
            this.show({ type: 'success', title: title, message: message });
        },
        error: function (message, title) {
            this.show({ type: 'error', title: title, message: message });
        },
        warning: function (message, title) {
            this.show({ type: 'warning', title: title, message: message });
        },
        info: function (message, title) {
            this.show({ type: 'info', title: title, message: message });
        }
    };

    // ──────────── BACK TO TOP BUTTON ────────────
    function initBackToTop() {
        var btn = document.getElementById('backToTop');
        if (!btn) return;

        var scrollThreshold = 400;

        function toggleVisibility() {
            if (window.scrollY > scrollThreshold) {
                btn.classList.add('visible');
            } else {
                btn.classList.remove('visible');
            }
        }

        window.addEventListener('scroll', throttle(toggleVisibility, 100), { passive: true });

        btn.addEventListener('click', function () {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        });
    }

    // ──────────── NAVBAR SCROLL EFFECT ────────────
    function initNavbarScroll() {
        var navbar = document.querySelector('.navbar.sticky-top');
        if (!navbar) return;

        function updateNavbar() {
            if (window.scrollY > 50) {
                navbar.classList.add('scrolled');
            } else {
                navbar.classList.remove('scrolled');
            }
        }

        window.addEventListener('scroll', throttle(updateNavbar, 50), { passive: true });
        updateNavbar();
    }

    // ──────────── THROTTLE UTILITY ────────────
    function throttle(fn, wait) {
        var lastTime = 0;
        return function () {
            var now = Date.now();
            if (now - lastTime >= wait) {
                lastTime = now;
                fn.apply(this, arguments);
            }
        };
    }

    // ──────────── INITIALIZE ALL ────────────
    function init() {
        initScrollReveal();
        initCounterAnimation();
        initBackToTop();
        initNavbarScroll();
    }

    // Run on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
