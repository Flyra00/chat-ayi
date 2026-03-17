/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        nvidia: {
          dark: '#1a1a1a',
          darker: '#0d0d0d',
          green: '#76B900',
          'green-hover': '#5a8f00',
        }
      }
    },
  },
  plugins: [],
}
