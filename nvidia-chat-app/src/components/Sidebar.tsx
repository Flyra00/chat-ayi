import { useChatStore } from '../stores/chatStore';
import { Plus, MessageSquare, Trash2 } from 'lucide-react';

export function Sidebar() {
  const sessions = useChatStore((state) => state.sessions);
  const currentSessionId = useChatStore((state) => state.currentSessionId);
  const createNewSession = useChatStore((state) => state.createNewSession);
  const loadSession = useChatStore((state) => state.loadSession);
  const deleteSession = useChatStore((state) => state.deleteSession);
  
  return (
    <div className="flex flex-col h-[calc(100%-65px)]">
      {/* New Chat Button */}
      <div className="p-4">
        <button
          onClick={createNewSession}
          className="w-full flex items-center gap-2 bg-nvidia-green hover:bg-nvidia-green-hover 
            text-black font-medium py-2 px-4 rounded-lg transition-colors"
        >
          <Plus size={18} />
          New Chat
        </button>
      </div>
      
      {/* Chat History */}
      <div className="flex-1 overflow-y-auto px-2">
        <div className="text-xs font-medium text-gray-500 uppercase px-2 mb-2">
          Recent Chats
        </div>
        
        {sessions.length === 0 ? (
          <div className="text-gray-600 text-sm px-2 py-4 text-center">
            No chat history yet
          </div>
        ) : (
          <div className="space-y-1">
            {sessions.map((session) => (
              <div
                key={session.id}
                className={`group flex items-center gap-2 p-2 rounded-lg cursor-pointer
                  transition-colors ${
                    currentSessionId === session.id
                      ? 'bg-gray-800 text-white'
                      : 'hover:bg-gray-800/50 text-gray-400'
                  }`}
              >
                <div 
                  className="flex-1 flex items-center gap-2 min-w-0"
                  onClick={() => loadSession(session.id)}
                >
                  <MessageSquare size={16} className="flex-shrink-0" />
                  <span className="truncate text-sm">
                    {session.title}
                  </span>
                </div>
                
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    deleteSession(session.id);
                  }}
                  className="opacity-0 group-hover:opacity-100 p-1 rounded 
                    hover:bg-red-500/20 hover:text-red-400 transition-all"
                >
                  <Trash2 size={14} />
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
      
      {/* Footer */}
      <div className="p-4 border-t border-gray-800 text-xs text-gray-600">
        <div>NVIDIA NIM Chat</div>
        <div>v1.0.0</div>
      </div>
    </div>
  );
}
