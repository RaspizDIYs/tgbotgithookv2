// i18n bootstrapper with switching and auto-apply
window.i18n = {
  lang: (localStorage.getItem('webapp-lang') || (navigator.language || 'ru').slice(0,2)).toLowerCase() === 'en' ? 'en' : 'ru',
  dict: {},
  basePath: './lang',
  async load(basePath) {
    if (basePath) this.basePath = basePath;
    const res = await fetch(`${this.basePath}/${this.lang}/index.json`);
    this.dict = await res.json();
  },
  t(key) { return this.dict[key] || key; },
  async switch(lang) {
    this.lang = (lang === 'en') ? 'en' : 'ru';
    localStorage.setItem('webapp-lang', this.lang);
    await this.load();
    this.apply();
  },
  apply() {
    document.querySelectorAll('[data-i18n]').forEach(el => {
      const key = el.getAttribute('data-i18n');
      if (key) el.textContent = this.t(key);
    });
    document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
      const key = el.getAttribute('data-i18n-placeholder');
      if (key) el.setAttribute('placeholder', this.t(key));
    });
    document.title = this.t('title') || document.title;
  }
};

