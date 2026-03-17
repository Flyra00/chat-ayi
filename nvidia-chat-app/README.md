# NVIDIA Chat AI

A modern chat application powered by NVIDIA NIM API with support for multiple state-of-the-art AI models.

## Features

- 🤖 **Multi-Model Support**
  - Llama 3.1 Nemotron 70B Instruct (NVIDIA)
  - Llama 3.1 405B Instruct (Meta)
  - DeepSeek R1 (DeepSeek AI)

- ⚡ **Real-time Streaming**
  - See AI responses appear word by word
  - Smooth streaming with Server-Sent Events

- 💾 **Chat History**
  - Automatically saves conversations
  - Access previous chats from sidebar
  - Persistent storage using localStorage

- 🎨 **Modern UI**
  - Dark theme optimized for long chats
  - Responsive design (desktop & mobile)
  - Clean, intuitive interface

## Getting Started

1. **Install dependencies:**
   ```bash
   npm install
   ```

2. **Run the development server:**
   ```bash
   npm run dev
   ```

3. **Open browser and navigate to:** `http://localhost:5173`

4. **Set your API key:**
   - Click the settings icon (⚙️) in the top right
   - Enter your NVIDIA API key
   - Click Save

## Getting an API Key

1. Visit [build.nvidia.com](https://build.nvidia.com/)
2. Sign in or create an account
3. Generate an API key from the dashboard

## Tech Stack

- React 19
- TypeScript
- Vite
- Tailwind CSS
- Zustand (state management)
- NVIDIA NIM API

## Build for Production

```bash
npm run build
```

The built files will be in the `dist` folder.

## License

MIT
