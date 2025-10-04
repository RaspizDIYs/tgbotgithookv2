import React from 'react';

interface BurgerMenuProps {
  isOpen: boolean;
  onToggle: () => void;
}

export const BurgerMenu = React.forwardRef<HTMLButtonElement, BurgerMenuProps>(
  ({ isOpen, onToggle }, ref) => {
    return (
      <button
        ref={ref}
        onClick={onToggle}
        className="lg:hidden p-2 rounded-md hover:bg-accent transition-colors"
        aria-label="Открыть меню"
      >
        <div className="w-6 h-6 flex flex-col justify-center items-center">
          <span className={`
            block w-5 h-0.5 bg-current transition-all duration-300
            ${isOpen ? 'rotate-45 translate-y-1' : ''}
          `} />
          <span className={`
            block w-5 h-0.5 bg-current transition-all duration-300 mt-1
            ${isOpen ? 'opacity-0' : ''}
          `} />
          <span className={`
            block w-5 h-0.5 bg-current transition-all duration-300 mt-1
            ${isOpen ? '-rotate-45 -translate-y-1' : ''}
          `} />
        </div>
      </button>
    );
  }
);

BurgerMenu.displayName = 'BurgerMenu';
