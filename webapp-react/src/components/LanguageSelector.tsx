import React, { useState, useRef, useEffect } from 'react';
import type { Language } from '../locales/translations';

interface LanguageSelectorProps {
  currentLanguage: Language;
  onLanguageChange: (lang: Language) => void;
}

export const LanguageSelector = React.forwardRef<HTMLDivElement, LanguageSelectorProps>(
  ({ currentLanguage, onLanguageChange }, ref) => {
    const [isOpen, setIsOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    const languages = [
      { code: 'ru' as Language, name: 'Русский', flag: 'RU' },
      { code: 'en' as Language, name: 'English', flag: 'EN' }
    ];

    useEffect(() => {
      function handleClickOutside(event: MouseEvent) {
        if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
          setIsOpen(false);
        }
      }

      document.addEventListener('mousedown', handleClickOutside);
      return () => {
        document.removeEventListener('mousedown', handleClickOutside);
      };
    }, []);

    const currentLang = languages.find(lang => lang.code === currentLanguage);

    return (
      <div className="relative" ref={ref || dropdownRef}>
        <button
          onClick={() => setIsOpen(!isOpen)}
          className="flex items-center justify-center w-12 h-10 rounded-md border border-border hover:bg-accent transition-colors"
          aria-label="Выбрать язык"
        >
          <span className="text-sm font-medium">{currentLang?.flag}</span>
        </button>

        {isOpen && (
          <div className="absolute top-full right-0 mt-1 w-12 bg-background border border-border rounded-md shadow-lg z-50">
            {languages.map((lang) => (
              <button
                key={lang.code}
                onClick={() => {
                  onLanguageChange(lang.code);
                  setIsOpen(false);
                }}
                className={`w-full flex items-center justify-center h-10 hover:bg-accent transition-colors first:rounded-t-md last:rounded-b-md ${
                  currentLanguage === lang.code ? 'bg-accent text-accent-foreground' : ''
                }`}
              >
                <span className="text-sm font-medium">{lang.flag}</span>
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }
);

LanguageSelector.displayName = 'LanguageSelector';
