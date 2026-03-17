export interface Message {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: number;
}

export type ChatCompletionRole = 'user' | 'assistant' | 'system';

export interface ChatCompletionMessage {
  role: ChatCompletionRole;
  content: string;
}

export interface ChatSession {
  id: string;
  title: string;
  messages: Message[];
  model: string;
  createdAt: number;
  updatedAt: number;
}

export interface Model {
  id: string;
  name: string;
  description: string;
  maxTokens: number;
}

export const AVAILABLE_MODELS: Model[] = [
  {
    id: 'nvidia/llama-3.1-nemotron-70b-instruct',
    name: 'Llama 3.1 Nemotron 70B',
    description: 'Model by NVIDIA - optimized for helpful responses',
    maxTokens: 4096
  },
  {
    id: 'meta/llama-3.1-405b-instruct',
    name: 'Llama 3.1 405B',
    description: 'Largest Llama model - 405B parameters',
    maxTokens: 8192
  },
  {
    id: 'deepseek-ai/deepseek-r1',
    name: 'DeepSeek R1',
    description: 'Advanced reasoning model',
    maxTokens: 8192
  }
];
