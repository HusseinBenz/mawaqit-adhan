/* ==================================================================
   Sakīna — shared behaviour for the Islamic Projects
   · Day / Night theme and Arabic / English, remembered across every
     project (they share one origin, so one choice follows you everywhere)
   · Sticky header gets its glass once you scroll
   · Gentle reveal-on-scroll for elements marked .sk-reveal
   Identical in every project.
   ================================================================== */
(function () {
  'use strict';
  var KEY = 'sakina-theme';
  var LANG_KEY = 'sakina-lang';
  var root = document.documentElement;

  function lang() {
    return root.getAttribute('data-lang') === 'ar' || root.getAttribute('lang') === 'ar' ? 'ar' : 'en';
  }

  function current() {
    return root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
  }

  function sync(theme) {
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', theme === 'dark' ? '#0B1624' : '#F6F4EE');
    var buttons = document.querySelectorAll('[data-sk-theme-toggle]');
    var ar = lang() === 'ar';
    for (var i = 0; i < buttons.length; i++) {
      var b = buttons[i];
      var toDay = (ar && b.getAttribute('data-label-day-ar')) || b.getAttribute('data-label-day') || (ar ? 'التبديل إلى الوضع النهاري' : 'Switch to day theme');
      var toNight = (ar && b.getAttribute('data-label-night-ar')) || b.getAttribute('data-label-night') || (ar ? 'التبديل إلى الوضع الليلي' : 'Switch to night theme');
      b.setAttribute('aria-label', theme === 'dark' ? toDay : toNight);
      b.setAttribute('title', theme === 'dark' ? toDay : toNight);
    }
  }

  function apply(theme) {
    root.setAttribute('data-theme', theme);
    sync(theme);
  }

  function toggle() {
    var next = current() === 'dark' ? 'light' : 'dark';
    try { localStorage.setItem(KEY, next); } catch (e) { /* private mode */ }
    apply(next);
    try { document.dispatchEvent(new CustomEvent('sakina:theme', { detail: next })); } catch (e) { /* old browser */ }
    return next;
  }

  /* Language: pages mark text as <span class="en">…</span><span class="ar">…</span>
     and CSS shows the one that matches <html data-lang>. */
  function setLang(next) {
    next = next === 'ar' ? 'ar' : 'en';
    root.setAttribute('data-lang', next);
    root.setAttribute('lang', next);
    root.setAttribute('dir', next === 'ar' ? 'rtl' : 'ltr');
    try { localStorage.setItem(LANG_KEY, next); } catch (e) { /* private mode */ }
    try {
      var url = new URL(location.href);
      if (url.searchParams.has('lang')) { url.searchParams.set('lang', next); history.replaceState(null, '', url); }
    } catch (e) { /* file:// or old browser */ }
    sync(current());
    try { document.dispatchEvent(new CustomEvent('sakina:lang', { detail: next })); } catch (e) { /* old browser */ }
    return next;
  }

  function t(pair) {
    if (!pair) return '';
    return lang() === 'ar' ? (pair.ar != null ? pair.ar : pair.en) : pair.en;
  }

  function onReady() {
    sync(current());
    var buttons = document.querySelectorAll('[data-sk-theme-toggle]');
    for (var i = 0; i < buttons.length; i++) buttons[i].addEventListener('click', toggle);
    var langButtons = document.querySelectorAll('[data-sk-lang-toggle]');
    for (var k = 0; k < langButtons.length; k++) {
      langButtons[k].addEventListener('click', function () { setLang(lang() === 'ar' ? 'en' : 'ar'); });
    }

    var header = document.querySelector('.sk-header');
    if (header) {
      var stuck = function () { header.classList.toggle('is-stuck', window.scrollY > 8); };
      stuck();
      window.addEventListener('scroll', stuck, { passive: true });
    }
    reveal(document);
  }

  function reveal(scope) {
    var els = (scope || document).querySelectorAll('.sk-reveal:not(.is-in)');
    if (!els.length) return;
    if (!('IntersectionObserver' in window)) {
      for (var i = 0; i < els.length; i++) els[i].classList.add('is-in');
      return;
    }
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) { entry.target.classList.add('is-in'); io.unobserve(entry.target); }
      });
    }, { threshold: 0.12, rootMargin: '0px 0px -6% 0px' });
    for (var j = 0; j < els.length; j++) io.observe(els[j]);
  }

  var toastTimer = null;
  function toast(message) {
    var el = document.querySelector('.sk-toast');
    if (!el) {
      el = document.createElement('div');
      el.className = 'sk-toast';
      el.setAttribute('role', 'status');
      el.setAttribute('aria-live', 'polite');
      document.body.appendChild(el);
    }
    el.textContent = message;
    el.classList.add('is-on');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { el.classList.remove('is-on'); }, 1900);
  }

  function copy(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).catch(function () { legacyCopy(text); });
    }
    legacyCopy(text);
    return Promise.resolve();
  }
  function legacyCopy(text) {
    var ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); } catch (e) { /* nothing else to try */ }
    document.body.removeChild(ta);
  }

  window.Sakina = { toggle: toggle, apply: apply, current: current, lang: lang, setLang: setLang, t: t, reveal: reveal, toast: toast, copy: copy };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', onReady);
  else onReady();
})();
