import path from 'path';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // Mirror the @/* path alias from tsconfig.json.
      '@': path.resolve(__dirname, 'src'),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    css: false,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.d.ts', 'src/**/*.stories.{ts,tsx}'],
      reporter: ['text', 'json', 'lcov'],
      thresholds: {
        lines: 1,
        branches: 60,
        functions: 45,
        statements: 1,
      },
    },
  },
});
