import { ClassConfig, ShortcutSettings } from './types';

export const CLASS_SETTINGS_STORAGE_KEY = 'visionqc-class-settings-v1';
export const SHORTCUT_SETTINGS_STORAGE_KEY = 'visionqc-shortcut-settings-v1';
export const UNCLASSIFIED_STATUS = 'UNCLASSIFIED';

export const createDefaultShortcutSettings = (): ShortcutSettings => ({
  multiLabelModifierKey: 'M'
});

export const isAllowedLabelHotkey = (value: string) => /^[A-Z0-9]$/.test(String(value || '').trim().toUpperCase());

export const normalizeHotkey = (value: string) => String(value || '').trim().slice(0, 1).toUpperCase();

export const normalizeShortcutSettings = (settings: Partial<ShortcutSettings> | null | undefined): ShortcutSettings => {
  const defaults = createDefaultShortcutSettings();
  const key = normalizeHotkey(settings?.multiLabelModifierKey || defaults.multiLabelModifierKey);
  return {
    multiLabelModifierKey: isAllowedLabelHotkey(key) ? key : defaults.multiLabelModifierKey
  };
};

export const createDefaultClassConfigs = (): ClassConfig[] => [
  { id: 'OK', label: 'OK', hotkey: 'O', kind: 'ok', enabled: true, tone: 'emerald' },
  { id: 'CRACK', label: 'CRACK', hotkey: '1', kind: 'ng', enabled: true, tone: 'red' },
  { id: 'ETC', label: 'ETC', hotkey: '2', kind: 'ng', enabled: true, tone: 'slate' },
  { id: 'FOIL_DAMAGE', label: 'FOIL', hotkey: '3', kind: 'ng', enabled: true, tone: 'orange' },
  { id: 'MAIN_WELDING', label: 'WELD', hotkey: '4', kind: 'ng', enabled: true, tone: 'indigo' },
  { id: 'NO_TAB', label: 'TAB', hotkey: '5', kind: 'ng', enabled: true, tone: 'pink' },
  { id: 'SPATTER', label: 'SPAT', hotkey: '6', kind: 'ng', enabled: true, tone: 'amber' },
  { id: 'TRIMMING', label: 'TRIM', hotkey: '7', kind: 'ng', enabled: true, tone: 'purple' }
];

export const normalizeClassConfigs = (configs: ClassConfig[] | null | undefined): ClassConfig[] => {
  const defaults = createDefaultClassConfigs();
  if (!configs || !Array.isArray(configs) || configs.length === 0) return defaults;

  const sanitized = configs
    .filter((item) => item && typeof item.id === 'string')
    .map((item, index) => ({
      id: item.id,
      label: String(item.label || item.id).trim() || item.id,
      hotkey: normalizeHotkey(String(item.hotkey || '')),
      kind: item.kind === 'ok' ? 'ok' : 'ng',
      enabled: item.enabled !== false,
      tone: item.tone || defaults[index % defaults.length].tone || 'slate'
    }));

  const okConfigs = sanitized.filter((item) => item.kind === 'ok');
  if (okConfigs.length === 0) {
    sanitized.unshift(defaults[0]);
  }

  return sanitized;
};

export const getClassConfigById = (classConfigs: ClassConfig[], status: string) => {
  return classConfigs.find((item) => item.id === status);
};

export const getClassLabel = (status: string, classConfigs: ClassConfig[]) => {
  if (status === UNCLASSIFIED_STATUS) return 'PENDING';
  return getClassConfigById(classConfigs, status)?.label || status;
};

export const getClassHotkeyLabel = (hotkey: string) => normalizeHotkey(hotkey);

export const createClassIdFromLabel = (label: string) => {
  const base = label
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '') || 'NEW_CLASS';

  return `${base}_${Math.random().toString(36).slice(2, 6).toUpperCase()}`;
};

export const getToneClasses = (tone: ClassConfig['tone'], kind: ClassConfig['kind']) => {
  if (kind === 'ok') {
    return {
      chip: 'bg-emerald-500/30 text-emerald-300',
      text: 'text-emerald-400',
      button: 'border-emerald-500 bg-emerald-500/20 text-emerald-400 shadow-[0_0_15px_rgba(16,185,129,0.1)]',
      buttonIdle: 'border-slate-700 bg-slate-800/50 hover:border-emerald-500/50',
      badge: 'bg-emerald-500/20 text-emerald-400 border-emerald-500/50'
    };
  }

  const map: Record<ClassConfig['tone'], { chip: string; text: string }> = {
    emerald: { chip: 'bg-emerald-500/30 text-emerald-300', text: 'text-emerald-400' },
    red: { chip: 'bg-red-500/30 text-red-300', text: 'text-red-400' },
    orange: { chip: 'bg-orange-500/30 text-orange-300', text: 'text-orange-400' },
    indigo: { chip: 'bg-indigo-500/30 text-indigo-300', text: 'text-indigo-400' },
    pink: { chip: 'bg-pink-500/30 text-pink-300', text: 'text-pink-400' },
    amber: { chip: 'bg-amber-500/30 text-amber-300', text: 'text-amber-400' },
    purple: { chip: 'bg-purple-500/30 text-purple-300', text: 'text-purple-400' },
    slate: { chip: 'bg-slate-500/30 text-slate-300', text: 'text-slate-400' }
  };

  const current = map[tone] || map.slate;
  return {
    chip: current.chip,
    text: current.text,
    button: 'border-red-500 bg-red-500/20 text-red-400 shadow-[0_0_15px_rgba(239,68,68,0.1)]',
    buttonIdle: 'border-slate-800 bg-slate-900/50 hover:border-red-500/30',
    badge: 'bg-red-500/20 text-red-400 border-red-500/50'
  };
};

export const sanitizeExportFolderName = (label: string) => label.replace(/[\\/:*?"<>|]/g, '_');
