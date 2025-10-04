import React, { useState } from 'react';
import { useTranslation } from '../hooks/useTranslation';
import type { Language } from '../locales/translations';

interface GifSearchDialogProps {
  language: Language;
  onSearch: (query: string) => void;
  onClose: () => void;
}

export function GifSearchDialog({ language, onSearch, onClose }: GifSearchDialogProps) {
  const { t } = useTranslation(language);
  const [query, setQuery] = useState('');

  const handleSearch = () => {
    if (query.trim()) {
      onSearch(query.trim());
      onClose();
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleSearch();
    }
  };

  const popularQueries = [
    'котики', 'собаки', 'мемы', 'смех', 'танцы', 'музыка',
    'cats', 'dogs', 'memes', 'laugh', 'dance', 'music'
  ];

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-background border rounded-lg p-6 max-w-md w-full mx-4">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center gap-2">
            <span className="material-icons">search</span>
            {t('gifSearch')}
          </h3>
          <button
            onClick={onClose}
            className="p-1 hover:bg-accent rounded-full"
          >
            <span className="material-icons text-sm">close</span>
          </button>
        </div>
        
        <p className="text-sm text-muted-foreground mb-4">
          {language === 'ru' ? 'Введите запрос для поиска GIF' : 'Enter query to search for GIF'}
        </p>
        
        <div className="space-y-4">
          <div>
            <input
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyPress={handleKeyPress}
              placeholder={language === 'ru' ? 'Например: котики' : 'For example: cats'}
              className="w-full px-3 py-2 border border-border rounded-md bg-background text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
              autoFocus
            />
          </div>
          
          <div>
            <p className="text-xs text-muted-foreground mb-2">
              {language === 'ru' ? 'Популярные запросы:' : 'Popular queries:'}
            </p>
            <div className="flex flex-wrap gap-2">
              {popularQueries.map((popularQuery) => (
                <button
                  key={popularQuery}
                  onClick={() => setQuery(popularQuery)}
                  className="px-2 py-1 text-xs bg-muted hover:bg-muted/80 rounded-md transition-colors"
                >
                  {popularQuery}
                </button>
              ))}
            </div>
          </div>
        </div>
        
        <div className="mt-6 flex gap-2">
          <button
            onClick={handleSearch}
            disabled={!query.trim()}
            className="flex-1 py-2 px-4 bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50 disabled:cursor-not-allowed rounded-md text-sm transition-colors"
          >
            {language === 'ru' ? 'Найти GIF' : 'Find GIF'}
          </button>
          <button
            onClick={onClose}
            className="px-4 py-2 bg-muted hover:bg-muted/80 rounded-md text-sm transition-colors"
          >
            {language === 'ru' ? 'Отмена' : 'Cancel'}
          </button>
        </div>
      </div>
    </div>
  );
}
