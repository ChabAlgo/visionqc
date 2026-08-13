import React, { useEffect, useMemo, useRef, useState } from 'react';
import { CheckCircle2, Clock, Folder, ShieldAlert } from 'lucide-react';
import { ClassConfig, ImageFile } from '../types';
import { getClassConfigById, getClassLabel, getToneClasses, UNCLASSIFIED_STATUS } from '../classSettings';

interface FileExplorerProps {
  files: ImageFile[];
  currentIndex: number;
  onSelect: (index: number) => void;
  classConfigs: ClassConfig[];
}

const ROW_HEIGHT = 68;
const OVERSCAN = 8;

const LazyThumbnail: React.FC<{ file: File; alt: string }> = ({ file, alt }) => {
  const [src, setSrc] = useState('');
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    const url = URL.createObjectURL(file);
    setSrc(url);
    return () => URL.revokeObjectURL(url);
  }, [file, retry]);

  return (
    <img
      src={src}
      alt={alt}
      loading="lazy"
      decoding="async"
      onError={() => {
        if (retry < 1) setRetry((prev) => prev + 1);
      }}
      className="h-full w-full object-cover opacity-80"
    />
  );
};


const FileExplorer: React.FC<FileExplorerProps> = ({ files, currentIndex, onSelect, classConfigs }) => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(600);

  const getParentPath = (path?: string) => {
    if (!path) return '';
    const parts = path.split('/');
    if (parts.length <= 1) return '';
    return parts.slice(0, -1).join('/');
  };

  useEffect(() => {
    const element = scrollRef.current;
    if (!element) return;

    const updateHeight = () => setViewportHeight(element.clientHeight || 600);
    updateHeight();

    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', updateHeight);
      return () => window.removeEventListener('resize', updateHeight);
    }

    const observer = new ResizeObserver(updateHeight);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    const element = scrollRef.current;
    if (!element || files.length === 0) return;

    const rowTop = currentIndex * ROW_HEIGHT;
    const rowBottom = rowTop + ROW_HEIGHT;
    const visibleTop = element.scrollTop;
    const visibleBottom = visibleTop + element.clientHeight;

    if (rowTop < visibleTop || rowBottom > visibleBottom) {
      element.scrollTo({ top: Math.max(0, rowTop - element.clientHeight / 2), behavior: 'auto' });
    }
  }, [currentIndex, files.length]);

  const { startIndex, endIndex, visibleFiles } = useMemo(() => {
    const start = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
    const end = Math.min(files.length, Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + OVERSCAN);
    return {
      startIndex: start,
      endIndex: end,
      visibleFiles: files.slice(start, end)
    };
  }, [files, scrollTop, viewportHeight]);

  return (
    <div className="flex h-full min-h-0 flex-col gap-1.5">
      <div className="mb-2 flex items-center justify-between px-1 shrink-0">
        <span className="text-[10px] font-black uppercase tracking-widest text-slate-500">File Explorer</span>
        <span className="text-[10px] font-mono text-slate-600">
          {files.length.toLocaleString()} ITEMS
          {files.length > 0 ? ` · ${startIndex + 1}-${endIndex}` : ''}
        </span>
      </div>

      <div
        ref={scrollRef}
        onScroll={(event) => setScrollTop(event.currentTarget.scrollTop)}
        className="min-h-0 flex-1 overflow-y-auto pr-1 no-scrollbar"
      >
        <div className="relative" style={{ height: `${files.length * ROW_HEIGHT}px` }}>
          {visibleFiles.map((img, relativeIndex) => {
            const idx = startIndex + relativeIndex;
            const labelIds = Array.isArray(img.labels) && img.labels.length > 0
              ? img.labels.filter((labelId) => labelId && labelId !== UNCLASSIFIED_STATUS)
              : img.status !== UNCLASSIFIED_STATUS
                ? [img.status]
                : [];
            const classConfig = labelIds.length > 0 ? getClassConfigById(classConfigs, labelIds[0]) : null;
            const tone = classConfig ? getToneClasses(classConfig.tone, classConfig.kind) : null;
            const isPending = labelIds.length === 0;

            return (
              <button
                key={img.id}
                type="button"
                onClick={() => onSelect(idx)}
                style={{ top: `${idx * ROW_HEIGHT}px`, height: `${ROW_HEIGHT - 6}px` }}
                className={`absolute left-0 right-0 group flex items-center gap-3 rounded-lg border p-2 text-left transition-all duration-150 
                  ${idx === currentIndex ? 'border-blue-500 bg-blue-500/10' : 'border-transparent bg-slate-800/30 hover:bg-slate-800/60'}`}
              >
                <div className="relative h-10 w-10 shrink-0 overflow-hidden rounded bg-slate-900">
                  <LazyThumbnail file={img.file} alt={img.file.name} />
                  {!isPending && tone ? (
                    <div className={`absolute inset-0 flex items-center justify-center ${tone.chip.split(' ')[0]}`}>
                      {classConfig?.kind === 'ok' ? (
                        <CheckCircle2 className="h-5 w-5 text-emerald-300" />
                      ) : (
                        <ShieldAlert className="h-5 w-5 text-white/70" />
                      )}
                    </div>
                  ) : null}
                </div>

                <div className="flex flex-1 flex-col overflow-hidden">
                  <span className={`truncate text-[11px] font-medium ${idx === currentIndex ? 'text-blue-200' : 'text-slate-300'}`}>
                    {img.file.name}
                  </span>
                  <div className="mt-0.5 flex flex-col gap-0.5">
                    {img.path && getParentPath(img.path) ? (
                      <div className="flex items-center gap-1 opacity-50">
                        <Folder className="h-2 w-2" />
                        <span className="truncate text-[8px] font-mono tracking-tight uppercase">{getParentPath(img.path)}</span>
                      </div>
                    ) : null}

                    <div className="flex items-center gap-1.5">
                      {isPending ? (
                        <>
                          <Clock className="h-2.5 w-2.5 text-slate-500" />
                          <span className="text-[9px] text-slate-500 uppercase tracking-tight">Pending</span>
                        </>
                      ) : (
                        <span className={`truncate text-[9px] font-black uppercase tracking-tight ${tone?.text || 'text-slate-300'}`}>
                          {labelIds.map((labelId) => getClassLabel(labelId, classConfigs)).join(' + ')}
                        </span>
                      )}
                    </div>
                  </div>
                </div>

                {img.aiSuggestion ? (
                  <div className="h-1.5 w-1.5 rounded-full bg-indigo-500 animate-pulse shadow-[0_0_8px_rgba(99,102,241,0.8)]" title="AI Analyzed" />
                ) : null}
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
};

export default FileExplorer;
