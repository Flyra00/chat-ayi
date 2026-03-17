import { useState, useRef, useEffect } from 'react';
import { useChatStore } from '../stores/chatStore';
import { NvidiaApiService } from '../services/nvidiaApi';
import { AVAILABLE_MODELS, type Message } from '../types';
import type { ChatCompletionMessage } from '../types';
import { Send, Loader2, Bot, User, AlertCircle } from 'lucide-react';

export function ChatWindow() {
  const [input, setInput] = useState('');
  const [error, setError] = useState<string | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  
  const messages = useChatStore((state) => state.messages);
  const currentModel = useChatStore((state) => state.currentModel);
  const isStreaming = useChatStore((state) => state.isStreaming);
  const addMessage = useChatStore((state) => state.addMessage);
  const updateLastMessage = useChatStore((state) => state.updateLastMessage);
  const setIsStreaming = useChatStore((state) => state.setIsStreaming);
  
  const currentModelName = AVAILABLE_MODELS.find(m => m.id === currentModel)?.name || currentModel;
  
  // Auto-scroll to bottom
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);
  
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || isStreaming) return;
    
    const userMessage: Message = {
      id: Date.now().toString(),
      role: 'user',
      content: input.trim(),
      timestamp: Date.now()
    };
    
    addMessage(userMessage);
    setInput('');
    setError(null);
    
    // Add empty assistant message for streaming
    const assistantMessage: Message = {
      id: (Date.now() + 1).toString(),
      role: 'assistant',
      content: '',
      timestamp: Date.now()
    };
    addMessage(assistantMessage);
    setIsStreaming(true);
    
    try {
      const api = new NvidiaApiService();

      const apiMessages: ChatCompletionMessage[] = [
        ...messages.map((m) => ({ role: m.role, content: m.content } as const)),
        { role: 'user', content: userMessage.content }
      ];
      
      let fullContent = '';
      for await (const chunk of api.streamChatCompletion(apiMessages, currentModel, setError)) {
        fullContent += chunk;
        updateLastMessage(fullContent);
      }
    } catch (err) {
      console.error('Chat error:', err);
      if (!error) {
        setError(err instanceof Error ? err.message : 'Failed to get response');
      }
    } finally {
      setIsStreaming(false);
    }
  };
  
  return (
    <div className="flex flex-col h-full">
      {/* Messages */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {messages.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-gray-500">
            <Bot size={48} className="mb-4 text-nvidia-green" />
            <h3 className="text-xl font-semibold mb-2">Welcome to NVIDIA Chat</h3>
            <p className="text-center max-w-md">
              Start a conversation with state-of-the-art AI models powered by NVIDIA NIM.
            </p>
            <div className="mt-4 text-sm text-gray-600">
              Current model: <span className="text-nvidia-green">{currentModelName}</span>
            </div>
          </div>
        ) : (
          messages.map((message) => (
            <div
              key={message.id}
              className={`flex gap-4 ${message.role === 'assistant' ? 'bg-nvidia-darker/50' : ''} p-4 rounded-lg`}
            >
              <div className={`flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center ${
                message.role === 'assistant' 
                  ? 'bg-nvidia-green/20 text-nvidia-green' 
                  : 'bg-gray-700 text-gray-300'
              }`}>
                {message.role === 'assistant' ? <Bot size={18} /> : <User size={18} />}
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium text-sm">
                    {message.role === 'assistant' ? 'AI' : 'You'}
                  </span>
                  <span className="text-xs text-gray-500">
                    {new Date(message.timestamp).toLocaleTimeString()}
                  </span>
                </div>
                <div className="prose prose-invert max-w-none whitespace-pre-wrap">
                  {message.content || (isStreaming && message.role === 'assistant' ? (
                    <span className="animate-pulse">▌</span>
                  ) : '')}
                </div>
              </div>
            </div>
          ))
        )}
        <div ref={messagesEndRef} />
      </div>
      
      {/* Input Area */}
      <div className="border-t border-gray-800 p-4 bg-nvidia-darker">
        {error && (
          <div className="mb-3 flex items-center gap-2 text-red-400 text-sm bg-red-400/10 p-3 rounded-lg">
            <AlertCircle size={16} />
            <span>{error}</span>
          </div>
        )}
        
        <form onSubmit={handleSubmit} className="flex gap-2">
          <input
            type="text"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder="Type your message..."
            disabled={isStreaming}
            className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-4 py-3 
              focus:outline-none focus:border-nvidia-green focus:ring-1 focus:ring-nvidia-green
              disabled:opacity-50 disabled:cursor-not-allowed"
          />
          <button
            type="submit"
            disabled={!input.trim() || isStreaming}
            className="bg-nvidia-green hover:bg-nvidia-green-hover text-black font-medium 
              px-4 py-2 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed
              flex items-center gap-2"
          >
            {isStreaming ? (
              <Loader2 size={20} className="animate-spin" />
            ) : (
              <Send size={20} />
            )}
          </button>
        </form>
        
        <div className="mt-2 text-xs text-gray-600 text-center">
          Using {currentModelName} • Powered by NVIDIA NIM
        </div>
      </div>
    </div>
  );
}
