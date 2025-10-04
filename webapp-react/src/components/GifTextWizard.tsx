import React, { useState } from 'react';
import { useTranslation } from '../hooks/useTranslation';
import type { Language } from '../locales/translations';

interface GifTextWizardProps {
  language: Language;
  onClose: () => void;
}

type WizardStep = 'upload' | 'text' | 'position' | 'color' | 'preview';

export function GifTextWizard({ language, onClose }: GifTextWizardProps) {
  const { } = useTranslation(language);
  const [step, setStep] = useState<WizardStep>('upload');
  const [gifFile, setGifFile] = useState<File | null>(null);
  const [text, setText] = useState('');
  const [position, setPosition] = useState('center');
  const [color, setColor] = useState('white');

  const positions = [
    { id: 'top', label: 'Верх', icon: 'keyboard_arrow_up' },
    { id: 'center', label: 'Центр', icon: 'center_focus_strong' },
    { id: 'bottom', label: 'Низ', icon: 'keyboard_arrow_down' },
    { id: 'top-left', label: 'Верх-лево', icon: 'north_west' },
    { id: 'top-right', label: 'Верх-право', icon: 'north_east' },
    { id: 'bottom-left', label: 'Низ-лево', icon: 'south_west' },
    { id: 'bottom-right', label: 'Низ-право', icon: 'south_east' },
  ];

  const colors = [
    { name: 'white', hex: '#FFFFFF', label: 'Белый' },
    { name: 'black', hex: '#000000', label: 'Черный' },
    { name: 'red', hex: '#FF0000', label: 'Красный' },
    { name: 'green', hex: '#00FF00', label: 'Зеленый' },
    { name: 'blue', hex: '#0000FF', label: 'Синий' },
    { name: 'yellow', hex: '#FFFF00', label: 'Желтый' },
  ];

  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file && file.type.startsWith('image/')) {
      setGifFile(file);
      setStep('text');
    }
  };

  const handleNext = () => {
    switch (step) {
      case 'text':
        setStep('position');
        break;
      case 'position':
        setStep('color');
        break;
      case 'color':
        setStep('preview');
        break;
    }
  };

  const handleBack = () => {
    switch (step) {
      case 'text':
        setStep('upload');
        break;
      case 'position':
        setStep('text');
        break;
      case 'color':
        setStep('position');
        break;
      case 'preview':
        setStep('color');
        break;
    }
  };

  const handleFinish = () => {
    // Здесь можно отправить данные на сервер для обработки
    console.log('GIF Text Wizard completed:', { gifFile, text, position, color });
    onClose();
  };

  const renderStep = () => {
    switch (step) {
      case 'upload':
        return (
          <div className="space-y-4">
            <div className="text-center">
              <div className="mx-auto w-16 h-16 bg-muted rounded-lg flex items-center justify-center mb-4">
                <span className="material-icons text-2xl text-muted-foreground">image</span>
              </div>
              <h3 className="font-medium mb-2">Загрузите GIF файл</h3>
              <p className="text-sm text-muted-foreground mb-4">
                Выберите GIF файл для добавления текста
              </p>
            </div>
            <input
              type="file"
              accept="image/*"
              onChange={handleFileUpload}
              className="w-full p-3 border border-dashed border-border rounded-lg bg-background text-center cursor-pointer hover:bg-accent/50"
            />
          </div>
        );

      case 'text':
        return (
          <div className="space-y-4">
            <div className="text-center">
              <div className="mx-auto w-16 h-16 bg-muted rounded-lg flex items-center justify-center mb-4">
                <span className="material-icons text-2xl text-muted-foreground">text_fields</span>
              </div>
              <h3 className="font-medium mb-2">Введите текст</h3>
              <p className="text-sm text-muted-foreground mb-4">
                Введите текст, который будет добавлен на GIF
              </p>
            </div>
            <textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="Введите текст..."
              className="w-full p-3 border border-border rounded-lg bg-background resize-none"
              rows={3}
            />
          </div>
        );

      case 'position':
        return (
          <div className="space-y-4">
            <div className="text-center">
              <div className="mx-auto w-16 h-16 bg-muted rounded-lg flex items-center justify-center mb-4">
                <span className="material-icons text-2xl text-muted-foreground">open_with</span>
              </div>
              <h3 className="font-medium mb-2">Выберите позицию</h3>
              <p className="text-sm text-muted-foreground mb-4">
                Выберите где разместить текст на GIF
              </p>
            </div>
            <div className="grid grid-cols-2 gap-2">
              {positions.map((pos) => (
                <button
                  key={pos.id}
                  onClick={() => setPosition(pos.id)}
                  className={`p-3 border rounded-lg flex items-center gap-2 transition-colors ${
                    position === pos.id ? 'border-primary bg-primary/10' : 'border-border hover:bg-accent'
                  }`}
                >
                  <span className="material-icons text-sm">{pos.icon}</span>
                  <span className="text-sm">{pos.label}</span>
                </button>
              ))}
            </div>
          </div>
        );

      case 'color':
        return (
          <div className="space-y-4">
            <div className="text-center">
              <div className="mx-auto w-16 h-16 bg-muted rounded-lg flex items-center justify-center mb-4">
                <span className="material-icons text-2xl text-muted-foreground">palette</span>
              </div>
              <h3 className="font-medium mb-2">Выберите цвет</h3>
              <p className="text-sm text-muted-foreground mb-4">
                Выберите цвет для текста
              </p>
            </div>
            <div className="grid grid-cols-3 gap-3">
              {colors.map((col) => (
                <button
                  key={col.name}
                  onClick={() => setColor(col.name)}
                  className={`p-3 border rounded-lg flex flex-col items-center gap-2 transition-colors ${
                    color === col.name ? 'border-primary bg-primary/10' : 'border-border hover:bg-accent'
                  }`}
                >
                  <div
                    className="w-6 h-6 rounded-full border border-border"
                    style={{ backgroundColor: col.hex }}
                  />
                  <span className="text-xs">{col.label}</span>
                </button>
              ))}
            </div>
          </div>
        );

      case 'preview':
        return (
          <div className="space-y-4">
            <div className="text-center">
              <div className="mx-auto w-16 h-16 bg-muted rounded-lg flex items-center justify-center mb-4">
                <span className="material-icons text-2xl text-muted-foreground">preview</span>
              </div>
              <h3 className="font-medium mb-2">Предварительный просмотр</h3>
              <p className="text-sm text-muted-foreground mb-4">
                Проверьте настройки перед созданием GIF
              </p>
            </div>
            <div className="bg-muted rounded-lg p-4 space-y-2">
              <div className="text-sm"><strong>Текст:</strong> {text}</div>
              <div className="text-sm"><strong>Позиция:</strong> {positions.find(p => p.id === position)?.label}</div>
              <div className="text-sm"><strong>Цвет:</strong> {colors.find(c => c.name === color)?.label}</div>
            </div>
          </div>
        );

      default:
        return null;
    }
  };

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-background border rounded-lg p-6 max-w-md w-full mx-4">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center gap-2">
            <span className="material-icons">text_fields</span>
            Добавить текст на GIF
          </h3>
          <button
            onClick={onClose}
            className="p-1 hover:bg-accent rounded-full"
          >
            <span className="material-icons text-sm">close</span>
          </button>
        </div>

        {/* Прогресс бар */}
        <div className="mb-6">
          <div className="flex items-center justify-between text-xs text-muted-foreground mb-2">
            <span>Шаг {['upload', 'text', 'position', 'color', 'preview'].indexOf(step) + 1} из 5</span>
            <span>{Math.round((['upload', 'text', 'position', 'color', 'preview'].indexOf(step) + 1) / 5 * 100)}%</span>
          </div>
          <div className="w-full bg-muted rounded-full h-2">
            <div
              className="bg-primary h-2 rounded-full transition-all duration-300"
              style={{ width: `${(['upload', 'text', 'position', 'color', 'preview'].indexOf(step) + 1) / 5 * 100}%` }}
            />
          </div>
        </div>

        {renderStep()}

        <div className="mt-6 flex gap-2">
          {step !== 'upload' && (
            <button
              onClick={handleBack}
              className="px-4 py-2 bg-muted hover:bg-muted/80 rounded-md text-sm transition-colors"
            >
              Назад
            </button>
          )}
          <div className="flex-1" />
          {step !== 'preview' ? (
            <button
              onClick={handleNext}
              disabled={step === 'text' && !text.trim()}
              className="px-4 py-2 bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50 disabled:cursor-not-allowed rounded-md text-sm transition-colors"
            >
              Далее
            </button>
          ) : (
            <button
              onClick={handleFinish}
              className="px-4 py-2 bg-primary text-primary-foreground hover:bg-primary/90 rounded-md text-sm transition-colors"
            >
              Создать GIF
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
