/* global document, navigator */
const menuButton = document.querySelector('.menu-button');
const navLinks = document.querySelector('.nav-links');

menuButton?.addEventListener('click', () => {
  const open = navLinks.classList.toggle('open');
  menuButton.setAttribute('aria-expanded', String(open));
});

navLinks?.addEventListener('click', () => {
  navLinks.classList.remove('open');
  menuButton?.setAttribute('aria-expanded', 'false');
});

// Recommend the download for the visitor's platform; both stay available.
// Phones and tablets get no recommendation (iOS reports "Mac OS X").
const agent = navigator.userAgent;
const platform = /iPhone|iPad|iPod|Android/i.test(agent)
  ? null
  : /Macintosh|Mac OS X/i.test(agent)
    ? 'mac'
    : /Windows/i.test(agent)
      ? 'windows'
      : null;

/** Gives the recommended platform the red button, the other the quiet one. */
function promote(selector, attribute) {
  for (const link of document.querySelectorAll(selector)) {
    const chosen =
      link.closest(`[${attribute}]`)?.getAttribute(attribute) ?? link.dataset.platform;
    link.classList.toggle('button-primary', chosen === platform);
    link.classList.toggle('button-secondary', chosen !== platform);
  }
}

if (platform) {
  document
    .querySelector(`[data-platform-card="${platform}"]`)
    ?.classList.add('is-recommended');
  promote('.hero-actions [data-platform]', 'data-platform');
  promote('.download-card .button', 'data-platform-card');
  const hero = document.querySelector(`.hero-actions [data-platform="${platform}"]`);
  hero?.parentElement.prepend(hero);
}
