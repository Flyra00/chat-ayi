import { useChatStore } from '../stores/chatStore';
import { AVAILABLE_MODELS } from '../types';
import { X, Cpu } from 'lucide-react';

interface SettingsPanelProps {
  onClose: () => void;
}

export function SettingsPanel({ onClose }: SettingsPanelProps) {
  const currentModel = useChatStore((state) => state.currentModel);
  const setCurrentModel = useChatStore((state) => state.setCurrentModel);
  
  const handleSave = () => {
    onClose();
  };
  
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
      <div className="bg-nvidia-darker border border-gray-800 rounded-xl w-full max-w-md shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-gray-800">
          <h2 className="text-lg font-semibold">Settings</h2>
          <button 
            onClick={onClose}
            className="text-gray-400 hover:text-white transition-colors"
          >
            <X size={20} />
          </button>
        </div>
        
        {/* Content */}
        <div className="p-4 space-y-6">
          <div className="text-xs text-gray-500 bg-gray-800/40 border border-gray-800 rounded-lg p-3">
            API key is configured server-side (not stored in the browser).
          </div>
          
          {/* Model Selection */}
          <div>
            <label className="flex items-center gap-2 text-sm font-medium mb-2">
              <Cpu size={16} className="text-nvidia-green" />
              Model
            </label>
            <div className="space-y-2">
              {AVAILABLE_MODELS.map((model) => (
                <label
                  key={model.id}
                  className={`flex items-start gap-3 p-3 rounded-lg border cursor-pointer
                    transition-colors ${
                      currentModel === model.id
                        ? 'border-nvidia-green bg-nvidia-green/10'
                        : 'border-gray-700 hover:border-gray-600'
                    }`}
                >
                  <input
                    type="radio"
                    name="model"
                    value={model.id}
                    checked={currentModel === model.id}
                    onChange={(e) => setCurrentModel(e.target.value)}
                    className="mt-1"
                  />
                  <div className="flex-1">
                    <div className="font-medium text-sm">{model.name}</div>
                    <div className="text-xs text-gray-500">{model.description}</div>
                    <div className="text-xs text-gray-600 mt-1">
                      Max tokens: {model.maxTokens.toLocaleString()}
                    </div>
                  </div>
                </label>
              ))}
            </div>
          </div>
        </div>
        
        {/* Footer */}
        <div className="flex justify-end gap-2 p-4 border-t border-gray-800">
          <button
            onClick={onClose}
            className="px-4 py-2 rounded-lg text-sm font-medium text-gray-400 
              hover:text-white hover:bg-gray-800 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            className="px-4 py-2 rounded-lg text-sm font-medium bg-nvidia-green 
              hover:bg-nvidia-green-hover text-black transition-colors"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
