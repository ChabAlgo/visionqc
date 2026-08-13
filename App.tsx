import React, { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import {
  FolderOpen,
  CheckCircle2,
  LayoutGrid,
  Sparkles,
  FolderArchive,
  Loader2,
  FileJson,
  FileUp,
  ShieldAlert,
  FolderTree,
  AlertTriangle,
  Cpu,
  Hash,
  Settings2,
  Download
} from 'lucide-react';
import JSZip from 'jszip';
import { ClassConfig, ImageFile, ClassificationStats, ShortcutSettings } from './types';
import FileExplorer from './components/FileExplorer';
import ImageViewer from './components/ImageViewer';
import StatsHeader from './components/StatsHeader';
import CellIdImportModal, { CellIdImportSummary } from './components/CellIdImportModal';
import ClassSettingsModal from './components/ClassSettingsModal';
import { analyzeImage } from './services/geminiService';
import {
  CLASS_SETTINGS_STORAGE_KEY,
  SHORTCUT_SETTINGS_STORAGE_KEY,
  UNCLASSIFIED_STATUS,
  createDefaultClassConfigs,
  createDefaultShortcutSettings,
  getClassLabel,
  getToneClasses,
  normalizeClassConfigs,
  normalizeShortcutSettings,
  sanitizeExportFolderName
} from './classSettings';
import {
  PersistedWorkSession,
  PersistedWorkItem,
  clearActiveWorkSession,
  createWorkItemIdentity,
  getPersistedWorkItemSignature,
  imageToPersistedWorkItem,
  loadActiveWorkSession,
  replaceActiveWorkSession,
  saveActiveWorkSessionChanges
} from './services/workSessionService';

type ExportProgress = {
  current: number;
  total: number;
  stage: 'preparing' | 'copying' | 'logging';
  message: string;
};

type VisionQcJsonItem = {
  fileName?: string;
  status?: string;
  statusId?: string;
  statusLabel?: string;
  aiConfidence?: number;
  path?: string;
  classId?: string;
  internalStatusId?: string;
  labels?: unknown;
  labelIds?: unknown;
  classIds?: unknown;
};

type OrganizeMode = 'copy' | 'move';

const normalizeKeyboardKey = (value?: string) => String(value || '').trim().slice(0, 1).toUpperCase();

const imageFileNamePattern = /\.(png|jpe?g|bmp|gif|webp|tif?f)$/i;

const isImageLikeFile = (file: File) => file.type.startsWith('image/') || imageFileNamePattern.test(file.name);

const createImageEntry = (file: File, path?: string): ImageFile => ({
  id: Math.random().toString(36).substring(2, 11),
  file,
  path: path || file.name,
  // v3.9: previewUrl is created lazily in ImageViewer/FileExplorer to avoid thousands of persistent object URLs.
  previewUrl: '',
  status: UNCLASSIFIED_STATUS,
  labels: []
});

const normalizeLookupKey = (value?: string) => String(value || '').replace(/\\/g, '/').trim().toLowerCase();


const stablePathCollator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

const getStablePathForFile = (file: File) => normalizeLookupKey((file as File & { webkitRelativePath?: string }).webkitRelativePath || file.name);

const compareStableText = (a: string, b: string) => stablePathCollator.compare(a, b);

const sortFilesForStableLoad = (files: File[]) => [...files].sort((a, b) => (
  compareStableText(getStablePathForFile(a), getStablePathForFile(b))
  || compareStableText(normalizeLookupKey(a.name), normalizeLookupKey(b.name))
  || a.lastModified - b.lastModified
  || a.size - b.size
));

const sortSourceEntriesForStableLoad = (entries: SourceFileEntry[]) => [...entries].sort((a, b) => (
  compareStableText(normalizeLookupKey(a.relativePath), normalizeLookupKey(b.relativePath))
  || compareStableText(normalizeLookupKey(a.fileName), normalizeLookupKey(b.fileName))
));

const isPendingStatusText = (value?: string) => {
  const normalized = String(value || '').trim().toUpperCase();
  return normalized === '' || normalized === 'PENDING' || normalized === 'UNCLASSIFIED' || normalized === 'UNKNOWN';
};


const uniqueLabelIds = (labels: string[]) => {
  const seen = new Set<string>();
  const result: string[] = [];

  labels.forEach((label) => {
    const normalized = String(label || '').trim();
    if (!normalized || normalized === UNCLASSIFIED_STATUS || isPendingStatusText(normalized)) return;
    if (seen.has(normalized)) return;
    seen.add(normalized);
    result.push(normalized);
  });

  return result;
};

const getImageLabelIds = (img: ImageFile) => {
  const labels = Array.isArray(img.labels) && img.labels.length > 0
    ? img.labels
    : img.status !== UNCLASSIFIED_STATUS
      ? [img.status]
      : [];
  return uniqueLabelIds(labels);
};

const getRepresentativeStatus = (labels: string[]) => {
  const cleanLabels = uniqueLabelIds(labels);
  return cleanLabels[0] || UNCLASSIFIED_STATUS;
};

const labelsToDisplayText = (labelIds: string[], classConfigs: ClassConfig[]) => (
  labelIds.map((labelId) => getClassLabel(labelId, classConfigs)).join(' + ')
);

const coerceStringArray = (value: unknown): string[] => {
  if (Array.isArray(value)) return value.map((item) => String(item || '').trim()).filter(Boolean);
  if (typeof value === 'string') {
    return value
      .split(/[+,|;]/g)
      .map((item) => item.trim())
      .filter(Boolean);
  }
  return [];
};

const sanitizeFileName = (fileName: string) => fileName.replace(/[\\/:*?"<>|]/g, '_');

const splitFileName = (fileName: string) => {
  const dotIndex = fileName.lastIndexOf('.');
  if (dotIndex <= 0) return { base: fileName, ext: '' };
  return { base: fileName.slice(0, dotIndex), ext: fileName.slice(dotIndex) };
};

const makeUniqueFileName = (fileName: string, usedNames: Set<string>) => {
  const safeName = sanitizeFileName(fileName) || 'image';
  const { base, ext } = splitFileName(safeName);
  let candidate = safeName;
  let index = 2;

  while (usedNames.has(candidate.toLowerCase())) {
    candidate = `${base}_${index}${ext}`;
    index += 1;
  }

  usedNames.add(candidate.toLowerCase());
  return candidate;
};

const csvEscape = (value: unknown) => {
  const text = String(value ?? '');
  if (/[",\r\n]/.test(text)) return `"${text.replace(/"/g, '""')}"`;
  return text;
};

const waitForUi = () => new Promise((resolve) => setTimeout(resolve, 0));

const writeTextFileToDirectory = async (directoryHandle: any, fileName: string, content: string) => {
  const fileHandle = await directoryHandle.getFileHandle(fileName, { create: true });
  const writable = await fileHandle.createWritable();
  await writable.write(content);
  await writable.close();
};

const safeWriteTextFileToDirectory = async (directoryHandle: any, fileName: string, content: string) => {
  try {
    await writeTextFileToDirectory(directoryHandle, fileName, content);
    return { ok: true, message: '' };
  } catch (error: any) {
    console.error(`Failed to write ${fileName}`, error);
    return {
      ok: false,
      message: `${error?.name || 'Error'}: ${error?.message || String(error)}`
    };
  }
};


type SourceFileEntry = {
  fileName: string;
  relativePath: string;
  fileHandle: any;
  parentDirectoryHandle: any;
};

type ScanWarning = {
  path: string;
  name: string;
  message: string;
};

type DirectoryLoadOptions = {
  restoreSession?: PersistedWorkSession;
};

const getBaseNameFromPath = (value?: string) => {
  const normalized = String(value || '').replace(/\\/g, '/').trim();
  const parts = normalized.split('/').filter(Boolean);
  return parts.length ? parts[parts.length - 1] : normalized;
};

const isVisionQcLogFile = (fileName: string) => {
  const upper = fileName.toUpperCase();
  return upper.startsWith('_VISIONQC_') || upper === 'VISIONQC_ORGANIZE_LOG.JSON';
};

const scanSourceDirectory = async (
  directoryHandle: any,
  prefix = '',
  onTick?: (count: number) => void,
  counter: { count: number } = { count: 0 },
  warnings: ScanWarning[] = []
): Promise<SourceFileEntry[]> => {
  const results: SourceFileEntry[] = [];

  try {
    for await (const [name, handle] of directoryHandle.entries()) {
      if (handle.kind === 'file') {
        if (!isVisionQcLogFile(name)) {
          counter.count += 1;
          results.push({
            fileName: name,
            relativePath: `${prefix}${name}`,
            fileHandle: handle,
            parentDirectoryHandle: directoryHandle
          });
          if (counter.count % 200 === 0) onTick?.(counter.count);
        }
      } else if (handle.kind === 'directory') {
        const childPrefix = `${prefix}${name}/`;
        const childResults = await scanSourceDirectory(handle, childPrefix, onTick, counter, warnings);
        results.push(...childResults);
      }
    }
  } catch (error: any) {
    warnings.push({
      path: prefix || '(selected root)',
      name: error?.name || 'Error',
      message: error?.message || String(error)
    });
    console.warn('Skipped directory while scanning source folder:', prefix || '(selected root)', error);
  }

  return results;
};

const buildSourceFileIndex = (entries: SourceFileEntry[]) => {
  const byPath = new Map<string, SourceFileEntry>();
  const byName = new Map<string, SourceFileEntry[]>();

  entries.forEach((entry) => {
    byPath.set(normalizeLookupKey(entry.relativePath), entry);
    const nameKey = normalizeLookupKey(entry.fileName);
    const list = byName.get(nameKey) || [];
    list.push(entry);
    byName.set(nameKey, list);
  });

  return { byPath, byName };
};

const resolveSourceEntry = (
  itemPath: string | undefined,
  itemFileName: string,
  index: ReturnType<typeof buildSourceFileIndex>
) => {
  const pathKey = normalizeLookupKey(itemPath || itemFileName);
  const fileNameKey = normalizeLookupKey(itemFileName || getBaseNameFromPath(itemPath));

  if (pathKey && index.byPath.has(pathKey)) return index.byPath.get(pathKey)!;

  const candidates = index.byName.get(fileNameKey) || [];
  if (candidates.length === 1) return candidates[0];

  if (pathKey && candidates.length > 1) {
    const suffixMatched = candidates.find((entry) => {
      const entryPath = normalizeLookupKey(entry.relativePath);
      return pathKey.endsWith(`/${entryPath}`) || entryPath.endsWith(`/${pathKey}`);
    });
    if (suffixMatched) return suffixMatched;
  }

  return null;
};

const isSameDestination = (entry: SourceFileEntry, folderName: string, fileName: string) => {
  return normalizeLookupKey(entry.relativePath) === normalizeLookupKey(`${folderName}/${fileName}`);
};

const App: React.FC = () => {
  const [images, setImages] = useState<ImageFile[]>([]);
  const [currentIndex, setCurrentIndex] = useState<number>(0);
  const [isAnalyzing, setIsAnalyzing] = useState<boolean>(false);
  const [isSaving, setIsSaving] = useState<boolean>(false);
  const [isCellIdModalOpen, setIsCellIdModalOpen] = useState<boolean>(false);
  const [isClassSettingsOpen, setIsClassSettingsOpen] = useState<boolean>(false);
  const [classConfigs, setClassConfigs] = useState<ClassConfig[]>(() => createDefaultClassConfigs());
  const [shortcutSettings, setShortcutSettings] = useState<ShortcutSettings>(() => createDefaultShortcutSettings());
  const folderInputRef = useRef<HTMLInputElement>(null);
  const jsonInputRef = useRef<HTMLInputElement>(null);
  const imagesRef = useRef<ImageFile[]>([]);
  const sourceDirectoryHandleRef = useRef<any | null>(null);
  const sourceEntriesRef = useRef<SourceFileEntry[]>([]);
  const [sourceFolderName, setSourceFolderName] = useState<string>('');
  const [isOrganizeModeOpen, setIsOrganizeModeOpen] = useState<boolean>(false);
  const heldKeysRef = useRef<Set<string>>(new Set());
  const [isMultiModifierHeld, setIsMultiModifierHeld] = useState<boolean>(false);
  const [restoreCandidate, setRestoreCandidate] = useState<PersistedWorkSession | null>(null);
  const [isRestoringSession, setIsRestoringSession] = useState(false);
  const [workSessionInitialized, setWorkSessionInitialized] = useState(false);
  const [lastAutoSavedAt, setLastAutoSavedAt] = useState<number | null>(null);
  const [autoSaveError, setAutoSaveError] = useState('');
  const currentIndexRef = useRef(0);
  const sourceFolderNameRef = useRef('');
  const activeSessionIdRef = useRef<string | null>(null);
  const persistedSignatureRef = useRef<Map<string, string>>(new Map());
  const persistenceQueueRef = useRef<Promise<void>>(Promise.resolve());
  const autoSaveTimerRef = useRef<number | null>(null);
  const isReplacingSessionRef = useRef(false);

  const [uploadProgress, setUploadProgress] = useState<{
    current: number;
    total: number | null;
    stage: 'preparing' | 'initializing' | 'loading';
  } | null>(null);

  const [exportProgress, setExportProgress] = useState<ExportProgress | null>(null);

  const revokePreviewUrls = useCallback((targetImages: ImageFile[]) => {
    targetImages.forEach((img) => {
      try {
        if (img.previewUrl) URL.revokeObjectURL(img.previewUrl);
      } catch {
        // ignore
      }
    });
  }, []);

  useEffect(() => {
    imagesRef.current = images;
  }, [images]);

  useEffect(() => {
    currentIndexRef.current = currentIndex;
  }, [currentIndex]);

  useEffect(() => {
    sourceFolderNameRef.current = sourceFolderName;
  }, [sourceFolderName]);

  useEffect(() => {
    return () => {
      revokePreviewUrls(imagesRef.current);
    };
  }, [revokePreviewUrls]);

  useEffect(() => {
    try {
      const saved = localStorage.getItem(CLASS_SETTINGS_STORAGE_KEY);
      if (saved) {
        const parsed = JSON.parse(saved) as ClassConfig[];
        setClassConfigs(normalizeClassConfigs(parsed));
      }
    } catch (error) {
      console.error('Failed to load class settings:', error);
    }

    try {
      const savedShortcutSettings = localStorage.getItem(SHORTCUT_SETTINGS_STORAGE_KEY);
      if (savedShortcutSettings) {
        setShortcutSettings(normalizeShortcutSettings(JSON.parse(savedShortcutSettings)));
      }
    } catch (error) {
      console.error('Failed to load shortcut settings:', error);
    }
  }, []);

  useEffect(() => {
    localStorage.setItem(CLASS_SETTINGS_STORAGE_KEY, JSON.stringify(classConfigs));
  }, [classConfigs]);

  useEffect(() => {
    localStorage.setItem(SHORTCUT_SETTINGS_STORAGE_KEY, JSON.stringify(shortcutSettings));
  }, [shortcutSettings]);

  const okClassConfig = useMemo(
    () => classConfigs.find((item) => item.kind === 'ok' && item.enabled) || classConfigs[0],
    [classConfigs]
  );

  const ngClassConfigs = useMemo(
    () => classConfigs.filter((item) => item.kind === 'ng' && item.enabled),
    [classConfigs]
  );

  const stats: ClassificationStats = useMemo(() => {
    const counts: Record<string, number> = {};
    classConfigs.forEach((item) => {
      counts[item.id] = 0;
    });

    let ok = 0;
    let remaining = 0;
    images.forEach((img) => {
      const labelIds = getImageLabelIds(img);
      if (labelIds.length === 0) {
        remaining += 1;
        return;
      }

      labelIds.forEach((labelId) => {
        counts[labelId] = (counts[labelId] || 0) + 1;
      });

      if (okClassConfig?.id && labelIds.includes(okClassConfig.id)) ok += 1;
    });

    return {
      total: images.length,
      ok,
      remaining,
      counts
    };
  }, [classConfigs, images, okClassConfig?.id]);

  const buildImageEntries = useCallback((files: File[]) => {
    return files.map((file) => createImageEntry(
      file,
      (file as File & { webkitRelativePath?: string }).webkitRelativePath || file.name
    ));
  }, []);

  const replaceSourceDirectory = useCallback((directoryHandle: any | null, entries: SourceFileEntry[] = []) => {
    sourceDirectoryHandleRef.current = directoryHandle;
    sourceEntriesRef.current = entries;
    setSourceFolderName(directoryHandle?.name || '');
  }, []);

  const replaceImages = useCallback((newImages: ImageFile[], nextIndex = 0) => {
    setImages((prev) => {
      revokePreviewUrls(prev);
      return newImages;
    });
    const safeIndex = newImages.length === 0
      ? 0
      : Math.min(Math.max(0, nextIndex), newImages.length - 1);
    setCurrentIndex(safeIndex);
  }, [revokePreviewUrls]);

  const appendImages = useCallback((newImages: ImageFile[]) => {
    setImages((prev) => [...prev, ...newImages]);
  }, []);

  const applyPersistedItems = useCallback((newImages: ImageFile[], persistedItems: PersistedWorkItem[]) => {
    const persistedByIdentity = new Map(persistedItems.map((item) => [item.identity, item]));

    return newImages.map((image) => {
      const identity = createWorkItemIdentity(
        image.path,
        image.file.name,
        image.file.size,
        image.file.lastModified
      );
      const saved = persistedByIdentity.get(identity);
      if (!saved) return image;

      return {
        ...image,
        status: saved.status || UNCLASSIFIED_STATUS,
        labels: Array.isArray(saved.labels) ? [...saved.labels] : [],
        aiSuggestion: saved.aiSuggestion
      };
    });
  }, []);

  const activateWorkSession = useCallback(async (
    directoryHandle: any,
    sessionImages: ImageFile[],
    sessionIndex: number,
    existingSessionId?: string
  ) => {
    const sessionId = existingSessionId || `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
    const items = sessionImages.map(imageToPersistedWorkItem);
    const now = Date.now();

    isReplacingSessionRef.current = true;
    try {
      await replaceActiveWorkSession({
        sessionId,
        sourceMode: 'directory',
        folderName: directoryHandle?.name || '',
        currentIndex: sessionIndex,
        total: sessionImages.length,
        updatedAt: now,
        directoryHandle
      }, items);

      activeSessionIdRef.current = sessionId;
      persistedSignatureRef.current = new Map(
        items.map((item) => [item.identity, getPersistedWorkItemSignature(item)])
      );
      setLastAutoSavedAt(now);
      setAutoSaveError('');
    } catch (error) {
      console.error('Failed to initialize work session:', error);
      activeSessionIdRef.current = null;
      persistedSignatureRef.current.clear();
      setAutoSaveError('자동 저장 초기화 실패');
    } finally {
      isReplacingSessionRef.current = false;
    }
  }, []);

  const disableWorkSessionPersistence = useCallback(async () => {
    activeSessionIdRef.current = null;
    persistedSignatureRef.current.clear();
    setLastAutoSavedAt(null);
    try {
      await clearActiveWorkSession();
    } catch (error) {
      console.error('Failed to clear work session:', error);
    }
  }, []);

  const persistCurrentSession = useCallback(async () => {
    const sessionId = activeSessionIdRef.current;
    const directoryHandle = sourceDirectoryHandleRef.current;
    if (!sessionId || !directoryHandle || isReplacingSessionRef.current) return;

    const currentImages = imagesRef.current;
    const items = currentImages.map(imageToPersistedWorkItem);
    const changedItems = items.filter((item) => (
      persistedSignatureRef.current.get(item.identity) !== getPersistedWorkItemSignature(item)
    ));
    const now = Date.now();

    try {
      await saveActiveWorkSessionChanges({
        sessionId,
        sourceMode: 'directory',
        folderName: directoryHandle?.name || sourceFolderNameRef.current,
        currentIndex: currentIndexRef.current,
        total: currentImages.length,
        updatedAt: now,
        directoryHandle
      }, changedItems);

      changedItems.forEach((item) => {
        persistedSignatureRef.current.set(item.identity, getPersistedWorkItemSignature(item));
      });
      setLastAutoSavedAt(now);
      setAutoSaveError('');
    } catch (error) {
      console.error('Failed to auto-save work session:', error);
      setAutoSaveError('자동 저장 실패');
    }
  }, []);

  const queuePersistCurrentSession = useCallback(() => {
    persistenceQueueRef.current = persistenceQueueRef.current
      .catch(() => undefined)
      .then(persistCurrentSession);
    return persistenceQueueRef.current;
  }, [persistCurrentSession]);

  const loadImagesFromDirectoryHandle = useCallback(async (
    directoryHandle: any,
    options: DirectoryLoadOptions = {}
  ) => {
    setUploadProgress({ current: 0, total: null, stage: 'initializing' });

    const scanWarnings: ScanWarning[] = [];
    const scannedEntries = sortSourceEntriesForStableLoad(await scanSourceDirectory(directoryHandle, '', (count) => {
      setUploadProgress({ current: count, total: null, stage: 'initializing' });
    }, { count: 0 }, scanWarnings));

    const imageEntries: SourceFileEntry[] = [];
    const loadedImages: ImageFile[] = [];

    setUploadProgress({ current: 0, total: scannedEntries.length, stage: 'loading' });
    for (let i = 0; i < scannedEntries.length; i += 1) {
      const entry = scannedEntries[i];
      try {
        const file = await entry.fileHandle.getFile();
        if (isImageLikeFile(file)) {
          imageEntries.push(entry);
          loadedImages.push(createImageEntry(file, entry.relativePath));
        }
      } catch (error) {
        console.warn('Skipped unreadable file while loading folder:', entry.relativePath, error);
      }

      if (i % 200 === 0 || i === scannedEntries.length - 1) {
        setUploadProgress({ current: i + 1, total: scannedEntries.length, stage: 'loading' });
        await waitForUi();
      }
    }

    if (loadedImages.length === 0) {
      replaceSourceDirectory(directoryHandle, imageEntries);
      replaceImages([]);
      await disableWorkSessionPersistence();
      alert(scanWarnings.length > 0
        ? `이미지 파일을 찾지 못했습니다. 일부 하위 폴더 접근 경고: ${scanWarnings.length.toLocaleString()}개`
        : '선택한 폴더에서 이미지 파일을 찾지 못했습니다.');
      return;
    }

    const restoredImages = options.restoreSession
      ? applyPersistedItems(loadedImages, options.restoreSession.items)
      : loadedImages;
    const restoredIndex = options.restoreSession?.meta.currentIndex || 0;
    const safeIndex = Math.min(Math.max(0, restoredIndex), restoredImages.length - 1);

    replaceSourceDirectory(directoryHandle, imageEntries);
    replaceImages(restoredImages, safeIndex);
    await activateWorkSession(
      directoryHandle,
      restoredImages,
      safeIndex,
      options.restoreSession?.meta.sessionId
    );

    if (scanWarnings.length > 0) {
      alert(`이미지 로드는 완료됐지만 일부 하위 폴더는 접근하지 못했습니다.\n스캔 경고: ${scanWarnings.length.toLocaleString()}개`);
    }
  }, [activateWorkSession, applyPersistedItems, disableWorkSessionPersistence, replaceImages, replaceSourceDirectory]);

  const restoreSavedWorkSession = useCallback(async (savedSession: PersistedWorkSession, requestPermission: boolean) => {
    const directoryHandle = savedSession.meta.directoryHandle;
    if (!directoryHandle) {
      await disableWorkSessionPersistence();
      setRestoreCandidate(null);
      setWorkSessionInitialized(true);
      return;
    }

    try {
      setIsRestoringSession(true);
      let permission = typeof directoryHandle.queryPermission === 'function'
        ? await directoryHandle.queryPermission({ mode: 'readwrite' })
        : 'prompt';

      if (permission !== 'granted' && requestPermission && typeof directoryHandle.requestPermission === 'function') {
        permission = await directoryHandle.requestPermission({ mode: 'readwrite' });
      }

      if (permission !== 'granted') {
        setRestoreCandidate(savedSession);
        return;
      }

      await loadImagesFromDirectoryHandle(directoryHandle, { restoreSession: savedSession });
      setRestoreCandidate(null);
    } catch (error: any) {
      console.error('Failed to restore work session:', error);
      if (error?.name !== 'AbortError') {
        alert(`이전 작업 복원 중 오류가 발생했습니다.\n\n${error?.name || 'Error'}: ${error?.message || String(error)}`);
      }
      setRestoreCandidate(savedSession);
    } finally {
      setUploadProgress(null);
      setIsRestoringSession(false);
      setWorkSessionInitialized(true);
    }
  }, [disableWorkSessionPersistence, loadImagesFromDirectoryHandle]);

  useEffect(() => {
    const handleResetAllLabels = () => {
      setImages((previous) => previous.map((image) => ({
        ...image,
        status: UNCLASSIFIED_STATUS,
        labels: []
      })));
    };

    window.addEventListener('visionqc:reset-labels', handleResetAllLabels);
    return () => window.removeEventListener('visionqc:reset-labels', handleResetAllLabels);
  }, []);

  useEffect(() => {
    let cancelled = false;

    const initializeWorkSession = async () => {
      try {
        const savedSession = await loadActiveWorkSession();
        if (cancelled) return;
        if (!savedSession) {
          setWorkSessionInitialized(true);
          return;
        }

        const directoryHandle = savedSession.meta.directoryHandle;
        const permission = directoryHandle && typeof directoryHandle.queryPermission === 'function'
          ? await directoryHandle.queryPermission({ mode: 'readwrite' })
          : 'prompt';

        if (cancelled) return;
        if (permission === 'granted') {
          await restoreSavedWorkSession(savedSession, false);
        } else {
          setRestoreCandidate(savedSession);
          setWorkSessionInitialized(true);
        }
      } catch (error) {
        console.error('Failed to inspect saved work session:', error);
        setWorkSessionInitialized(true);
      }
    };

    initializeWorkSession();
    return () => {
      cancelled = true;
    };
  }, [restoreSavedWorkSession]);

  useEffect(() => {
    if (!workSessionInitialized || !activeSessionIdRef.current || isReplacingSessionRef.current) return;
    if (autoSaveTimerRef.current !== null) window.clearTimeout(autoSaveTimerRef.current);

    autoSaveTimerRef.current = window.setTimeout(() => {
      autoSaveTimerRef.current = null;
      queuePersistCurrentSession();
    }, 300);

    return () => {
      if (autoSaveTimerRef.current !== null) {
        window.clearTimeout(autoSaveTimerRef.current);
        autoSaveTimerRef.current = null;
      }
    };
  }, [currentIndex, images, queuePersistCurrentSession, sourceFolderName, workSessionInitialized]);

  useEffect(() => {
    const flushSession = () => {
      queuePersistCurrentSession();
    };
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'hidden') flushSession();
    };

    window.addEventListener('pagehide', flushSession);
    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => {
      window.removeEventListener('pagehide', flushSession);
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
  }, [queuePersistCurrentSession]);

  const handleLoadFolderClick = async () => {
    const showDirectoryPicker = (window as any).showDirectoryPicker;

    if (typeof showDirectoryPicker === 'function') {
      try {
        // Important: call the picker directly from this button click.
        // This keeps Chrome's user-gesture requirement satisfied.
        const directoryHandle = await showDirectoryPicker({ mode: 'readwrite' });
        if (typeof directoryHandle.requestPermission === 'function') {
          const permission = await directoryHandle.requestPermission({ mode: 'readwrite' });
          if (permission !== 'granted') {
            alert('폴더 읽기/쓰기 권한이 허용되지 않았습니다. 폴더 권한을 허용한 뒤 다시 Load Folder를 눌러 주세요.');
            return;
          }
        }

        await loadImagesFromDirectoryHandle(directoryHandle);
      } catch (error: any) {
        if (error?.name !== 'AbortError') {
          console.error(error);
          alert(`폴더 로드 중 오류가 발생했습니다.\n\n원인 상세: ${error?.name || 'Error'}: ${error?.message || String(error)}`);
        }
      } finally {
        setUploadProgress(null);
      }
      return;
    }

    // Fallback for older browsers. This can load images but cannot keep write permission for Organize.
    setUploadProgress({ current: 0, total: null, stage: 'preparing' });
    requestAnimationFrame(() => {
      setTimeout(() => folderInputRef.current?.click(), 50);
    });
  };

  const startUploadProcess = (files: FileList | null, inputElement: HTMLInputElement) => {
    if (!files || files.length === 0) {
      setUploadProgress(null);
      return;
    }

    setUploadProgress({ current: 0, total: files.length, stage: 'initializing' });

    requestAnimationFrame(() => {
      setTimeout(async () => {
        const existingFileKeys = new Set(images.map((img) => `${img.file.name}-${img.file.size}`));
        const validFiles: File[] = [];

        for (let i = 0; i < files.length; i += 1) {
          const file = files[i];
          if (file.type.startsWith('image/') && !existingFileKeys.has(`${file.name}-${file.size}`)) {
            validFiles.push(file);
          }

          if (i % 800 === 0 && i > 0) {
            setUploadProgress({ current: i, total: files.length, stage: 'initializing' });
            await new Promise((resolve) => setTimeout(resolve, 0));
          }
        }

        if (validFiles.length === 0) {
          setUploadProgress(null);
          inputElement.value = '';
          return;
        }

        const sortedValidFiles = sortFilesForStableLoad(validFiles);

        await disableWorkSessionPersistence();
        setWorkSessionInitialized(true);
        replaceSourceDirectory(null, []);

        setUploadProgress({ current: 0, total: sortedValidFiles.length, stage: 'loading' });
        const batchSize = 35;
        for (let i = 0; i < sortedValidFiles.length; i += batchSize) {
          const batch = sortedValidFiles.slice(i, i + batchSize);
          appendImages(buildImageEntries(batch));
          setUploadProgress((prev) => prev ? { ...prev, current: i + batch.length, total: sortedValidFiles.length, stage: 'loading' } : null);
          await new Promise((resolve) => setTimeout(resolve, 0));
        }

        setUploadProgress(null);
        inputElement.value = '';
      }, 10);
    });
  };

  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    startUploadProcess(event.target.files, event.target);
  };

  const handleClassify = useCallback((status: string) => {
    if (images.length === 0) return;
    setImages((prev) => {
      const updated = [...prev];
      updated[currentIndex] = { ...updated[currentIndex], status, labels: [status] };
      return updated;
    });
    if (currentIndex < images.length - 1) {
      setCurrentIndex((prev) => prev + 1);
    }
  }, [currentIndex, images.length]);

  const handleToggleMultiLabel = useCallback((status: string) => {
    if (images.length === 0) return;
    setImages((prev) => {
      const updated = [...prev];
      const current = updated[currentIndex];
      const currentLabels = getImageLabelIds(current);
      const nextLabels = currentLabels.includes(status)
        ? currentLabels.filter((labelId) => labelId !== status)
        : [...currentLabels, status];

      updated[currentIndex] = {
        ...current,
        status: getRepresentativeStatus(nextLabels),
        labels: nextLabels
      };
      return updated;
    });
  }, [currentIndex, images.length]);

  const handleClearCurrentLabels = useCallback(() => {
    if (images.length === 0) return;
    setImages((prev) => {
      const updated = [...prev];
      updated[currentIndex] = { ...updated[currentIndex], status: UNCLASSIFIED_STATUS, labels: [] };
      return updated;
    });
  }, [currentIndex, images.length]);

  const goNextImage = useCallback(() => {
    if (currentIndex < images.length - 1) setCurrentIndex((prev) => prev + 1);
  }, [currentIndex, images.length]);

  const handleAISuggestion = async () => {
    if (images.length === 0 || isAnalyzing || uploadProgress) return;
    setIsAnalyzing(true);
    const reader = new FileReader();
    reader.onloadend = async () => {
      const base64 = (reader.result as string).split(',')[1];
      const suggestion = await analyzeImage(base64, images[currentIndex].file.name);
      if (suggestion) {
        setImages((prev) => {
          const updated = [...prev];
          updated[currentIndex] = { ...updated[currentIndex], aiSuggestion: suggestion };
          return updated;
        });
      }
      setIsAnalyzing(false);
    };
    reader.readAsDataURL(images[currentIndex].file);
  };

  const buildExportRows = useCallback((sourceImages: ImageFile[]) => {
    return sourceImages.map((img, index) => {
      const labelIds = getImageLabelIds(img);
      const representativeStatus = getRepresentativeStatus(labelIds);
      const statusLabel = representativeStatus === UNCLASSIFIED_STATUS
        ? 'PENDING'
        : getClassLabel(representativeStatus, classConfigs);
      const labelNames = labelIds.map((labelId) => getClassLabel(labelId, classConfigs));

      return {
        index: index + 1,
        fileName: img.file.name,
        // 기존 File_Ogarnizer 호환: 대표 라벨 1개를 status 계열에 유지
        status: statusLabel,
        statusId: statusLabel,
        statusLabel,
        // 다중 라벨 지원: 새 VisionQC는 labels/labelIds를 우선 사용
        labels: labelNames,
        labelIds,
        labelsText: labelNames.join(' + '),
        // VisionQC 내부 복원용 ID는 별도 보존
        classId: representativeStatus,
        internalStatusId: representativeStatus,
        classIds: labelIds,
        isClassified: labelIds.length > 0,
        isMultiLabel: labelIds.length > 1,
        aiConfidence: img.aiSuggestion?.confidence || 0,
        path: img.path || img.file.name
      };
    });
  }, [classConfigs]);

  const handleSaveToZip = async () => {
    if (images.length === 0 || isSaving) return;
    const classified = images.filter((img) => getImageLabelIds(img).length > 0);
    if (classified.length === 0) {
      alert('No classified images found.');
      return;
    }

    setIsSaving(true);
    setExportProgress({ current: 0, total: 100, stage: 'preparing', message: 'ZIP 파일 생성 준비 중...' });
    try {
      const zip = new JSZip();
      const usedNamesByFolder = new Map<string, Set<string>>();
      classified.forEach((img) => {
        getImageLabelIds(img).forEach((labelId) => {
          const folderName = sanitizeExportFolderName(getClassLabel(labelId, classConfigs)) || 'UNKNOWN';
          const usedNames = usedNamesByFolder.get(folderName) || new Set<string>();
          usedNamesByFolder.set(folderName, usedNames);
          zip.folder(folderName)?.file(makeUniqueFileName(img.file.name, usedNames), img.file);
        });
      });

      const content = await zip.generateAsync({ type: 'blob' }, (metadata) => {
        setExportProgress({
          current: Math.round(metadata.percent),
          total: 100,
          stage: 'copying',
          message: `ZIP 압축 중... ${Math.round(metadata.percent)}%`
        });
      });
      const url = URL.createObjectURL(content);
      const a = document.createElement('a');
      a.href = url;
      a.download = `VisionQC_Result_${new Date().toISOString().slice(0, 10)}.zip`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      console.error(error);
      alert('An error occurred during file generation.');
    } finally {
      setIsSaving(false);
      setExportProgress(null);
    }
  };

  const handleExportToFolder = async () => {
    if (images.length === 0 || isSaving) return;

    const showDirectoryPicker = (window as any).showDirectoryPicker;
    if (typeof showDirectoryPicker !== 'function') {
      alert('이 브라우저는 폴더 직접 저장을 지원하지 않습니다. Chrome 또는 Edge에서 localhost/HTTPS로 실행해 주세요.');
      return;
    }

    const classified = images.filter((img) => getImageLabelIds(img).length > 0);
    if (classified.length === 0) {
      alert('분류 완료된 이미지가 없습니다. PENDING 이미지는 폴더로 복사하지 않습니다.');
      return;
    }

    setIsSaving(true);
    try {
      const outputDirectory = await showDirectoryPicker({ mode: 'readwrite' });
      const folderCache = new Map<string, any>();
      const usedNamesByFolder = new Map<string, Set<string>>();
      let copiedCount = 0;
      let errorCount = 0;

      const totalCopyTargets = classified.reduce((sum, img) => sum + getImageLabelIds(img).length, 0);
      let processedTargets = 0;
      setExportProgress({ current: 0, total: totalCopyTargets, stage: 'copying', message: '분류 폴더로 이미지 복사 중...' });

      for (let i = 0; i < classified.length; i += 1) {
        const img = classified[i];
        const labelIds = getImageLabelIds(img);

        for (const labelId of labelIds) {
          const statusLabel = getClassLabel(labelId, classConfigs);
          const folderName = sanitizeExportFolderName(statusLabel) || 'UNKNOWN';

          try {
            let classDirectory = folderCache.get(folderName);
            if (!classDirectory) {
              classDirectory = await outputDirectory.getDirectoryHandle(folderName, { create: true });
              folderCache.set(folderName, classDirectory);
              usedNamesByFolder.set(folderName, new Set<string>());
            }

            const usedNames = usedNamesByFolder.get(folderName) || new Set<string>();
            usedNamesByFolder.set(folderName, usedNames);
            const exportFileName = makeUniqueFileName(img.file.name, usedNames);
            const fileHandle = await classDirectory.getFileHandle(exportFileName, { create: true });
            const writable = await fileHandle.createWritable();
            await writable.write(img.file);
            await writable.close();
            copiedCount += 1;
          } catch (error) {
            console.error(error);
            errorCount += 1;
          }

          processedTargets += 1;
        }

        if (i % 20 === 0 || i === classified.length - 1) {
          setExportProgress({
            current: processedTargets,
            total: totalCopyTargets,
            stage: 'copying',
            message: `분류 폴더 저장 중... ${processedTargets} / ${totalCopyTargets}`
          });
          await waitForUi();
        }
      }

      alert(`Export Folder 완료
분류 이미지: ${classified.length.toLocaleString()}개
복사 대상: ${totalCopyTargets.toLocaleString()}개
복사 성공: ${copiedCount.toLocaleString()}개
오류: ${errorCount.toLocaleString()}개
미분류(PENDING): ${(images.filter((img) => getImageLabelIds(img).length === 0).length).toLocaleString()}개`);
    } catch (error: any) {
      if (error?.name !== 'AbortError') {
        console.error(error);
        alert('폴더 직접 저장 중 오류가 발생했습니다. 권한 허용 여부와 저장 위치를 확인해 주세요.');
      }
    } finally {
      setIsSaving(false);
      setExportProgress(null);
    }
  };


  const handleOrganizeExistingFolder = () => {
    if (images.length === 0 || isSaving) return;

    const classified = images.filter((img) => getImageLabelIds(img).length > 0);
    if (classified.length === 0) {
      alert('분류 완료된 이미지가 없습니다. PENDING 이미지는 정리하지 않습니다.');
      return;
    }

    setIsOrganizeModeOpen(true);
  };

  const executeOrganizeExistingFolder = async (mode: OrganizeMode) => {
    if (images.length === 0 || isSaving) return;

    const showDirectoryPicker = (window as any).showDirectoryPicker;
    if (typeof showDirectoryPicker !== 'function' && !sourceDirectoryHandleRef.current) {
      alert('이 브라우저는 기존 폴더 직접 정리를 지원하지 않습니다. Chrome 또는 Edge에서 localhost/HTTPS로 실행해 주세요.');
      return;
    }

    const classified = images.filter((img) => getImageLabelIds(img).length > 0);
    if (classified.length === 0) {
      alert('분류 완료된 이미지가 없습니다. PENDING 이미지는 정리하지 않습니다.');
      return;
    }

    try {
      let sourceDirectory = sourceDirectoryHandleRef.current;
      let sourceEntries = sourceEntriesRef.current;

      if (!sourceDirectory) {
        // Called directly from the modal button click when no folder handle exists.
        // Keep this picker before any awaited work so Chrome sees a user gesture.
        sourceDirectory = await showDirectoryPicker({ mode: 'readwrite' });
        sourceDirectoryHandleRef.current = sourceDirectory;
        setSourceFolderName(sourceDirectory?.name || '');
      }

      setIsOrganizeModeOpen(false);
      setIsSaving(true);

      if (typeof sourceDirectory.requestPermission === 'function') {
        const permission = await sourceDirectory.requestPermission({ mode: 'readwrite' });
        if (permission !== 'granted') {
          alert('폴더 읽기/쓰기 권한이 허용되지 않았습니다. Load Folder 또는 Organize Folder를 다시 눌러 권한을 허용해 주세요.');
          return;
        }
      }

      const scanWarnings: ScanWarning[] = [];
      if (!sourceEntries || sourceEntries.length === 0) {
        setExportProgress({ current: 0, total: classified.length, stage: 'preparing', message: '원본 폴더 파일 인덱스 생성 중...' });
        sourceEntries = await scanSourceDirectory(sourceDirectory, '', (count) => {
          setExportProgress({ current: 0, total: classified.length, stage: 'preparing', message: `원본 폴더 스캔 중... ${count.toLocaleString()}개 발견` });
        }, { count: 0 }, scanWarnings);
        sourceEntriesRef.current = sourceEntries;
      }

      if (sourceEntries.length === 0) {
        alert('선택한 폴더에서 이미지/파일을 찾지 못했습니다. Load Folder에서 선택했던 원본 루트 폴더를 다시 선택해 주세요.');
        return;
      }

      const sourceIndex = buildSourceFileIndex(sourceEntries);
      const folderCache = new Map<string, any>();
      const usedNamesByFolder = new Map<string, Set<string>>();
      let copiedCount = 0;
      let movedCount = 0;
      let missingCount = 0;
      let skippedCount = 0;
      let errorCount = 0;

      const totalCopyTargets = classified.reduce((sum, img) => sum + getImageLabelIds(img).length, 0);
      let processedTargets = 0;
      setExportProgress({ current: 0, total: totalCopyTargets, stage: 'copying', message: mode === 'move' ? '기존 파일 이동 정리 중...' : '기존 파일 복사 정리 중...' });

      for (let i = 0; i < classified.length; i += 1) {
        const img = classified[i];
        const labelIds = getImageLabelIds(img);
        const sourceEntry = resolveSourceEntry(img.path, img.file.name, sourceIndex);

        if (!sourceEntry) {
          missingCount += 1;
          processedTargets += labelIds.length;
        } else {
          let copiedForImage = 0;
          let failedForImage = false;
          let sourceAlreadyInTargetFolder = false;

          for (const labelId of labelIds) {
            const statusLabel = getClassLabel(labelId, classConfigs);
            const folderName = sanitizeExportFolderName(statusLabel) || 'UNKNOWN';

            try {
              let classDirectory = folderCache.get(folderName);
              if (!classDirectory) {
                classDirectory = await sourceDirectory.getDirectoryHandle(folderName, { create: true });
                folderCache.set(folderName, classDirectory);
                usedNamesByFolder.set(folderName, new Set<string>());
              }

              const usedNames = usedNamesByFolder.get(folderName) || new Set<string>();
              usedNamesByFolder.set(folderName, usedNames);
              const exportFileName = makeUniqueFileName(sourceEntry.fileName, usedNames);

              if (isSameDestination(sourceEntry, folderName, exportFileName)) {
                skippedCount += 1;
                sourceAlreadyInTargetFolder = true;
                copiedForImage += 1;
              } else {
                const sourceFile = await sourceEntry.fileHandle.getFile();
                const fileHandle = await classDirectory.getFileHandle(exportFileName, { create: true });
                const writable = await fileHandle.createWritable();
                await writable.write(sourceFile);
                await writable.close();
                copiedCount += 1;
                copiedForImage += 1;
              }
            } catch (error) {
              console.error(error);
              errorCount += 1;
              failedForImage = true;
            }

            processedTargets += 1;
          }

          if (mode === 'move' && copiedForImage === labelIds.length && !failedForImage && !sourceAlreadyInTargetFolder) {
            try {
              await sourceEntry.parentDirectoryHandle.removeEntry(sourceEntry.fileName);
              movedCount += 1;
            } catch (error) {
              console.error(error);
              errorCount += 1;
            }
          }
        }

        if (i % 20 === 0 || i === classified.length - 1) {
          setExportProgress({
            current: processedTargets,
            total: totalCopyTargets,
            stage: 'copying',
            message: `${mode === 'move' ? '이동' : '복사'} 정리 중... ${processedTargets} / ${totalCopyTargets}`
          });
          await waitForUi();
        }
      }

      alert(`Organize Folder 완료
원본 폴더: ${sourceFolderName || sourceDirectory?.name || '(selected root)'}
모드: ${mode === 'move' ? '이동' : '복사'}
처리 이미지: ${classified.length.toLocaleString()}개
복사 대상: ${totalCopyTargets.toLocaleString()}개
복사 성공: ${copiedCount.toLocaleString()}개
이동 성공: ${movedCount.toLocaleString()}개
이미 정리됨: ${skippedCount.toLocaleString()}개
원본 못 찾음: ${missingCount.toLocaleString()}개
스캔 경고: ${scanWarnings.length.toLocaleString()}개
오류: ${errorCount.toLocaleString()}개`);
    } catch (error: any) {
      if (error?.name !== 'AbortError') {
        console.error(error);
        const detail = `${error?.name || 'Error'}: ${error?.message || String(error)}`;
        alert(`기존 폴더 정리 중 오류가 발생했습니다.\n\n원인 상세: ${detail}\n\n선택한 원본 루트 폴더와 폴더 권한을 확인해 주세요.`);
      }
    } finally {
      setIsSaving(false);
      setExportProgress(null);
    }
  };


  const handleExportJson = () => {
    if (images.length === 0) return;
    const results = buildExportRows(images);
    const blob = new Blob([JSON.stringify(results, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `VisionQC_Labels_${new Date().getTime()}.json`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const resolveStatusFromJsonItem = useCallback((item: VisionQcJsonItem, configs: ClassConfig[]) => {
    const idByUpper = new Map(configs.map((cfg) => [cfg.id.toUpperCase(), cfg.id]));
    const labelByUpper = new Map(configs.map((cfg) => [cfg.label.trim().toUpperCase(), cfg.id]));
    const candidates = [item.classId, item.internalStatusId, item.statusId, item.status, item.statusLabel];

    for (const candidate of candidates) {
      const normalized = String(candidate || '').trim();
      if (!normalized) continue;
      if (isPendingStatusText(normalized)) return UNCLASSIFIED_STATUS;
      const upper = normalized.toUpperCase();
      if (idByUpper.has(upper)) return idByUpper.get(upper)!;
      if (labelByUpper.has(upper)) return labelByUpper.get(upper)!;
    }

    return null;
  }, []);

  const resolveLabelsFromJsonItem = useCallback((item: VisionQcJsonItem, configs: ClassConfig[]) => {
    const idByUpper = new Map(configs.map((cfg) => [cfg.id.toUpperCase(), cfg.id]));
    const labelByUpper = new Map(configs.map((cfg) => [cfg.label.trim().toUpperCase(), cfg.id]));

    const resolveOne = (value: string) => {
      const normalized = String(value || '').trim();
      if (!normalized || isPendingStatusText(normalized)) return null;
      const upper = normalized.toUpperCase();
      if (idByUpper.has(upper)) return idByUpper.get(upper)!;
      if (labelByUpper.has(upper)) return labelByUpper.get(upper)!;
      return null;
    };

    const rawMultiValues = [
      ...coerceStringArray(item.labelIds),
      ...coerceStringArray(item.labels),
      ...coerceStringArray(item.classIds)
    ];

    const multiLabels = uniqueLabelIds(rawMultiValues.map((value) => resolveOne(value)).filter(Boolean) as string[]);
    if (multiLabels.length > 0) return multiLabels;

    const singleStatus = resolveStatusFromJsonItem(item, configs);
    if (!singleStatus || singleStatus === UNCLASSIFIED_STATUS) return [];
    return [singleStatus];
  }, [resolveStatusFromJsonItem]);

  const handleImportJsonUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    if (images.length === 0) {
      alert('먼저 이미지 폴더/파일을 Load 한 다음 Labels를 불러와 주세요.');
      return;
    }

    try {
      const text = await file.text();
      const parsed = JSON.parse(text);
      const items: VisionQcJsonItem[] = Array.isArray(parsed)
        ? parsed
        : Array.isArray(parsed?.items)
          ? parsed.items
          : Array.isArray(parsed?.results)
            ? parsed.results
            : Array.isArray(parsed?.images)
              ? parsed.images
              : [];

      if (items.length === 0) {
        alert('Labels JSON 안에서 이미지 결과 목록을 찾지 못했습니다.');
        return;
      }

      const labelUpdates = new Map<string, string>();
      const knownClassIds = new Set(classConfigs.map((cfg) => cfg.id));
      items.forEach((item) => {
        if (!item.statusLabel || isPendingStatusText(item.statusLabel)) return;
        const idCandidates = [item.classId, item.internalStatusId, item.statusId];
        const matchedId = idCandidates.map((value) => String(value || '').trim()).find((value) => knownClassIds.has(value));
        if (!matchedId) return;
        labelUpdates.set(matchedId, String(item.statusLabel).trim());
      });

      if (labelUpdates.size > 0) {
        setClassConfigs((prev) => prev.map((cfg) => {
          const nextLabel = labelUpdates.get(cfg.id);
          return nextLabel ? { ...cfg, label: nextLabel } : cfg;
        }));
      }

      const byPath = new Map<string, VisionQcJsonItem>();
      const byName = new Map<string, VisionQcJsonItem[]>();

      items.forEach((item) => {
        if (item.path) byPath.set(normalizeLookupKey(item.path), item);
        if (item.fileName) {
          const key = normalizeLookupKey(item.fileName);
          const list = byName.get(key) || [];
          list.push(item);
          byName.set(key, list);
        }
      });

      let matchedCount = 0;
      let appliedCount = 0;
      let unresolvedCount = 0;

      const nextImages = images.map((img) => {
        const pathKey = normalizeLookupKey(img.path || img.file.name);
        const nameKey = normalizeLookupKey(img.file.name);
        const matched = byPath.get(pathKey) || (byName.get(nameKey)?.length === 1 ? byName.get(nameKey)?.[0] : undefined);
        if (!matched) return img;

        matchedCount += 1;
        const nextLabels = resolveLabelsFromJsonItem(matched, classConfigs);
        if (nextLabels.length === 0 && !isPendingStatusText(matched.statusLabel || matched.status || matched.statusId || matched.classId || matched.internalStatusId)) {
          unresolvedCount += 1;
          return img;
        }

        appliedCount += 1;
        return { ...img, status: getRepresentativeStatus(nextLabels), labels: nextLabels };
      });

      setImages(nextImages);

      alert(`Load Labels 적용 완료\n매칭 파일: ${matchedCount.toLocaleString()}개\n라벨 적용: ${appliedCount.toLocaleString()}개\n미해석 상태값: ${unresolvedCount.toLocaleString()}개`);
    } catch (error) {
      console.error(error);
      alert('Labels JSON을 읽는 중 오류가 발생했습니다. 파일 형식을 확인해 주세요.');
    }
  };

  const handleCellIdImport = useCallback(async (files: File[], cellIdText: string): Promise<CellIdImportSummary> => {
    const normalizedIds = Array.from(
      new Set(
        cellIdText
          .split(/\r?\n/)
          .map((line) => line.trim().toUpperCase())
          .filter(Boolean)
      )
    );

    const imageFiles = files.filter((file) => file.type.startsWith('image/'));
    const matchedFilesMap = new Map<string, File>();
    const unmatchedIds: string[] = [];

    normalizedIds.forEach((cellId) => {
      const matches = imageFiles.filter((file) => file.name.toUpperCase().includes(cellId));
      if (matches.length === 0) {
        unmatchedIds.push(cellId);
        return;
      }
      matches.forEach((file) => {
        const uniqueKey = `${file.name}-${file.size}-${file.lastModified}`;
        if (!matchedFilesMap.has(uniqueKey)) matchedFilesMap.set(uniqueKey, file);
      });
    });

    const matchedFiles = Array.from(matchedFilesMap.values());
    await disableWorkSessionPersistence();
    setWorkSessionInitialized(true);
    replaceSourceDirectory(null, []);
    replaceImages(buildImageEntries(matchedFiles));

    return {
      inputCount: normalizedIds.length,
      matchedCount: matchedFiles.length,
      unmatchedIds
    };
  }, [buildImageEntries, disableWorkSessionPersistence, replaceImages, replaceSourceDirectory]);

  const handleSaveClassSettings = (nextConfigs: ClassConfig[], nextShortcutSettings: ShortcutSettings) => {
    setClassConfigs(nextConfigs);
    setShortcutSettings(nextShortcutSettings);
    heldKeysRef.current.clear();
    setIsMultiModifierHeld(false);
  };

  useEffect(() => {
    const multiModifierKey = normalizeKeyboardKey(shortcutSettings.multiLabelModifierKey || 'M');

    const refreshModifierHeld = () => {
      const held = !!multiModifierKey && heldKeysRef.current.has(multiModifierKey);
      setIsMultiModifierHeld(held);
      return held;
    };

    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const isTyping = !!target && (
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        target.tagName === 'SELECT' ||
        target.isContentEditable
      );

      if (isTyping || uploadProgress || exportProgress || isCellIdModalOpen || isClassSettingsOpen) return;

      const pressedKey = normalizeKeyboardKey(e.key);
      if (pressedKey) {
        heldKeysRef.current.add(pressedKey);
      }

      const multiModifierHeld = refreshModifierHeld();

      if (multiModifierKey && pressedKey === multiModifierKey) {
        e.preventDefault();
        return;
      }

      if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') {
        e.preventDefault();
        if (currentIndex > 0) setCurrentIndex((prev) => prev - 1);
        return;
      }

      if (e.key === 'ArrowDown' || e.key === 'ArrowRight') {
        e.preventDefault();
        if (currentIndex < images.length - 1) setCurrentIndex((prev) => prev + 1);
        return;
      }

      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        goNextImage();
        return;
      }

      if (e.key === 'Backspace') {
        e.preventDefault();
        handleClearCurrentLabels();
        return;
      }

      const matched = classConfigs.find((item) => item.enabled && normalizeKeyboardKey(item.hotkey) === pressedKey);
      if (matched) {
        e.preventDefault();
        if (multiModifierHeld) {
          handleToggleMultiLabel(matched.id);
        } else {
          handleClassify(matched.id);
        }
      }
    };

    const handleKeyUp = (e: KeyboardEvent) => {
      const releasedKey = normalizeKeyboardKey(e.key);
      if (releasedKey) {
        heldKeysRef.current.delete(releasedKey);
        refreshModifierHeld();
      }
    };

    const handleBlur = () => {
      heldKeysRef.current.clear();
      setIsMultiModifierHeld(false);
    };

    window.addEventListener('keydown', handleKeyDown);
    window.addEventListener('keyup', handleKeyUp);
    window.addEventListener('blur', handleBlur);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('keyup', handleKeyUp);
      window.removeEventListener('blur', handleBlur);
    };
  }, [classConfigs, currentIndex, exportProgress, goNextImage, handleClassify, handleClearCurrentLabels, handleToggleMultiLabel, images.length, isCellIdModalOpen, isClassSettingsOpen, shortcutSettings.multiLabelModifierKey, uploadProgress]);

  const handleResumeSavedSession = useCallback(async () => {
    if (!restoreCandidate || isRestoringSession) return;
    await restoreSavedWorkSession(restoreCandidate, true);
  }, [isRestoringSession, restoreCandidate, restoreSavedWorkSession]);

  const handleDiscardSavedSession = useCallback(async () => {
    if (isRestoringSession) return;
    await disableWorkSessionPersistence();
    setRestoreCandidate(null);
    setWorkSessionInitialized(true);
  }, [disableWorkSessionPersistence, isRestoringSession]);

  const okTone = okClassConfig ? getToneClasses(okClassConfig.tone, okClassConfig.kind) : null;

  return (
    <div className="flex h-screen flex-col bg-slate-950 text-slate-100 overflow-hidden font-sans">
      {uploadProgress ? (
        <div className="fixed inset-0 z-[100] flex flex-col items-center justify-center bg-slate-950/90 backdrop-blur-xl animate-in fade-in duration-300">
          <div className="flex flex-col items-center gap-10 w-full max-w-lg px-12">
            <div className="relative">
              <div className="flex h-32 w-32 items-center justify-center rounded-[2.5rem] bg-blue-600/10 text-blue-500 border border-blue-500/30 shadow-[0_0_50px_rgba(37,99,235,0.2)]">
                {uploadProgress.stage === 'preparing' ? <Cpu className="h-14 w-14 animate-pulse" /> : <FileUp className="h-14 w-14 animate-bounce" />}
              </div>
              <div className="absolute -bottom-2 -right-2 flex h-10 w-10 items-center justify-center rounded-full bg-blue-600 text-white shadow-xl ring-4 ring-slate-950">
                <Loader2 className="h-5 w-5 animate-spin" />
              </div>
            </div>

            <div className="text-center space-y-3">
              <h2 className="text-3xl font-black text-white tracking-tighter uppercase">
                {uploadProgress.stage === 'preparing' ? 'System Readying...' : uploadProgress.stage === 'initializing' ? 'Indexing Metadata' : 'Importing Dataset'}
              </h2>
              <div className="flex flex-col items-center">
                <p className="text-xl font-mono font-bold text-blue-400">
                  {uploadProgress.current.toLocaleString()}<span className="text-slate-600 mx-2">/</span>{uploadProgress.total ? uploadProgress.total.toLocaleString() : '...'}
                </p>
                <p className="text-[10px] text-slate-500 font-black uppercase tracking-[0.4em] mt-3">High-Speed Asset Processing Engine</p>
              </div>
            </div>

            <div className="w-full space-y-4">
              <div className="h-4 w-full overflow-hidden rounded-full bg-slate-900 border border-slate-800 p-1">
                <div className={`h-full bg-gradient-to-r from-blue-600 via-indigo-500 to-blue-400 transition-all duration-300 ease-out rounded-full shadow-[0_0_15px_rgba(37,99,235,0.4)] ${!uploadProgress.total ? 'animate-shimmer' : ''}`} style={{ width: uploadProgress.total ? `${(uploadProgress.current / uploadProgress.total) * 100}%` : '40%', backgroundSize: '200% 100%' }} />
              </div>
              {uploadProgress.stage === 'preparing' ? (
                <div className="flex items-center gap-2 justify-center rounded-lg bg-amber-500/10 py-2 border border-amber-500/20">
                  <AlertTriangle className="h-3 w-3 text-amber-500" />
                  <span className="text-[10px] text-amber-200/80 font-bold uppercase tracking-tight">Waiting for OS File System Response...</span>
                </div>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}

      {exportProgress ? (
        <div className="fixed inset-0 z-[100] flex flex-col items-center justify-center bg-slate-950/90 backdrop-blur-xl animate-in fade-in duration-300">
          <div className="flex flex-col items-center gap-8 w-full max-w-lg px-12">
            <div className="relative">
              <div className="flex h-28 w-28 items-center justify-center rounded-[2rem] bg-emerald-600/10 text-emerald-400 border border-emerald-500/30 shadow-[0_0_50px_rgba(16,185,129,0.18)]">
                <Download className="h-12 w-12 animate-pulse" />
              </div>
              <div className="absolute -bottom-2 -right-2 flex h-10 w-10 items-center justify-center rounded-full bg-emerald-600 text-white shadow-xl ring-4 ring-slate-950">
                <Loader2 className="h-5 w-5 animate-spin" />
              </div>
            </div>

            <div className="text-center space-y-3">
              <h2 className="text-3xl font-black text-white tracking-tighter uppercase">Export Processing</h2>
              <p className="text-sm font-bold text-emerald-300">{exportProgress.message}</p>
              <p className="text-xl font-mono font-bold text-emerald-400">
                {exportProgress.current.toLocaleString()}<span className="text-slate-600 mx-2">/</span>{exportProgress.total.toLocaleString()}
              </p>
            </div>

            <div className="h-4 w-full overflow-hidden rounded-full bg-slate-900 border border-slate-800 p-1">
              <div className="h-full bg-gradient-to-r from-emerald-700 via-emerald-500 to-teal-400 transition-all duration-300 ease-out rounded-full shadow-[0_0_15px_rgba(16,185,129,0.35)]" style={{ width: `${Math.min(100, Math.max(0, (exportProgress.current / Math.max(1, exportProgress.total)) * 100))}%`, backgroundSize: '200% 100%' }} />
            </div>
          </div>
        </div>
      ) : null}

      {restoreCandidate ? (
        <div className="fixed inset-0 z-[110] flex items-center justify-center bg-slate-950/90 p-6 backdrop-blur-xl">
          <div className="w-full max-w-lg rounded-3xl border border-blue-500/30 bg-slate-900 p-7 shadow-2xl shadow-blue-950/40">
            <div className="mb-6 flex items-start gap-4">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border border-blue-500/30 bg-blue-600/15 text-blue-300">
                <FolderTree className="h-7 w-7" />
              </div>
              <div>
                <h3 className="text-xl font-black text-white">이전 작업이 발견되었습니다</h3>
                <p className="mt-2 text-sm leading-relaxed text-slate-400">폴더 접근 권한을 다시 허용하면 마지막 이미지 위치와 분류 결과를 그대로 복원합니다.</p>
              </div>
            </div>

            <div className="space-y-2 rounded-2xl border border-slate-800 bg-slate-950/70 p-4 text-sm">
              <div className="flex justify-between gap-4"><span className="text-slate-500">원본 폴더</span><span className="truncate font-bold text-slate-200">{restoreCandidate.meta.folderName || '(이름 없음)'}</span></div>
              <div className="flex justify-between gap-4"><span className="text-slate-500">전체 이미지</span><span className="font-mono font-bold text-blue-300">{restoreCandidate.meta.total.toLocaleString()}개</span></div>
              <div className="flex justify-between gap-4"><span className="text-slate-500">마지막 위치</span><span className="font-mono font-bold text-blue-300">{Math.min(restoreCandidate.meta.currentIndex + 1, restoreCandidate.meta.total).toLocaleString()} / {restoreCandidate.meta.total.toLocaleString()}</span></div>
              <div className="flex justify-between gap-4"><span className="text-slate-500">마지막 저장</span><span className="font-bold text-slate-300">{new Date(restoreCandidate.meta.updatedAt).toLocaleString('ko-KR')}</span></div>
            </div>

            <div className="mt-6 grid grid-cols-2 gap-3">
              <button
                type="button"
                onClick={handleResumeSavedSession}
                disabled={isRestoringSession}
                className="flex items-center justify-center gap-2 rounded-xl bg-blue-600 px-4 py-3 text-sm font-black text-white transition-colors hover:bg-blue-500 disabled:opacity-50"
              >
                {isRestoringSession ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />}
                이전 작업 복원
              </button>
              <button
                type="button"
                onClick={handleDiscardSavedSession}
                disabled={isRestoringSession}
                className="rounded-xl border border-slate-700 bg-slate-800 px-4 py-3 text-sm font-black text-slate-300 transition-colors hover:bg-slate-700 disabled:opacity-50"
              >
                새 작업 시작
              </button>
            </div>
          </div>
        </div>
      ) : null}

      <CellIdImportModal isOpen={isCellIdModalOpen} onClose={() => setIsCellIdModalOpen(false)} onImport={handleCellIdImport} />
      <ClassSettingsModal isOpen={isClassSettingsOpen} onClose={() => setIsClassSettingsOpen(false)} onSave={handleSaveClassSettings} classConfigs={classConfigs} shortcutSettings={shortcutSettings} />

      <header className="flex items-center justify-between border-b border-slate-800 bg-slate-900/50 px-6 py-4 backdrop-blur-md shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-blue-600 shadow-[0_0_20px_rgba(37,99,235,0.3)]">
            <LayoutGrid className="h-6 w-6 text-white" />
          </div>
          <div>
            <h1 className="text-xl font-bold tracking-tight text-white">VisionQC @TOPTEC</h1>
            <p className="text-[10px] text-slate-500 font-bold uppercase tracking-widest">Inspection Terminal v4.4.7</p>
            <p className={`mt-1 text-[9px] font-bold ${autoSaveError ? 'text-red-400' : activeSessionIdRef.current ? 'text-emerald-400' : 'text-slate-600'}`} title={autoSaveError || 'Load Folder로 선택한 작업은 새로고침 후 자동 복원됩니다.'}>
              {autoSaveError || (activeSessionIdRef.current
                ? `● 자동 저장됨${lastAutoSavedAt ? ` · ${new Date(lastAutoSavedAt).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}` : ''}`
                : '○ Load Folder 작업 자동 복원 대기')}
            </p>
          </div>
        </div>

        <StatsHeader stats={stats} classConfigs={classConfigs} />

        <div className="flex items-center gap-2">
          <button onClick={() => setIsCellIdModalOpen(true)} className="flex items-center gap-2 rounded-lg bg-violet-600/10 px-4 py-2 text-sm font-bold hover:bg-violet-600/20 border border-violet-500/30 transition-all text-violet-300">
            <Hash className="h-4 w-4" /> Input Cell ID
          </button>

          <button onClick={handleLoadFolderClick} className="flex items-center gap-2 rounded-lg bg-blue-600/10 px-4 py-2 text-sm font-bold hover:bg-blue-600/20 border border-blue-500/30 transition-all text-blue-400" title={sourceFolderName ? `현재 원본 폴더: ${sourceFolderName}` : '원본 폴더를 선택하고 정리 권한을 함께 확보합니다.'}>
            <FolderTree className="h-4 w-4" /> Load Folder{sourceFolderName ? <span className="max-w-32 truncate text-[10px] text-blue-200/70">· {sourceFolderName}</span> : null}
          </button>

          <input type="file" ref={folderInputRef} multiple className="hidden" onChange={(e) => startUploadProcess(e.target.files, e.target)} {...({ webkitdirectory: '', directory: '' } as any)} />

          <label className="flex cursor-pointer items-center gap-2 rounded-lg bg-slate-800 px-4 py-2 text-sm font-medium hover:bg-slate-700 border border-slate-700 transition-colors">
            <FolderOpen className="h-4 w-4" /> Load Files
            <input type="file" multiple accept="image/*" className="hidden" onChange={handleFileUpload} />
          </label>

          <div className="h-6 w-[1px] bg-slate-800 mx-1" />
          <button onClick={() => jsonInputRef.current?.click()} disabled={images.length === 0 || isSaving} className="flex items-center gap-2 rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm font-medium hover:bg-slate-700 disabled:opacity-50" title="저장된 라벨 JSON을 불러와 현재 이미지 목록에 분류값을 적용합니다.">
            <FileJson className="h-4 w-4" /> Load Labels
          </button>
          <input ref={jsonInputRef} type="file" accept="application/json,.json" className="hidden" onChange={handleImportJsonUpload} />

          <button onClick={handleExportJson} disabled={images.length === 0 || isSaving} className="flex items-center gap-2 rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm font-medium hover:bg-slate-700 disabled:opacity-50" title="현재 라벨 상태를 JSON으로 저장합니다. File_Organizer 호환 status 필드도 포함됩니다.">
            <FileJson className="h-4 w-4" /> Save Labels
          </button>
          <button onClick={handleSaveToZip} disabled={images.length === 0 || isSaving} className="flex items-center gap-2 rounded-lg border border-emerald-500/40 bg-emerald-600/15 px-3 py-2 text-sm font-medium text-emerald-300 hover:bg-emerald-600/25 disabled:opacity-50" title="소량 데이터용 ZIP Export입니다. 대량 데이터는 Export Folder를 권장합니다.">
            {isSaving && exportProgress ? <Loader2 className="h-4 w-4 animate-spin" /> : <FolderArchive className="h-4 w-4" />} ZIP
          </button>
          <button onClick={handleOrganizeExistingFolder} disabled={images.length === 0 || isSaving} className="flex items-center gap-2 rounded-lg border border-cyan-500/40 bg-cyan-600/15 px-3 py-2 text-sm font-bold text-cyan-300 hover:bg-cyan-600/25 disabled:opacity-50" title="현재 Classification 결과를 기준으로 기존 원본 폴더 안에 분류 폴더를 만들고, 파일을 복사하거나 이동합니다.">
            {isSaving && exportProgress ? <Loader2 className="h-4 w-4 animate-spin" /> : <FolderTree className="h-4 w-4" />} Organize Folder
          </button>
          <button onClick={handleExportToFolder} disabled={images.length === 0 || isSaving} className="flex items-center gap-2 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-bold hover:bg-emerald-500 disabled:opacity-50" title="브라우저에서 선택한 출력 폴더에 Classification별 폴더를 직접 생성하고 이미지를 복사합니다.">
            {isSaving && exportProgress ? <Loader2 className="h-4 w-4 animate-spin" /> : <FolderArchive className="h-4 w-4" />} Export Folder
          </button>
        </div>
      </header>

      <main className="flex flex-1 overflow-hidden">
        <div className="relative flex flex-1 flex-col overflow-hidden border-r border-slate-800">
          {images.length > 0 ? (
            <div className="flex h-full flex-col">
              <ImageViewer
                image={images[currentIndex]}
                onNext={() => currentIndex < images.length - 1 && setCurrentIndex((prev) => prev + 1)}
                onPrev={() => currentIndex > 0 && setCurrentIndex((prev) => prev - 1)}
                hasPrev={currentIndex > 0}
                hasNext={currentIndex < images.length - 1}
                currentIndex={currentIndex}
                total={images.length}
                classConfigs={classConfigs}
              />

              <div className="sticky bottom-0 z-10 flex w-full flex-col items-center justify-center gap-4 border-t border-slate-800 bg-slate-900/90 p-6 backdrop-blur-xl">
                <div className="flex w-full items-center justify-center gap-3">
                  {okClassConfig ? (
                    <button
                      onClick={() => isMultiModifierHeld ? handleToggleMultiLabel(okClassConfig.id) : handleClassify(okClassConfig.id)}
                      className={`flex h-20 w-48 flex-col items-center justify-center gap-1 rounded-2xl border-2 transition-all ${getImageLabelIds(images[currentIndex]).includes(okClassConfig.id) ? okTone?.button || 'border-emerald-500 bg-emerald-500/20 text-emerald-400' : okTone?.buttonIdle || 'border-slate-700 bg-slate-800/50 hover:border-emerald-500/50'}`}
                    >
                      <CheckCircle2 className="h-8 w-8" />
                      <span className="text-xs font-black uppercase tracking-widest">{okClassConfig.label} ({okClassConfig.hotkey})</span>
                    </button>
                  ) : null}

                  <div className="h-16 w-[1px] bg-slate-800 mx-4" />

                  <div className="flex max-w-[980px] flex-wrap items-center justify-center gap-2">
                    {ngClassConfigs.map((btn) => {
                      const tone = getToneClasses(btn.tone, btn.kind);
                      return (
                        <button
                          key={btn.id}
                          onClick={() => isMultiModifierHeld ? handleToggleMultiLabel(btn.id) : handleClassify(btn.id)}
                          className={`flex h-12 w-28 flex-col items-center justify-center rounded-xl border-2 text-[10px] font-bold uppercase transition-all ${getImageLabelIds(images[currentIndex]).includes(btn.id) ? tone.button : tone.buttonIdle}`}
                        >
                          {btn.label} ({btn.hotkey})
                        </button>
                      );
                    })}

                    <button onClick={handleAISuggestion} disabled={isAnalyzing} className="flex h-12 w-32 items-center justify-center gap-2 rounded-xl bg-indigo-600 text-[10px] font-black uppercase text-white hover:bg-indigo-500 disabled:opacity-50">
                      <Sparkles className={`h-4 w-4 ${isAnalyzing ? 'animate-spin' : ''}`} /> AI Suggest
                    </button>

                    <button onClick={() => setIsClassSettingsOpen(true)} className="flex h-12 w-36 items-center justify-center gap-2 rounded-xl border border-cyan-500/30 bg-cyan-500/10 text-[10px] font-black uppercase text-cyan-300 transition-colors hover:bg-cyan-500/20">
                      <Settings2 className="h-4 w-4" /> Class Settings
                    </button>

                    <button
                      onClick={handleOrganizeExistingFolder}
                      disabled={images.length === 0 || isSaving}
                      className="flex h-12 w-44 items-center justify-center gap-2 rounded-xl border border-amber-400/50 bg-amber-500/15 text-[10px] font-black uppercase text-amber-300 transition-colors hover:bg-amber-500/25 disabled:opacity-50"
                      title="기존 원본 폴더 안에 Classification별 폴더를 만들고 파일을 복사 또는 이동합니다."
                    >
                      {isSaving && exportProgress ? <Loader2 className="h-4 w-4 animate-spin" /> : <FolderTree className="h-4 w-4" />} Organize Folder
                    </button>
                  </div>
                </div>
                <div className={`text-center text-[10px] font-bold uppercase tracking-widest ${isMultiModifierHeld ? 'text-cyan-300' : 'text-slate-500'}`}>
                  Single: Hotkey/Click = label + next · Multi: Hold {shortcutSettings.multiLabelModifierKey} + Hotkey/Click = toggle label · ←/↑ previous · →/↓/Enter/Space next · Backspace = clear
                  {isMultiModifierHeld ? <span className="ml-3 rounded-full border border-cyan-400/50 bg-cyan-400/10 px-3 py-1 text-cyan-200">MULTI HELD</span> : null}
                </div>
              </div>
            </div>
          ) : (
            <div className="flex h-full flex-col items-center justify-center p-12 text-center bg-slate-900/20">
              <div className="relative mb-6">
                <div className="absolute inset-0 bg-blue-500/20 blur-3xl rounded-full" />
                <ShieldAlert className="h-20 w-20 text-slate-700 relative" />
              </div>
              <h2 className="text-2xl font-black uppercase mb-3 text-slate-300 tracking-tight">System Standby</h2>
              <p className="max-w-md text-slate-500 text-sm font-medium leading-relaxed">Please select a folder to inspect. During large folder loading, the window may appear unresponsive temporarily due to internal processing. This is normal.</p>
            </div>
          )}
        </div>

        <aside className="w-80 flex flex-col bg-slate-900/30 p-4 border-l border-slate-800">
          <FileExplorer files={images} currentIndex={currentIndex} onSelect={setCurrentIndex} classConfigs={classConfigs} />
        </aside>
      </main>

      {isOrganizeModeOpen ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6 backdrop-blur-sm">
          <div className="w-full max-w-md rounded-2xl border border-slate-700 bg-slate-900 p-6 shadow-2xl">
            <div className="mb-5">
              <h3 className="text-lg font-black text-slate-100">Organize Folder</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">
                현재 라벨 기준으로 Classification별 폴더를 만듭니다.
                {sourceFolderName ? <span className="mt-2 block text-cyan-300">원본 폴더: {sourceFolderName}</span> : <span className="mt-2 block text-amber-300">Load Folder 권한이 없어 다음 단계에서 원본 폴더를 한 번 선택합니다.</span>}
              </p>
            </div>

            <div className="grid grid-cols-3 gap-3">
              <button
                type="button"
                onClick={() => executeOrganizeExistingFolder('copy')}
                className="rounded-xl border border-blue-500/40 bg-blue-500/15 px-4 py-3 text-sm font-black text-blue-200 transition-colors hover:bg-blue-500/25"
              >
                복사
                <span className="mt-1 block text-[10px] font-medium text-blue-100/60">원본 유지</span>
              </button>
              <button
                type="button"
                onClick={() => executeOrganizeExistingFolder('move')}
                className="rounded-xl border border-red-500/40 bg-red-500/15 px-4 py-3 text-sm font-black text-red-200 transition-colors hover:bg-red-500/25"
              >
                이동
                <span className="mt-1 block text-[10px] font-medium text-red-100/60">원본 삭제</span>
              </button>
              <button
                type="button"
                onClick={() => setIsOrganizeModeOpen(false)}
                className="rounded-xl border border-slate-600 bg-slate-800 px-4 py-3 text-sm font-black text-slate-300 transition-colors hover:bg-slate-700"
              >
                취소
                <span className="mt-1 block text-[10px] font-medium text-slate-400">작업 안 함</span>
              </button>
            </div>
          </div>
        </div>
      ) : null}

      <style>{`
        @keyframes shimmer { 0% { background-position: -200% 0; } 100% { background-position: 200% 0; } }
        .animate-shimmer { animation: shimmer 2.2s infinite linear; }
        .no-scrollbar::-webkit-scrollbar { display: none; }
        .no-scrollbar { -ms-overflow-style: none; scrollbar-width: none; }
      `}</style>
    </div>
  );
};

export default App;
