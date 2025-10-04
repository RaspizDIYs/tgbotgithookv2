import { useTranslation } from '../hooks/useTranslation';
import type { Language } from '../locales/translations';

interface GifColorPickerProps {
  language: Language;
  onColorSelect: (color: string) => void;
  onClose: () => void;
}

const colors = [
  { name: 'white', hex: '#FFFFFF', label: 'Белый' },
  { name: 'black', hex: '#000000', label: 'Черный' },
  { name: 'red', hex: '#FF0000', label: 'Красный' },
  { name: 'green', hex: '#00FF00', label: 'Зеленый' },
  { name: 'blue', hex: '#0000FF', label: 'Синий' },
  { name: 'yellow', hex: '#FFFF00', label: 'Желтый' },
  { name: 'orange', hex: '#FFA500', label: 'Оранжевый' },
  { name: 'purple', hex: '#800080', label: 'Фиолетовый' },
  { name: 'pink', hex: '#FFC0CB', label: 'Розовый' },
  { name: 'cyan', hex: '#00FFFF', label: 'Голубой' },
  { name: 'magenta', hex: '#FF00FF', label: 'Пурпурный' },
  { name: 'lime', hex: '#00FF00', label: 'Лайм' },
];

export function GifColorPicker({ language, onColorSelect, onClose }: GifColorPickerProps) {
  const { t } = useTranslation(language);

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-background border rounded-lg p-6 max-w-md w-full mx-4">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center gap-2">
            <span className="material-icons">palette</span>
            {t('gifColor')}
          </h3>
          <button
            onClick={onClose}
            className="p-1 hover:bg-accent rounded-full"
          >
            <span className="material-icons text-sm">close</span>
          </button>
        </div>
        
        <p className="text-sm text-muted-foreground mb-4">
          {language === 'ru' ? 'Выберите цвет для текста на GIF' : 'Select color for text on GIF'}
        </p>
        
        <div className="grid grid-cols-4 gap-3">
          {colors.map((color) => (
            <button
              key={color.name}
              onClick={() => {
                onColorSelect(color.name);
                onClose();
              }}
              className="flex flex-col items-center gap-2 p-3 hover:bg-accent rounded-lg transition-colors"
              title={color.label}
            >
              <div
                className="w-8 h-8 rounded-full border-2 border-border shadow-sm"
                style={{ backgroundColor: color.hex }}
              />
              <span className="text-xs text-muted-foreground">{color.label}</span>
            </button>
          ))}
        </div>
        
        <div className="mt-4 pt-4 border-t">
          <button
            onClick={onClose}
            className="w-full py-2 px-4 bg-muted hover:bg-muted/80 rounded-md text-sm transition-colors"
          >
            {language === 'ru' ? 'Отмена' : 'Cancel'}
          </button>
        </div>
      </div>
    </div>
  );
}
