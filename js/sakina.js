/* ==================================================================
   Sakīna — shared behaviour for the Islamic Projects
   · Day / Night theme, Arabic / English, and the digits used in Arabic
     (123 or ١٢٣), remembered across every project (they share one origin,
     so one choice follows you everywhere)
   · Sticky header gets its glass once you scroll
   · Gentle reveal-on-scroll for elements marked .sk-reveal
   Identical in every project.
   ================================================================== */
(function () {
  'use strict';
  var KEY = 'sakina-theme';
  var LANG_KEY = 'sakina-lang';
  var DIGITS_KEY = 'sakina-digits';
  var AR_DIGITS = '٠١٢٣٤٥٦٧٨٩';
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

  /* Digits in Arabic: ordinary 0-9 by default, Arabic-Indic ٠-٩ on request.
     Text in the page is converted as it appears; anything inside lang="en",
     <code>, <bdi dir="ltr"> or [data-digits="keep"] is left alone. */
  function digits() {
    try { return localStorage.getItem(DIGITS_KEY) === 'arab' ? 'arab' : 'latn'; } catch (e) { return 'latn'; }
  }
  function numAr(v) {
    var s = String(v);
    if (digits() === 'arab') return s.replace(/(\d)\.(\d)/g, '$1٫$2').replace(/[0-9]/g, function (d) { return AR_DIGITS[d]; });
    return s.replace(/[٠-٩]/g, function (d) { return String(AR_DIGITS.indexOf(d)); }).replace(/٫/g, '.');
  }
  function num(v) { return lang() === 'ar' ? numAr(v) : String(v); }
  function keep(node) {
    for (var el = node.parentNode; el && el.nodeType === 1; el = el.parentNode) {
      var tag = el.tagName;
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'CODE' || tag === 'TEXTAREA') return true;
      if (el.getAttribute('lang') === 'en' || el.getAttribute('data-digits') === 'keep') return true;
      if (tag === 'BDI' && el.getAttribute('dir') === 'ltr') return true;
    }
    return false;
  }
  function fixText(node) {
    var v = node.nodeValue;
    if (!v || !/[0-9٠-٩]/.test(v) || keep(node)) return;
    var n = numAr(v);
    if (n !== v) node.nodeValue = n;
  }
  function applyDigits(scope) {
    if (lang() !== 'ar' || !document.body) return;
    var w = document.createTreeWalker(scope || document.body, NodeFilter.SHOW_TEXT);
    var n;
    while ((n = w.nextNode())) fixText(n);
  }
  function setDigits(next) {
    next = next === 'arab' ? 'arab' : 'latn';
    try { localStorage.setItem(DIGITS_KEY, next); } catch (e) { /* private mode */ }
    root.setAttribute('data-digits', next);
    applyDigits();
    syncDigitButtons();
    try { document.dispatchEvent(new CustomEvent('sakina:digits', { detail: next })); } catch (e) { /* old browser */ }
    return next;
  }
  function syncDigitButtons() {
    var arab = digits() === 'arab';
    var buttons = document.querySelectorAll('[data-sk-digits-toggle]');
    for (var i = 0; i < buttons.length; i++) {
      buttons[i].setAttribute('aria-pressed', arab ? 'true' : 'false');
      buttons[i].setAttribute('aria-label', arab ? 'استخدم الأرقام 123' : 'استخدم الأرقام ١٢٣');
      buttons[i].setAttribute('title', arab ? 'الأرقام: ١٢٣ — اضغط لـ 123' : 'الأرقام: 123 — اضغط لـ ١٢٣');
    }
  }
  function watchDigits() {
    if (!('MutationObserver' in window) || !document.body) return;
    new MutationObserver(function (list) {
      if (lang() !== 'ar') return;
      list.forEach(function (m) {
        if (m.type === 'characterData') fixText(m.target);
        else m.addedNodes.forEach(function (n) { if (n.nodeType === 3) fixText(n); else if (n.nodeType === 1) applyDigits(n); });
      });
    }).observe(document.body, { childList: true, subtree: true, characterData: true });
    new MutationObserver(function () { applyDigits(); }).observe(root, { attributes: true, attributeFilter: ['lang', 'data-lang'] });
  }

  function t(pair) {
    if (!pair) return '';
    return lang() === 'ar' ? (pair.ar != null ? pair.ar : pair.en) : pair.en;
  }

  function onReady() {
    sync(current());
    var buttons = document.querySelectorAll('[data-sk-theme-toggle]');
    for (var i = 0; i < buttons.length; i++) buttons[i].addEventListener('click', toggle);
    root.setAttribute('data-digits', digits());
    applyDigits();
    watchDigits();
    syncDigitButtons();
    var digitButtons = document.querySelectorAll('[data-sk-digits-toggle]');
    for (var j = 0; j < digitButtons.length; j++) {
      digitButtons[j].addEventListener('click', function () { setDigits(digits() === 'arab' ? 'latn' : 'arab'); });
    }
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

  window.Sakina = { toggle: toggle, apply: apply, current: current, lang: lang, setLang: setLang, t: t, digits: digits, setDigits: setDigits, num: num, numAr: numAr, applyDigits: applyDigits, reveal: reveal, toast: toast, copy: copy };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', onReady);
  else onReady();
})();
