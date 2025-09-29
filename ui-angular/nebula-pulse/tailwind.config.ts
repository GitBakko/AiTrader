import type { Config } from 'tailwindcss';

const config = {
  content: [
    './src/**/*.{html,ts,scss}'
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        ktui: {
          surface: '#06060f',
          primary: '#7c3aed',
          accent: '#22d3ee',
          warning: '#facc15',
          danger: '#fb7185',
          success: '#4ade80'
        }
      },
      fontFamily: {
        display: ['Space Grotesk', 'Segoe UI', 'system-ui', 'sans-serif'],
        mono: ['JetBrains Mono', 'Fira Code', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Monaco', 'Consolas', 'monospace']
      },
      boxShadow: {
        'ktui-card': '0 24px 48px -12px rgba(124, 58, 237, 0.25)',
        'ktui-card-soft': '0 18px 40px -15px rgba(34, 211, 238, 0.18)'
      },
      animation: {
        'pulse-soft': 'pulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite'
      }
    }
  },
  plugins: []
} satisfies Config;

export default config;
