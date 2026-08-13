import { ImageFile } from '../types';

const DB_NAME = 'visionqc-work-session-db-v1';
const DB_VERSION = 1;
const META_STORE = 'meta';
const ITEMS_STORE = 'items';
const ACTIVE_META_KEY = 'active-session';

export type PersistedWorkItem = {
  identity: string;
  path: string;
  fileName: string;
  size: number;
  lastModified: number;
  status: string;
  labels: string[];
  aiSuggestion?: ImageFile['aiSuggestion'];
};

export type PersistedWorkSessionMeta = {
  key: typeof ACTIVE_META_KEY;
  sessionId: string;
  sourceMode: 'directory';
  folderName: string;
  currentIndex: number;
  total: number;
  updatedAt: number;
  directoryHandle: any;
};

export type PersistedWorkSession = {
  meta: PersistedWorkSessionMeta;
  items: PersistedWorkItem[];
};

const normalizePath = (value?: string) => String(value || '').replace(/\\/g, '/').trim().toLowerCase();

export const createWorkItemIdentity = (
  path: string | undefined,
  fileName: string,
  size: number,
  lastModified: number
) => `${normalizePath(path || fileName)}|${size}|${lastModified}`;

export const imageToPersistedWorkItem = (image: ImageFile): PersistedWorkItem => ({
  identity: createWorkItemIdentity(image.path, image.file.name, image.file.size, image.file.lastModified),
  path: image.path || image.file.name,
  fileName: image.file.name,
  size: image.file.size,
  lastModified: image.file.lastModified,
  status: image.status,
  labels: Array.isArray(image.labels) ? [...image.labels] : [],
  aiSuggestion: image.aiSuggestion
});

export const getPersistedWorkItemSignature = (item: PersistedWorkItem) => JSON.stringify([
  item.status,
  item.labels,
  item.aiSuggestion || null
]);

const requestToPromise = <T>(request: IDBRequest<T>): Promise<T> => new Promise((resolve, reject) => {
  request.onsuccess = () => resolve(request.result);
  request.onerror = () => reject(request.error || new Error('IndexedDB request failed.'));
});

const transactionDone = (transaction: IDBTransaction): Promise<void> => new Promise((resolve, reject) => {
  transaction.oncomplete = () => resolve();
  transaction.onerror = () => reject(transaction.error || new Error('IndexedDB transaction failed.'));
  transaction.onabort = () => reject(transaction.error || new Error('IndexedDB transaction aborted.'));
});

const openDatabase = (): Promise<IDBDatabase> => new Promise((resolve, reject) => {
  const request = indexedDB.open(DB_NAME, DB_VERSION);

  request.onupgradeneeded = () => {
    const db = request.result;
    if (!db.objectStoreNames.contains(META_STORE)) {
      db.createObjectStore(META_STORE, { keyPath: 'key' });
    }
    if (!db.objectStoreNames.contains(ITEMS_STORE)) {
      db.createObjectStore(ITEMS_STORE, { keyPath: 'identity' });
    }
  };

  request.onsuccess = () => resolve(request.result);
  request.onerror = () => reject(request.error || new Error('Failed to open work session database.'));
});

export const loadActiveWorkSession = async (): Promise<PersistedWorkSession | null> => {
  const db = await openDatabase();
  try {
    const transaction = db.transaction([META_STORE, ITEMS_STORE], 'readonly');
    const meta = await requestToPromise(
      transaction.objectStore(META_STORE).get(ACTIVE_META_KEY)
    ) as PersistedWorkSessionMeta | undefined;

    if (!meta) {
      await transactionDone(transaction);
      return null;
    }

    const items = await requestToPromise(
      transaction.objectStore(ITEMS_STORE).getAll()
    ) as PersistedWorkItem[];

    await transactionDone(transaction);
    return { meta, items };
  } finally {
    db.close();
  }
};

export const replaceActiveWorkSession = async (
  meta: Omit<PersistedWorkSessionMeta, 'key'>,
  items: PersistedWorkItem[]
): Promise<void> => {
  const db = await openDatabase();
  try {
    const transaction = db.transaction([META_STORE, ITEMS_STORE], 'readwrite');
    const metaStore = transaction.objectStore(META_STORE);
    const itemStore = transaction.objectStore(ITEMS_STORE);

    itemStore.clear();
    metaStore.put({ ...meta, key: ACTIVE_META_KEY } satisfies PersistedWorkSessionMeta);
    items.forEach((item) => itemStore.put(item));

    await transactionDone(transaction);
  } finally {
    db.close();
  }
};

export const saveActiveWorkSessionChanges = async (
  meta: Omit<PersistedWorkSessionMeta, 'key'>,
  changedItems: PersistedWorkItem[]
): Promise<void> => {
  const db = await openDatabase();
  try {
    const transaction = db.transaction([META_STORE, ITEMS_STORE], 'readwrite');
    transaction.objectStore(META_STORE).put({ ...meta, key: ACTIVE_META_KEY } satisfies PersistedWorkSessionMeta);
    const itemStore = transaction.objectStore(ITEMS_STORE);
    changedItems.forEach((item) => itemStore.put(item));
    await transactionDone(transaction);
  } finally {
    db.close();
  }
};

export const clearActiveWorkSession = async (): Promise<void> => {
  const db = await openDatabase();
  try {
    const transaction = db.transaction([META_STORE, ITEMS_STORE], 'readwrite');
    transaction.objectStore(META_STORE).clear();
    transaction.objectStore(ITEMS_STORE).clear();
    await transactionDone(transaction);
  } finally {
    db.close();
  }
};
