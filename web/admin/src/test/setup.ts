import '@testing-library/jest-dom/vitest';

Object.defineProperty(window, 'confirm', { value: () => true, writable: true });
