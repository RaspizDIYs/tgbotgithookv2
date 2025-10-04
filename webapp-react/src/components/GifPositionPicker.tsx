
interface GifPositionPickerProps {
  onPositionSelect: (position: string) => void;
  onClose: () => void;
}

export function GifPositionPicker({ onPositionSelect, onClose }: GifPositionPickerProps) {
  const positions = [
    { 
      id: 'top', 
      label: 'Верх',
      icon: 'keyboard_arrow_up',
      description: 'Текст в верхней части GIF'
    },
    { 
      id: 'center', 
      label: 'Центр',
      icon: 'center_focus_strong',
      description: 'Текст в центре GIF'
    },
    { 
      id: 'bottom', 
      label: 'Низ',
      icon: 'keyboard_arrow_down',
      description: 'Текст в нижней части GIF'
    },
    { 
      id: 'top-left', 
      label: 'Верх-лево',
      icon: 'north_west',
      description: 'Текст в верхнем левом углу'
    },
    { 
      id: 'top-right', 
      label: 'Верх-право',
      icon: 'north_east',
      description: 'Текст в верхнем правом углу'
    },
    { 
      id: 'bottom-left', 
      label: 'Низ-лево',
      icon: 'south_west',
      description: 'Текст в нижнем левом углу'
    },
    { 
      id: 'bottom-right', 
      label: 'Низ-право',
      icon: 'south_east',
      description: 'Текст в нижнем правом углу'
    },
  ];

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-background border rounded-lg p-6 max-w-md w-full mx-4">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center gap-2">
            <span className="material-icons">open_with</span>
            Позиция GIF
          </h3>
          <button
            onClick={onClose}
            className="p-1 hover:bg-accent rounded-full"
          >
            <span className="material-icons text-sm">close</span>
          </button>
        </div>
        
        <p className="text-sm text-muted-foreground mb-4">
          Выберите позицию для текста на GIF
        </p>
        
        <div className="space-y-2">
          {positions.map((position) => (
            <button
              key={position.id}
              onClick={() => {
                onPositionSelect(position.id);
                onClose();
              }}
              className="w-full flex items-center gap-3 p-3 hover:bg-accent rounded-lg transition-colors text-left"
            >
              <div className="p-2 bg-primary/10 rounded-lg">
                <span className="material-icons text-primary">{position.icon}</span>
              </div>
              <div className="flex-1">
                <div className="font-medium">{position.label}</div>
                <div className="text-xs text-muted-foreground">{position.description}</div>
              </div>
              <span className="material-icons text-muted-foreground">chevron_right</span>
            </button>
          ))}
        </div>
        
        <div className="mt-4 pt-4 border-t">
          <button
            onClick={onClose}
            className="w-full py-2 px-4 bg-muted hover:bg-muted/80 rounded-md text-sm transition-colors"
          >
            Отмена
          </button>
        </div>
      </div>
    </div>
  );
}
