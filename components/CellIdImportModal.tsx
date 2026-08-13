import React, { useMemo, useRef, useState } from 'react';
import { FolderOpen, Hash, Loader2, X } from 'lucide-react';

export interface CellIdImportSummary {
  inputCount: number;
  matchedCount: number;
  unmatchedIds: string[];
}

interface CellIdImportModalProps {
  isOpen: boolean;
  onClose: () => void;
  onImport: (files: File[], cellIdText: string) => Promise<CellIdImportSummary>;
}

const CellIdImportModal: React.FC<CellIdImportModalProps> = ({ isOpen, onClose, onImport }) => {
  const folderInputRef = useRef<HTMLInputElement>(null);
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [selectedFolderName, setSelectedFolderName] = useState('');
  const [cellIdText, setCellIdText] = useState('');
  const [isProcessing, setIsProcessing] = useState(false);
  const [summary, setSummary] = useState<CellIdImportSummary | null>(null);

  const parsedPreviewIds = useMemo(() => {
    return Array.from(
      new Set(
        cellIdText
          .split(/\r?\n/)
          .map((line) => line.trim())
          .filter(Boolean)
      )
    );
  }, [cellIdText]);

  if (!isOpen) return null;

  const handleFolderSelection = (event: React.ChangeEvent<HTMLInputElement>) => {
    const fileList = event.target.files;

    if (!fileList || fileList.length === 0) {
      setSelectedFiles([]);
      setSelectedFolderName('');
      setSummary(null);
      event.target.value = '';
      return;
    }

    const files: File[] = Array.from(fileList);
    setSelectedFiles(files);
    setSummary(null);

    const firstFile = files[0];
    const firstPath =
      ((firstFile as File & { webkitRelativePath?: string }).webkitRelativePath) || firstFile.name;

    const folderName = firstPath.includes('/') ? firstPath.split('/')[0] : firstFile.name;
    setSelectedFolderName(folderName);

    event.target.value = '';
  };

  const handleImport = async () => {
    if (selectedFiles.length === 0) {
      alert('Please select a folder first.');
      return;
    }

    if (parsedPreviewIds.length === 0) {
      alert('Please paste at least one Cell ID.');
      return;
    }

    setIsProcessing(true);
    try {
      const result = await onImport(selectedFiles, cellIdText);
      setSummary(result);
    } finally {
      setIsProcessing(false);
    }
  };

  const handleClose = () => {
    onClose();
  };

  return (
    <div className="fixed inset-0 z-[120] flex items-center justify-center bg-slate-950/80 p-6 backdrop-blur-md">
      <div className="flex max-h-[90vh] w-full max-w-4xl flex-col overflow-hidden rounded-3xl border border-slate-800 bg-slate-950 shadow-2xl shadow-black/40">
        <div className="flex items-center justify-between border-b border-slate-800 px-6 py-5">
          <div>
            <p className="text-[10px] font-black uppercase tracking-[0.28em] text-slate-500">Filtered Import</p>
            <h2 className="mt-1 text-2xl font-black tracking-tight text-white">Input Cell ID</h2>
          </div>
          <button
            onClick={handleClose}
            className="rounded-xl border border-slate-700 bg-slate-900 p-2.5 text-slate-400 transition-colors hover:text-white"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex flex-1 flex-col gap-6 overflow-y-auto px-6 py-6">
          <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-5">
            <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
              <div>
                <p className="text-xs font-black uppercase tracking-[0.24em] text-slate-500">1. Select Source Folder</p>
                <p className="mt-2 text-sm text-slate-400">
                  Choose one folder, then paste the Cell IDs to load only the matching image files.
                </p>
              </div>
              <div className="flex flex-col items-start gap-2 md:items-end">
                <label className="relative flex cursor-pointer items-center gap-2 overflow-hidden rounded-xl border border-blue-500/30 bg-blue-600/10 px-4 py-2.5 text-sm font-bold text-blue-400 transition-all hover:bg-blue-600/20">
                  <FolderOpen className="h-4 w-4" /> Select Folder
                  <input
                    ref={folderInputRef}
                    type="file"
                    multiple
                    className="absolute inset-0 cursor-pointer opacity-0"
                    onChange={handleFolderSelection}
                    {...({ webkitdirectory: '', directory: '' } as any)}
                  />
                </label>
                <span className="text-xs text-slate-500">
                  {selectedFolderName
                    ? `${selectedFolderName} · ${selectedFiles.length.toLocaleString()} files selected`
                    : 'No folder selected'}
                </span>
              </div>
            </div>

          </div>

          <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-5">
            <div className="mb-4 flex items-center justify-between gap-4">
              <div>
                <p className="text-xs font-black uppercase tracking-[0.24em] text-slate-500">2. Paste Cell IDs</p>
                <p className="mt-2 text-sm text-slate-400">One Cell ID per line.</p>
              </div>
              <div className="flex items-center gap-2 rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-xs font-mono text-slate-300">
                <Hash className="h-4 w-4 text-blue-400" />
                {parsedPreviewIds.length.toLocaleString()} IDs detected
              </div>
            </div>

            <textarea
              value={cellIdText}
              onChange={(e) => {
                setCellIdText(e.target.value);
                setSummary(null);
              }}
              placeholder={'J4037F26P6106475\nJ4037F28P6107026\nJ4037F2PP6113281'}
              className="h-72 w-full resize-none rounded-2xl border border-slate-800 bg-slate-950 px-4 py-4 text-sm text-slate-100 outline-none transition-colors placeholder:text-slate-600 focus:border-blue-500/50"
              spellCheck={false}
            />
          </div>

          {summary && (
            <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-5">
              <p className="text-xs font-black uppercase tracking-[0.24em] text-slate-500">3. Result Summary</p>

              <div className="mt-4 grid gap-3 md:grid-cols-3">
                <div className="rounded-xl border border-slate-800 bg-slate-950 p-4">
                  <p className="text-[10px] font-black uppercase tracking-[0.2em] text-slate-500">Input IDs</p>
                  <p className="mt-2 text-2xl font-black text-white">{summary.inputCount}</p>
                </div>
                <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-4">
                  <p className="text-[10px] font-black uppercase tracking-[0.2em] text-emerald-300/70">Matched Images</p>
                  <p className="mt-2 text-2xl font-black text-emerald-400">{summary.matchedCount}</p>
                </div>
                <div className="rounded-xl border border-red-500/20 bg-red-500/10 p-4">
                  <p className="text-[10px] font-black uppercase tracking-[0.2em] text-red-300/70">Unmatched IDs</p>
                  <p className="mt-2 text-2xl font-black text-red-400">{summary.unmatchedIds.length}</p>
                </div>
              </div>

              <div className="mt-5 rounded-2xl border border-slate-800 bg-slate-950 p-4">
                <div className="mb-3 flex items-center justify-between gap-4">
                  <p className="text-xs font-black uppercase tracking-[0.2em] text-slate-500">Unmatched Cell ID List</p>
                  <span className="text-xs text-slate-500">
                    {summary.unmatchedIds.length === 0 ? 'All IDs matched successfully' : `${summary.unmatchedIds.length} not found`}
                  </span>
                </div>

                {summary.unmatchedIds.length > 0 ? (
                  <div className="max-h-56 overflow-y-auto rounded-xl border border-slate-800 bg-slate-900/50 p-3 font-mono text-sm text-red-300">
                    {summary.unmatchedIds.map((id) => (
                      <div key={id} className="border-b border-slate-800/70 py-1.5 last:border-b-0">
                        {id}
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/10 px-4 py-3 text-sm font-medium text-emerald-300">
                    No unmatched Cell IDs.
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-slate-800 px-6 py-5">
          <div className="text-xs text-slate-500">
            This import replaces the current image list with matched results only.
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={handleClose}
              className="rounded-xl border border-slate-700 bg-slate-900 px-4 py-2.5 text-sm font-bold text-slate-300 transition-colors hover:bg-slate-800"
            >
              Close
            </button>
            <button
              onClick={handleImport}
              disabled={isProcessing}
              className="flex items-center gap-2 rounded-xl bg-blue-600 px-4 py-2.5 text-sm font-black text-white transition-colors hover:bg-blue-500 disabled:opacity-50"
            >
              {isProcessing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Hash className="h-4 w-4" />}
              Load Matched Images
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default CellIdImportModal;