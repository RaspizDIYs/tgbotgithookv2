import { translations, type Language, type TranslationKey } from '../locales/translations';

export function useTranslation(language: Language) {
  const t = (key: TranslationKey): string => {
    return translations[language][key] || key;
  };

  return { t };
}
