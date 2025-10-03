// i18n bootstrapper
window.i18n = {
  lang: 'ru',
  dict: {},
  async load(basePath) {
    const res = await fetch(`${basePath}/${this.lang}.json`);
    this.dict = await res.json();
  },
  t(key) { return this.dict[key] || key; }
};

