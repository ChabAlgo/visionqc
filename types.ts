export type ClassificationStatus = string;

export interface ClassConfig {
  id: string;
  label: string;
  hotkey: string;
  kind: 'ok' | 'ng';
  enabled: boolean;
  tone: 'emerald' | 'red' | 'orange' | 'indigo' | 'pink' | 'amber' | 'purple' | 'slate';
}

export interface ShortcutSettings {
  /** Hold this key and press a class hotkey to toggle multi-label without moving to the next image. */
  multiLabelModifierKey: string;
}


export interface DefectRegion {
  ymin: number;
  xmin: number;
  ymax: number;
  xmax: number;
}

export interface ImageFile {
  id: string;
  file: File;
  path?: string;
  previewUrl: string;
  status: ClassificationStatus;
  /**
   * Multi-label support.
   * - Empty or undefined means pending/unclassified.
   * - status is kept as the representative/first label for older JSON/File_Organizer compatibility.
   */
  labels?: ClassificationStatus[];
  aiSuggestion?: {
    status: ClassificationStatus;
    reason: string;
    confidence: number;
    regions?: DefectRegion[];
  };
}

export interface ClassificationStats {
  total: number;
  ok: number;
  remaining: number;
  counts: Record<string, number>;
}
