import type { Config } from 'tailwindcss';

// Tasarım yönü: "usta işi" — sıcak kağıt zemin, derin mürekkep, bakır vurgu.
// Stok foto yok; atmosfer CSS gradyan + ince desenle kuruluyor (bkz. globals.css).
const config: Config = {
  content: ['./app/**/*.{ts,tsx}', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        ink: {
          DEFAULT: '#211A15',
          950: '#181310',
          900: '#211A15',
          800: '#2E251E',
          700: '#463A30',
          600: '#5F5045',
          500: '#7A6A5D',
          400: '#9A8878',
        },
        paper: {
          DEFAULT: '#FAF6EF',
          warm: '#F2EAE0',
          deep: '#E9DFD2',
        },
        copper: {
          DEFAULT: '#B4551F',
          700: '#96451A',
          600: '#B4551F',
          500: '#C96A31',
          400: '#DE8A52',
          200: '#F3D3BC',
          100: '#F9E9DC',
        },
        gold: {
          DEFAULT: '#D9A441',
        },
      },
      fontFamily: {
        display: ['var(--font-display)', 'Georgia', 'serif'],
        sans: ['var(--font-sans)', 'system-ui', 'sans-serif'],
      },
      maxWidth: {
        site: '72rem',
      },
      boxShadow: {
        card: '0 1px 2px rgba(33, 26, 21, 0.05), 0 4px 16px -6px rgba(33, 26, 21, 0.08)',
        'card-hover':
          '0 2px 4px rgba(33, 26, 21, 0.06), 0 12px 32px -8px rgba(33, 26, 21, 0.16)',
      },
      keyframes: {
        'fade-up': {
          from: { opacity: '0', transform: 'translateY(14px)' },
          to: { opacity: '1', transform: 'translateY(0)' },
        },
      },
      animation: {
        'fade-up': 'fade-up 0.6s cubic-bezier(0.22, 1, 0.36, 1) both',
      },
    },
  },
  plugins: [],
};

export default config;
