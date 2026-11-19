/* global document, window, URL */
(() => {
  const measurementId = 'G-1EXW8M182D';
  const preferenceKey = 'exileworth-website-analytics';
  const scriptUrl = document.currentScript?.src;
  const privacyUrl = new URL('privacy/', scriptUrl || window.location.href).href;
  let started = false;
  let preference = null;
  try {
    preference = window.localStorage.getItem(preferenceKey);
  } catch {
    // A visitor can still choose for this page when storage is unavailable.
  }

  function startAnalytics() {
    // Local previews never send analytics.
    if (started || window.location.hostname !== 'wild-butterfly.github.io') return;
    started = true;
    window[`ga-disable-${measurementId}`] = false;
    window.dataLayer = window.dataLayer || [];
    window.gtag = function () {
      window.dataLayer.push(arguments);
    };
    window.gtag('consent', 'default', {
      analytics_storage: 'granted',
      ad_storage: 'denied',
      ad_user_data: 'denied',
      ad_personalization: 'denied',
    });
    window.gtag('js', new Date());
    window.gtag('config', measurementId, {
      allow_google_signals: false,
      allow_ad_personalization_signals: false,
      page_location: window.location.origin + window.location.pathname,
    });
    const script = document.createElement('script');
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${measurementId}`;
    document.head.append(script);
  }

  function choose(value) {
    preference = value;
    try {
      window.localStorage.setItem(preferenceKey, value);
    } catch {
      // Keep the current page usable without persistent storage.
    }
    if (value === 'accepted') {
      if (started) {
        window[`ga-disable-${measurementId}`] = false;
        window.gtag('consent', 'update', { analytics_storage: 'granted' });
      } else {
        startAnalytics();
      }
    } else {
      window[`ga-disable-${measurementId}`] = true;
      // Remove only this property's first-party analytics cookies.
      for (const cookie of document.cookie.split(';')) {
        const name = cookie.trim().split('=')[0];
        if (name !== '_ga' && name !== '_ga_1EXW8M182D') continue;
        for (const domain of ['', '; domain=wild-butterfly.github.io']) {
          document.cookie = `${name}=; Max-Age=0; path=/${domain}`;
        }
      }
    }
    document.querySelector('.analytics-notice')?.remove();
  }

  function showChoices() {
    document.querySelector('.analytics-notice')?.remove();
    const notice = document.createElement('section');
    notice.className = 'analytics-notice';
    notice.setAttribute('aria-label', 'Website analytics choices');
    notice.innerHTML = `<div><strong>Help us understand website downloads</strong><p>With your permission, Google Analytics measures visits and download-link clicks. The app itself has no analytics. <a href="${privacyUrl}">Website privacy</a></p></div><div class="analytics-actions"><button type="button" data-choice="accepted">Accept analytics</button><button type="button" data-choice="declined">No thanks</button></div>`;
    notice.querySelectorAll('[data-choice]').forEach((button) => {
      button.addEventListener('click', () => choose(button.dataset.choice));
    });
    document.body.append(notice);
  }

  document.querySelectorAll('.analytics-settings').forEach((button) => {
    button.addEventListener('click', showChoices);
  });
  if (preference === 'accepted') startAnalytics();
  else if (preference !== 'declined') showChoices();
})();
