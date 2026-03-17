import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { AVAILABLE_MODELS, type ChatSession, type Message } from '../types';

interface ChatState {
  // Current session
  currentSessionId: string | null;
  messages: Message[];
  currentModel: string;
  isStreaming: boolean;
  
  // Settings
  // API key is configured server-side
  
  // History
  sessions: ChatSession[];
  
  // Actions
  setCurrentModel: (model: string) => void;
  addMessage: (message: Message) => void;
  updateLastMessage: (content: string) => void;
  setIsStreaming: (streaming: boolean) => void;
  createNewSession: () => void;
  loadSession: (sessionId: string) => void;
  deleteSession: (sessionId: string) => void;
  clearMessages: () => void;
}

const createNewSessionId = () => Date.now().toString();

export const useChatStore = create<ChatState>()(
  persist(
    (set, get) => ({
      currentSessionId: null,
      messages: [],
      currentModel: AVAILABLE_MODELS[0].id,
      isStreaming: false,
      sessions: [],
      
      setCurrentModel: (model) => set({ currentModel: model }),
      
      addMessage: (message) => {
        const { messages, currentSessionId, sessions } = get();
        const newMessages = [...messages, message];
        
        // Update or create session
        let newSessions = sessions.filter(s => s.id !== currentSessionId);
        const session: ChatSession = {
          id: currentSessionId || createNewSessionId(),
          title: newMessages[0]?.content.slice(0, 50) || 'New Chat',
          messages: newMessages,
          model: get().currentModel,
          createdAt: Date.now(),
          updatedAt: Date.now()
        };
        newSessions.unshift(session);
        
        set({
          messages: newMessages,
          currentSessionId: session.id,
          sessions: newSessions.slice(0, 50) // Keep last 50 sessions
        });
      },
      
      updateLastMessage: (content) => {
        const { messages, currentSessionId, sessions } = get();
        if (messages.length === 0) return;
        
        const newMessages = [...messages];
        newMessages[newMessages.length - 1].content = content;
        
        // Update session
        const newSessions = sessions.map(s => 
          s.id === currentSessionId 
            ? { ...s, messages: newMessages, updatedAt: Date.now() }
            : s
        );
        
        set({ messages: newMessages, sessions: newSessions });
      },
      
      setIsStreaming: (streaming) => set({ isStreaming: streaming }),
      
      createNewSession: () => set({
        currentSessionId: createNewSessionId(),
        messages: []
      }),
      
      loadSession: (sessionId) => {
        const session = get().sessions.find(s => s.id === sessionId);
        if (session) {
          set({
            currentSessionId: sessionId,
            messages: session.messages,
            currentModel: session.model
          });
        }
      },
      
      deleteSession: (sessionId) => {
        const newSessions = get().sessions.filter(s => s.id !== sessionId);
        set({ sessions: newSessions });
        if (get().currentSessionId === sessionId) {
          set({ currentSessionId: null, messages: [] });
        }
      },
      
      clearMessages: () => set({ messages: [], currentSessionId: null })
    }),
    {
      name: 'nvidia-chat-storage',
      partialize: (state) => ({
        sessions: state.sessions,
        currentModel: state.currentModel
      })
    }
  )
);
