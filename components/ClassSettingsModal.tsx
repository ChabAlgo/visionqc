import React, { useEffect, useMemo, useState } from 'react';
import { Download, Plus, RotateCcw, Save, Trash2, Upload, X } from 'lucide-react';
import { ClassConfig, ShortcutSettings } from '../types';
import {
  createClassIdFromLabel,
  createDefaultClassConfigs,
  createDefaultShortcutSettings,
  getClassHotkeyLabel,
  isAllowedLabelHotkey,
  normalizeHotkey,
  normalizeClassConfigs,
  normalizeShortcutSettings
} from '../classSettings';

interface ClassSettingsModalProps {
  isOpen: boolean;
  classConfigs: ClassConfig[];
  shortcutSettings: ShortcutSettings;
  onClose: () => void;
  onSave: (nextConfigs: ClassConfig[], nextShortcutSettings: ShortcutSettings) => void;
}

const availableTones: ClassConfig['tone'][] = ['red', 'orange', 'indigo', 'pink', 'amber', 'purple', 'slate'];

const invalidHotkeyMessage = '단축키는 숫자(0~9) 또는 영문자(A~Z) 1글자만 사용할 수 있습니다.';

const CLASS_SETTINGS_PROFILE_STORAGE_KEY = 'visionqc-class-settings-profiles-v1';
const PROFILE_SLOT_COUNT = 5;

type ClassSettingsProfile = {
  classConfigs: ClassConfig[];
  shortcutSettings: ShortcutSettings;
  savedAt: string;
};

type ClassSettingsProfiles = Record<string, ClassSettingsProfile | undefined>;

const createProfileSlotId = (index: number) => `class${index}`;

const readProfiles = (): ClassSettingsProfiles => {
  if (typeof window === 'undefined') return {};
  try {
    const raw = window.localStorage.getItem(CLASS_SETTINGS_PROFILE_STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object') return {};
    return parsed as ClassSettingsProfiles;
  } catch (error) {
    console.warn('Failed to read class setting profiles:', error);
    return {};
  }
};

const writeProfiles = (profiles: ClassSettingsProfiles) => {
  if (typeof window === 'undefined') return;
  window.localStorage.setItem(CLASS_SETTINGS_PROFILE_STORAGE_KEY, JSON.stringify(profiles));
};

const formatSavedAt = (value?: string) => {
  if (!value) return 'EMPTY';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'SAVED';
  return date.toLocaleString();
};

const ClassSettingsModal: React.FC<ClassSettingsModalProps> = ({
  isOpen,
  classConfigs,
  shortcutSettings,
  onClose,
  onSave
}) => {
  const [draftConfigs, setDraftConfigs] = useState<ClassConfig[]>([]);
  const [draftShortcutSettings, setDraftShortcutSettings] = useState<ShortcutSettings>(() => createDefaultShortcutSettings());
  const [errorMessage, setErrorMessage] = useState('');
  const [profiles, setProfiles] = useState<ClassSettingsProfiles>({});

  useEffect(() => {
    if (isOpen) {
      setDraftConfigs(classConfigs.map((item) => ({ ...item })));
      setDraftShortcutSettings(normalizeShortcutSettings(shortcutSettings));
      setProfiles(readProfiles());
      setErrorMessage('');
    }
  }, [classConfigs, shortcutSettings, isOpen]);

  const okConfig = useMemo(() => draftConfigs.find((item) => item.kind === 'ok'), [draftConfigs]);
  const ngConfigs = useMemo(() => draftConfigs.filter((item) => item.kind === 'ng'), [draftConfigs]);

  if (!isOpen) return null;

  const rejectInvalidHotkey = () => {
    setErrorMessage(invalidHotkeyMessage);
    alert(invalidHotkeyMessage);
  };

  const toAllowedHotkey = (raw: string) => {
    const key = normalizeHotkey(raw);
    if (!key) return '';
    if (!isAllowedLabelHotkey(key)) return null;
    return key;
  };

  const updateConfig = (id: string, patch: Partial<ClassConfig>) => {
    setDraftConfigs((prev) => prev.map((item) => (item.id === id ? { ...item, ...patch } : item)));
    setErrorMessage('');
  };

  const updateConfigHotkey = (id: string, raw: string) => {
    const key = toAllowedHotkey(raw);
    if (key === null) {
      rejectInvalidHotkey();
      return;
    }
    updateConfig(id, { hotkey: key });
  };

  const updateMultiModifierKey = (raw: string) => {
    const key = toAllowedHotkey(raw);
    if (key === null) {
      rejectInvalidHotkey();
      return;
    }
    setDraftShortcutSettings({ multiLabelModifierKey: key });
    setErrorMessage('');
  };

  const handleHotkeyKeyDown = (event: React.KeyboardEvent<HTMLInputElement>, onValidKey: (key: string) => void) => {
    const allowedControlKeys = ['Backspace', 'Delete', 'ArrowLeft', 'ArrowRight', 'Tab'];
    if (allowedControlKeys.includes(event.key)) return;

    if (event.key.length === 1) {
      const key = event.key.toUpperCase();
      if (!isAllowedLabelHotkey(key)) {
        event.preventDefault();
        rejectInvalidHotkey();
        return;
      }
      event.preventDefault();
      onValidKey(key);
      return;
    }

    event.preventDefault();
    rejectInvalidHotkey();
  };

  const handleAddClass = () => {
    const newIndex = draftConfigs.filter((item) => item.kind === 'ng').length + 1;
    setDraftConfigs((prev) => [
      ...prev,
      {
        id: createClassIdFromLabel(`CLASS_${newIndex}`),
        label: `CLASS_${newIndex}`,
        hotkey: '',
        kind: 'ng',
        enabled: true,
        tone: availableTones[(newIndex - 1) % availableTones.length]
      }
    ]);
    setErrorMessage('');
  };

  const handleDeleteClass = (id: string) => {
    setDraftConfigs((prev) => prev.filter((item) => item.id !== id));
    setErrorMessage('');
  };

  const handleRestoreDefaults = () => {
    setDraftConfigs(createDefaultClassConfigs());
    setDraftShortcutSettings(createDefaultShortcutSettings());
    setErrorMessage('');
  };

  const validateConfigs = () => {
    const trimmed = draftConfigs.map((item) => ({
      ...item,
      label: item.label.trim(),
      hotkey: getClassHotkeyLabel(item.hotkey)
    }));
    const nextShortcutSettings = normalizeShortcutSettings(draftShortcutSettings);

    if (trimmed.some((item) => !item.label)) {
      return { message: '클래스 이름은 비워둘 수 없습니다.', configs: trimmed, shortcutSettings: nextShortcutSettings };
    }

    if (trimmed.some((item) => !item.hotkey)) {
      return { message: '모든 클래스에 단축키 1개를 지정해야 합니다.', configs: trimmed, shortcutSettings: nextShortcutSettings };
    }

    if (trimmed.some((item) => !isAllowedLabelHotkey(item.hotkey))) {
      return { message: invalidHotkeyMessage, configs: trimmed, shortcutSettings: nextShortcutSettings };
    }

    if (!isAllowedLabelHotkey(nextShortcutSettings.multiLabelModifierKey)) {
      return { message: invalidHotkeyMessage, configs: trimmed, shortcutSettings: nextShortcutSettings };
    }

    const hotkeys = trimmed.map((item) => item.hotkey.toUpperCase());
    if (new Set(hotkeys).size !== hotkeys.length) {
      return { message: '단축키가 중복되었습니다. 서로 다른 키를 지정해 주세요.', configs: trimmed, shortcutSettings: nextShortcutSettings };
    }

    if (hotkeys.includes(nextShortcutSettings.multiLabelModifierKey.toUpperCase())) {
      return { message: `Multi Label Key(${nextShortcutSettings.multiLabelModifierKey})가 클래스 단축키와 중복됩니다. 다른 키로 지정해 주세요.`, configs: trimmed, shortcutSettings: nextShortcutSettings };
    }

    const okCount = trimmed.filter((item) => item.kind === 'ok').length;
    if (okCount !== 1) {
      return { message: 'OK 클래스는 정확히 1개여야 합니다.', configs: trimmed, shortcutSettings: nextShortcutSettings };
    }

    return { message: '', configs: trimmed, shortcutSettings: nextShortcutSettings };
  };


  const handleSaveProfile = (slotIndex: number) => {
    const validation = validateConfigs();
    if (validation.message) {
      setErrorMessage(validation.message);
      alert(`Class ${slotIndex} 저장 실패: ${validation.message}`);
      return;
    }

    const slotId = createProfileSlotId(slotIndex);
    const nextProfiles: ClassSettingsProfiles = {
      ...profiles,
      [slotId]: {
        classConfigs: validation.configs.map((item) => ({ ...item })),
        shortcutSettings: { ...validation.shortcutSettings },
        savedAt: new Date().toISOString()
      }
    };

    writeProfiles(nextProfiles);
    setProfiles(nextProfiles);
    setDraftConfigs(validation.configs.map((item) => ({ ...item })));
    setDraftShortcutSettings(validation.shortcutSettings);
    setErrorMessage('');
    alert(`Class ${slotIndex} 설정을 저장했습니다.`);
  };

  const handleLoadProfile = (slotIndex: number) => {
    const slotId = createProfileSlotId(slotIndex);
    const profile = profiles[slotId];
    if (!profile) {
      alert(`Class ${slotIndex}에 저장된 설정이 없습니다.`);
      return;
    }

    const nextConfigs = normalizeClassConfigs(profile.classConfigs).map((item) => ({ ...item }));
    const nextShortcutSettings = normalizeShortcutSettings(profile.shortcutSettings);
    setDraftConfigs(nextConfigs);
    setDraftShortcutSettings(nextShortcutSettings);
    setErrorMessage('');
    alert(`Class ${slotIndex} 설정을 불러왔습니다. 적용하려면 하단 Save를 눌러 주세요.`);
  };

  const handleSave = () => {
    const validation = validateConfigs();
    if (validation.message) {
      setErrorMessage(validation.message);
      return;
    }

    setDraftConfigs(validation.configs);
    setDraftShortcutSettings(validation.shortcutSettings);
    onSave(validation.configs, validation.shortcutSettings);
    onClose();
  };

  const renderRow = (item: ClassConfig, isDeletable: boolean) => {
    return (
      <div key={item.id} className="grid grid-cols-[1.2fr_120px_110px_70px] gap-3 rounded-2xl border border-slate-800 bg-slate-950/80 p-4">
        <div>
          <p className="mb-2 text-[10px] font-black uppercase tracking-[0.22em] text-slate-500">Name</p>
          <input
            value={item.label}
            onChange={(e) => updateConfig(item.id, { label: e.target.value })}
            className="w-full rounded-xl border border-slate-700 bg-slate-900 px-3 py-2.5 text-sm text-white outline-none focus:border-blue-500/50"
          />
        </div>

        <div>
          <p className="mb-2 text-[10px] font-black uppercase tracking-[0.22em] text-slate-500">Hotkey</p>
          <input
            value={item.hotkey}
            maxLength={1}
            onKeyDown={(e) => handleHotkeyKeyDown(e, (key) => updateConfigHotkey(item.id, key))}
            onChange={(e) => updateConfigHotkey(item.id, e.target.value)}
            className="w-full rounded-xl border border-slate-700 bg-slate-900 px-3 py-2.5 text-center text-sm font-black uppercase text-white outline-none focus:border-blue-500/50"
          />
        </div>

        <div>
          <p className="mb-2 text-[10px] font-black uppercase tracking-[0.22em] text-slate-500">Type</p>
          <div className={`rounded-xl border px-3 py-2.5 text-center text-sm font-black uppercase ${item.kind === 'ok' ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300' : 'border-red-500/20 bg-red-500/10 text-red-300'}`}>
            {item.kind}
          </div>
        </div>

        <div>
          <p className="mb-2 text-[10px] font-black uppercase tracking-[0.22em] text-slate-500">Delete</p>
          <button
            onClick={() => handleDeleteClass(item.id)}
            disabled={!isDeletable}
            className="flex w-full items-center justify-center rounded-xl border border-slate-700 bg-slate-900 px-3 py-2.5 text-slate-300 transition-colors hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-30"
          >
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      </div>
    );
  };

  return (
    <div className="fixed inset-0 z-[130] flex items-center justify-center bg-slate-950/80 p-6 backdrop-blur-md">
      <div className="flex max-h-[92vh] w-full max-w-5xl flex-col overflow-hidden rounded-3xl border border-slate-800 bg-slate-950 shadow-2xl shadow-black/40">
        <div className="flex items-center justify-between border-b border-slate-800 px-6 py-5">
          <div>
            <p className="text-[10px] font-black uppercase tracking-[0.28em] text-slate-500">Customization</p>
            <h2 className="mt-1 text-2xl font-black tracking-tight text-white">Class Settings</h2>
          </div>
          <button
            onClick={onClose}
            className="rounded-xl border border-slate-700 bg-slate-900 p-2.5 text-slate-400 transition-colors hover:text-white"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-6 py-6">
          <div className="space-y-6">
            <div className="rounded-2xl border border-blue-500/20 bg-blue-500/5 p-5">
              <div className="mb-4 flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs font-black uppercase tracking-[0.24em] text-blue-300">Class Presets</p>
                  <p className="mt-2 text-sm text-slate-400">현재 Class Settings의 라벨명, 단축키, Multi Label Key를 Class 1~5 슬롯에 저장/불러오기합니다.</p>
                  <p className="mt-1 text-xs text-slate-500">Load는 설정창 안의 임시값만 바꿉니다. 실제 적용하려면 하단 Save를 눌러 주세요.</p>
                </div>
              </div>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-5">
                {Array.from({ length: PROFILE_SLOT_COUNT }, (_, index) => {
                  const slotIndex = index + 1;
                  const profile = profiles[createProfileSlotId(slotIndex)];
                  return (
                    <div key={slotIndex} className="rounded-2xl border border-slate-800 bg-slate-950/80 p-3">
                      <div className="mb-3">
                        <p className="text-sm font-black uppercase text-white">Class {slotIndex}</p>
                        <p className={`mt-1 truncate text-[10px] font-bold uppercase ${profile ? 'text-blue-300' : 'text-slate-600'}`}>{formatSavedAt(profile?.savedAt)}</p>
                      </div>
                      <div className="grid grid-cols-2 gap-2">
                        <button
                          type="button"
                          onClick={() => handleLoadProfile(slotIndex)}
                          disabled={!profile}
                          className="flex items-center justify-center gap-1 rounded-xl border border-slate-700 bg-slate-900 px-2 py-2 text-[10px] font-black uppercase text-slate-200 transition-colors hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-30"
                        >
                          <Download className="h-3 w-3" /> Load
                        </button>
                        <button
                          type="button"
                          onClick={() => handleSaveProfile(slotIndex)}
                          className="flex items-center justify-center gap-1 rounded-xl border border-blue-500/40 bg-blue-500/15 px-2 py-2 text-[10px] font-black uppercase text-blue-200 transition-colors hover:bg-blue-500/25"
                        >
                          <Upload className="h-3 w-3" /> Save
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            <div className="rounded-2xl border border-cyan-500/20 bg-cyan-500/5 p-5">
              <div className="grid grid-cols-[1fr_150px] gap-4 items-end">
                <div>
                  <p className="text-xs font-black uppercase tracking-[0.24em] text-cyan-400">Multi Label Shortcut</p>
                  <p className="mt-2 text-sm text-slate-400">이 키를 누른 상태에서 라벨 단축키를 누르면 다중 라벨을 추가/해제합니다. 예: {draftShortcutSettings.multiLabelModifierKey || 'M'} + 1</p>
                  <p className="mt-1 text-xs text-slate-500">숫자(0~9) 또는 영문자(A~Z) 1글자만 허용됩니다. 클래스 단축키와 중복되면 저장할 수 없습니다.</p>
                </div>
                <div>
                  <p className="mb-2 text-[10px] font-black uppercase tracking-[0.22em] text-slate-500">Multi Label Key</p>
                  <input
                    value={draftShortcutSettings.multiLabelModifierKey}
                    maxLength={1}
                    onKeyDown={(e) => handleHotkeyKeyDown(e, updateMultiModifierKey)}
                    onChange={(e) => updateMultiModifierKey(e.target.value)}
                    className="w-full rounded-xl border border-cyan-500/40 bg-slate-900 px-3 py-3 text-center text-lg font-black uppercase text-cyan-200 outline-none focus:border-cyan-300"
                  />
                </div>
              </div>
            </div>

            <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-5">
              <div className="mb-4 flex items-center justify-between gap-4">
                <div>
                  <p className="text-xs font-black uppercase tracking-[0.24em] text-slate-500">OK Class</p>
                  <p className="mt-2 text-sm text-slate-400">OK 클래스는 1개만 유지됩니다.</p>
                </div>
              </div>
              {okConfig ? renderRow(okConfig, false) : null}
            </div>

            <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-5">
              <div className="mb-4 flex items-center justify-between gap-4">
                <div>
                  <p className="text-xs font-black uppercase tracking-[0.24em] text-slate-500">NG Classes</p>
                  <p className="mt-2 text-sm text-slate-400">이름과 단축키를 수정하고, 필요한 항목을 추가/삭제하세요.</p>
                </div>
                <button
                  onClick={handleAddClass}
                  className="flex items-center gap-2 rounded-xl border border-blue-500/30 bg-blue-600/10 px-4 py-2.5 text-sm font-bold text-blue-300 transition-colors hover:bg-blue-600/20"
                >
                  <Plus className="h-4 w-4" /> Add Class
                </button>
              </div>

              <div className="space-y-3">
                {ngConfigs.map((item) => renderRow(item, true))}
              </div>
            </div>

            {errorMessage ? (
              <div className="rounded-2xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm font-medium text-red-300">
                {errorMessage}
              </div>
            ) : null}
          </div>
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-slate-800 px-6 py-5">
          <button
            onClick={handleRestoreDefaults}
            className="flex items-center gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-2.5 text-sm font-bold text-amber-300 transition-colors hover:bg-amber-500/20"
          >
            <RotateCcw className="h-4 w-4" /> Restore Defaults
          </button>

          <div className="flex items-center gap-3">
            <button
              onClick={onClose}
              className="rounded-xl border border-slate-700 bg-slate-900 px-4 py-2.5 text-sm font-bold text-slate-300 transition-colors hover:bg-slate-800"
            >
              Close
            </button>
            <button
              onClick={handleSave}
              className="flex items-center gap-2 rounded-xl bg-blue-600 px-4 py-2.5 text-sm font-black text-white transition-colors hover:bg-blue-500"
            >
              <Save className="h-4 w-4" /> Save
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ClassSettingsModal;
