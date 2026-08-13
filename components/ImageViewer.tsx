import React, { useState, useRef, useEffect, useCallback } from 'react';
import {
  ChevronLeft,
  ChevronRight,
  Maximize2,
  Minimize2,
  ZoomIn,
  ZoomOut,
  Sparkles,
  Eye,
  EyeOff,
  Target
} from 'lucide-react';
import { ClassConfig, ImageFile } from '../types';
import { getClassConfigById, getClassLabel, getToneClasses, UNCLASSIFIED_STATUS } from '../classSettings';

interface ImageViewerProps {
  image: ImageFile;
  onNext: () => void;
  onPrev: () => void;
  hasPrev: boolean;
  hasNext: boolean;
  currentIndex: number;
  total: number;
  classConfigs: ClassConfig[];
}

const MIN_ZOOM = 0.5;
const MAX_ZOOM = 5.0;
const ZOOM_STEP = 0.1;

type AnchorPoint = {
  xRatio: number;
  yRatio: number;
  viewportX: number;
  viewportY: number;
};

const ImageViewer: React.FC<ImageViewerProps> = ({
  image,
  onNext,
  onPrev,
  hasPrev,
  hasNext,
  currentIndex,
  total,
  classConfigs
}) => {
  const [isZoomed, setIsZoomed] = useState(false);
  const [zoomScale, setZoomScale] = useState<number>(1);
  const [showHeatmap, setShowHeatmap] = useState(true);
  const [isImgLoaded, setIsImgLoaded] = useState(false);
  const [imageUrl, setImageUrl] = useState('');
  const [imageRetry, setImageRetry] = useState(0);
  const [naturalSize, setNaturalSize] = useState({ width: 0, height: 0 });
  const [isDragging, setIsDragging] = useState(false);

  const viewportRef = useRef<HTMLDivElement>(null);
  const imageElementRef = useRef<HTMLImageElement>(null);
  const scrollPositionRef = useRef({ left: 0, top: 0 });
  const pendingAnchorRef = useRef<AnchorPoint | null>(null);
  const wheelAnchorRef = useRef<AnchorPoint | null>(null);
  const wheelBurstTimerRef = useRef<number | null>(null);
  const zoomScaleRef = useRef(1);
  const dragStateRef = useRef({ startX: 0, startY: 0, scrollLeft: 0, scrollTop: 0 });

  const clampZoom = useCallback((value: number) => {
    return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, Number(value.toFixed(2))));
  }, []);

  useEffect(() => {
    zoomScaleRef.current = zoomScale;
  }, [zoomScale]);

  useEffect(() => () => {
    if (wheelBurstTimerRef.current !== null) {
      window.clearTimeout(wheelBurstTimerRef.current);
    }
  }, []);

  useEffect(() => {
    setImageRetry(0);
    setIsDragging(false);
    wheelAnchorRef.current = null;

    const viewport = viewportRef.current;
    if (viewport && isZoomed && naturalSize.width > 0 && naturalSize.height > 0) {
      const viewportX = viewport.clientWidth / 2;
      const viewportY = viewport.clientHeight / 2;
      const scaledWidth = naturalSize.width * zoomScaleRef.current;
      const scaledHeight = naturalSize.height * zoomScaleRef.current;

      pendingAnchorRef.current = {
        xRatio: Math.min(1, Math.max(0, (viewport.scrollLeft + viewportX) / Math.max(1, scaledWidth))),
        yRatio: Math.min(1, Math.max(0, (viewport.scrollTop + viewportY) / Math.max(1, scaledHeight))),
        viewportX,
        viewportY
      };

      scrollPositionRef.current = {
        left: viewport.scrollLeft,
        top: viewport.scrollTop
      };
    }
  }, [image.id]);

  useEffect(() => {
    const url = URL.createObjectURL(image.file);
    setImageUrl(url);
    setIsImgLoaded(false);
    setNaturalSize({ width: 0, height: 0 });
    return () => URL.revokeObjectURL(url);
  }, [image.file, image.id, imageRetry]);

  const handleScroll = useCallback(() => {
    if (viewportRef.current && isZoomed) {
      scrollPositionRef.current = {
        left: viewportRef.current.scrollLeft,
        top: viewportRef.current.scrollTop
      };
    }
  }, [isZoomed]);

  const rememberAnchor = useCallback(
    (viewportX?: number, viewportY?: number): AnchorPoint | null => {
      const viewport = viewportRef.current;
      if (!viewport || naturalSize.width === 0 || naturalSize.height === 0) return null;

      const safeViewportX = viewportX ?? viewport.clientWidth / 2;
      const safeViewportY = viewportY ?? viewport.clientHeight / 2;
      let xRatio = 0.5;
      let yRatio = 0.5;

      if (isZoomed) {
        const currentScale = zoomScaleRef.current;
        const scaledWidth = naturalSize.width * currentScale;
        const scaledHeight = naturalSize.height * currentScale;
        xRatio = (viewport.scrollLeft + safeViewportX) / Math.max(1, scaledWidth);
        yRatio = (viewport.scrollTop + safeViewportY) / Math.max(1, scaledHeight);
      } else {
        const imageElement = imageElementRef.current;
        const viewportRect = viewport.getBoundingClientRect();
        const imageRect = imageElement?.getBoundingClientRect();

        if (imageRect && imageRect.width > 0 && imageRect.height > 0) {
          const clientX = viewportRect.left + safeViewportX;
          const clientY = viewportRect.top + safeViewportY;
          xRatio = (clientX - imageRect.left) / imageRect.width;
          yRatio = (clientY - imageRect.top) / imageRect.height;
        }
      }

      const anchor = {
        xRatio: Math.min(1, Math.max(0, xRatio)),
        yRatio: Math.min(1, Math.max(0, yRatio)),
        viewportX: safeViewportX,
        viewportY: safeViewportY
      };
      pendingAnchorRef.current = anchor;
      return anchor;
    },
    [isZoomed, naturalSize.height, naturalSize.width]
  );

  useEffect(() => {
    if (!isImgLoaded || !isZoomed || !viewportRef.current) return;

    requestAnimationFrame(() => {
      const viewport = viewportRef.current;
      if (!viewport) return;

      if (pendingAnchorRef.current && naturalSize.width > 0 && naturalSize.height > 0) {
        const scaledWidth = naturalSize.width * zoomScale;
        const scaledHeight = naturalSize.height * zoomScale;

        viewport.scrollLeft = pendingAnchorRef.current.xRatio * scaledWidth - pendingAnchorRef.current.viewportX;
        viewport.scrollTop = pendingAnchorRef.current.yRatio * scaledHeight - pendingAnchorRef.current.viewportY;
        pendingAnchorRef.current = null;
      } else {
        viewport.scrollLeft = scrollPositionRef.current.left;
        viewport.scrollTop = scrollPositionRef.current.top;
      }

      scrollPositionRef.current = {
        left: viewport.scrollLeft,
        top: viewport.scrollTop
      };
    });
  }, [isImgLoaded, isZoomed, image.id, naturalSize.height, naturalSize.width, zoomScale]);

  useEffect(() => {
    if (!isDragging) return;

    const handleMouseMove = (e: MouseEvent) => {
      const viewport = viewportRef.current;
      if (!viewport) return;

      e.preventDefault();

      const dx = e.clientX - dragStateRef.current.startX;
      const dy = e.clientY - dragStateRef.current.startY;

      viewport.scrollLeft = dragStateRef.current.scrollLeft - dx;
      viewport.scrollTop = dragStateRef.current.scrollTop - dy;

      scrollPositionRef.current = {
        left: viewport.scrollLeft,
        top: viewport.scrollTop
      };
    };

    const handleMouseUp = () => setIsDragging(false);

    window.addEventListener('mousemove', handleMouseMove, { passive: false });
    window.addEventListener('mouseup', handleMouseUp);

    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [isDragging]);

  const changeZoom = useCallback(
    (nextZoom: number, viewportX?: number, viewportY?: number) => {
      const clamped = clampZoom(nextZoom);
      if (clamped === zoomScaleRef.current && isZoomed) return;

      wheelAnchorRef.current = null;
      rememberAnchor(viewportX, viewportY);
      zoomScaleRef.current = clamped;
      if (!isZoomed) setIsZoomed(true);
      setZoomScale(clamped);
    },
    [clampZoom, isZoomed, rememberAnchor]
  );

  const handleToggleZoom = () => {
    wheelAnchorRef.current = null;
    if (isZoomed) {
      setIsZoomed(false);
      setIsDragging(false);
      pendingAnchorRef.current = null;
      return;
    }

    zoomScaleRef.current = zoomScale;
    setIsZoomed(true);
  };

  const handleViewportClick = (e: React.MouseEvent) => {
    if ((e.target as HTMLElement).closest('button')) return;
    if (isZoomed) return;
    handleToggleZoom();
  };

  const handleWheel = (e: React.WheelEvent<HTMLDivElement>) => {
    const viewport = viewportRef.current;
    if (!viewport) return;

    e.preventDefault();
    e.stopPropagation();

    const rect = viewport.getBoundingClientRect();
    const viewportX = e.clientX - rect.left;
    const viewportY = e.clientY - rect.top;
    const normalizedDelta = e.deltaMode === 1
      ? e.deltaY * 16
      : e.deltaMode === 2
        ? e.deltaY * viewport.clientHeight
        : e.deltaY;

    if (!isZoomed && normalizedDelta > 0) return;

    if (!wheelAnchorRef.current) {
      wheelAnchorRef.current = rememberAnchor(viewportX, viewportY);
    }

    const direction = normalizedDelta < 0 ? 1 : -1;
    const stepCount = Math.min(4, Math.max(1, Math.round(Math.abs(normalizedDelta) / 100)));
    const nextZoom = clampZoom(zoomScaleRef.current + direction * ZOOM_STEP * stepCount);

    if (nextZoom !== zoomScaleRef.current) {
      if (wheelAnchorRef.current) pendingAnchorRef.current = wheelAnchorRef.current;
      zoomScaleRef.current = nextZoom;
      if (!isZoomed) setIsZoomed(true);
      setZoomScale(nextZoom);
    }

    if (wheelBurstTimerRef.current !== null) {
      window.clearTimeout(wheelBurstTimerRef.current);
    }
    wheelBurstTimerRef.current = window.setTimeout(() => {
      wheelAnchorRef.current = null;
      wheelBurstTimerRef.current = null;
    }, 120);
  };

  const handleMouseDown = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!isZoomed) return;
    if (e.button !== 0) return;
    if ((e.target as HTMLElement).closest('button')) return;

    const viewport = viewportRef.current;
    if (!viewport) return;

    e.preventDefault();

    dragStateRef.current = {
      startX: e.clientX,
      startY: e.clientY,
      scrollLeft: viewport.scrollLeft,
      scrollTop: viewport.scrollTop
    };

    setIsDragging(true);
  };

  const zoomedWidth = isZoomed && naturalSize.width > 0 ? `${naturalSize.width * zoomScale}px` : 'auto';
  const zoomedHeight = isZoomed && naturalSize.height > 0 ? `${naturalSize.height * zoomScale}px` : 'auto';

  const activeLabelIds = Array.isArray(image.labels) && image.labels.length > 0
    ? image.labels.filter((labelId) => labelId && labelId !== UNCLASSIFIED_STATUS)
    : image.status !== UNCLASSIFIED_STATUS
      ? [image.status]
      : [];
  const activeClassConfig = activeLabelIds.length > 0 ? getClassConfigById(classConfigs, activeLabelIds[0]) : null;
  const activeTone = activeClassConfig ? getToneClasses(activeClassConfig.tone, activeClassConfig.kind) : null;
  const aiClassConfig = image.aiSuggestion ? getClassConfigById(classConfigs, image.aiSuggestion.status) : null;

  return (
    <div className="relative flex flex-1 flex-col p-4 md:p-6 min-h-0 bg-slate-950 overflow-hidden">
      <div className="mb-4 flex items-center justify-between shrink-0">
        <div className="flex flex-col">
          <span className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Inspection Asset</span>
          <h2 className="text-lg font-bold text-slate-200 truncate max-w-[300px] md:max-w-[500px] tracking-tight">{image.file.name}</h2>
        </div>

        <div className="flex items-center gap-3">
          {image.aiSuggestion?.regions && image.aiSuggestion.regions.length > 0 ? (
            <button
              onClick={() => setShowHeatmap(!showHeatmap)}
              className={`flex items-center gap-2 rounded-lg px-3 py-2 text-xs font-black uppercase tracking-tighter transition-all border ${showHeatmap ? 'bg-red-600 border-red-500 text-white shadow-lg shadow-red-600/20' : 'bg-slate-800 text-slate-400 border-slate-700'}`}
            >
              {showHeatmap ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
              Heatmap {showHeatmap ? 'Active' : 'Off'}
            </button>
          ) : null}

          <div className="flex h-10 items-center gap-1 rounded-xl bg-slate-900 px-4 border border-slate-800">
            <span className="text-sm font-mono font-bold text-blue-400">{currentIndex + 1}</span>
            <span className="text-[10px] font-black text-slate-600">/</span>
            <span className="text-sm font-mono font-bold text-slate-500">{total}</span>
          </div>

          <div className="flex items-center gap-2 rounded-xl border border-slate-800 bg-slate-900 px-2 py-1.5">
            <button onClick={() => changeZoom(zoomScale - ZOOM_STEP)} disabled={!isZoomed} className="rounded-lg p-2 text-slate-300 transition-colors hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-40" title="Zoom Out">
              <ZoomOut className="h-4 w-4" />
            </button>

            <div className="min-w-[68px] text-center text-xs font-black text-blue-400">{Math.round(zoomScale * 100)}%</div>

            <button onClick={() => changeZoom(zoomScale + ZOOM_STEP)} disabled={!isZoomed} className="rounded-lg p-2 text-slate-300 transition-colors hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-40" title="Zoom In">
              <ZoomIn className="h-4 w-4" />
            </button>
          </div>

          <button
            onClick={handleToggleZoom}
            className={`rounded-xl p-2.5 transition-all border ${isZoomed ? 'bg-blue-600 border-blue-500 text-white shadow-lg shadow-blue-600/20' : 'bg-slate-800 border-slate-700 text-slate-400 hover:text-white'}`}
            title="Toggle Fit/Zoom"
          >
            {isZoomed ? <Minimize2 className="h-5 w-5" /> : <Maximize2 className="h-5 w-5" />}
          </button>
        </div>
      </div>

      <div className="group relative flex flex-1 items-center justify-center overflow-hidden rounded-2xl bg-black shadow-inner ring-1 ring-white/5 min-h-0">
        <button onClick={(e) => { e.stopPropagation(); onPrev(); }} disabled={!hasPrev} className="absolute left-6 z-30 flex h-16 w-16 items-center justify-center rounded-full bg-black/40 text-white opacity-0 transition-all hover:bg-black/80 hover:scale-110 active:scale-95 group-hover:opacity-100 disabled:hidden backdrop-blur-md border border-white/10">
          <ChevronLeft className="h-10 w-10" />
        </button>

        <div
          ref={viewportRef}
          onScroll={handleScroll}
          onClick={handleViewportClick}
          onWheel={handleWheel}
          onMouseDown={handleMouseDown}
          className={`relative h-full w-full transition-colors duration-300 ${isZoomed ? isDragging ? 'overflow-hidden cursor-grabbing' : 'overflow-hidden cursor-grab' : 'flex items-center justify-center overflow-hidden cursor-zoom-in'}`}
        >
          <div className={`relative flex ${isZoomed ? 'min-h-full min-w-full items-start justify-start' : 'h-full w-full items-center justify-center'}`}>
            <img
              ref={imageElementRef}
              src={imageUrl}
              alt={image.file.name}
              draggable={false}
              decoding="async"
              onError={() => {
                if (imageRetry < 2) setImageRetry((prev) => prev + 1);
              }}
              onDragStart={(e) => e.preventDefault()}
              onLoad={(e) => {
                setNaturalSize({ width: e.currentTarget.naturalWidth, height: e.currentTarget.naturalHeight });
                setIsImgLoaded(true);
              }}
              className={`transition-opacity duration-300 ease-out select-none block ${!isImgLoaded ? 'opacity-0' : 'opacity-100'}`}
              style={{
                maxWidth: isZoomed ? 'none' : '100%',
                maxHeight: isZoomed ? 'none' : '100%',
                width: isZoomed ? zoomedWidth : 'auto',
                height: isZoomed ? zoomedHeight : 'auto',
                objectFit: 'contain'
              }}
            />

            {isImgLoaded && showHeatmap && image.aiSuggestion?.regions ? image.aiSuggestion.regions.map((region, i) => (
              <div key={i} className="absolute pointer-events-none z-20" style={{ top: `${region.ymin / 10}%`, left: `${region.xmin / 10}%`, width: `${(region.xmax - region.xmin) / 10}%`, height: `${(region.ymax - region.ymin) / 10}%` }}>
                <div className="absolute inset-0 border-[3px] border-red-500 rounded-sm shadow-[0_0_20px_rgba(255,0,0,0.6)] animate-pulse" />
                <div className="absolute inset-0 bg-red-600/15" />
                <div className="absolute -top-7 left-0 flex items-center gap-1.5 rounded-md bg-red-600 px-2 py-1 text-[9px] font-black text-white shadow-xl whitespace-nowrap">
                  <Target className="h-3 w-3" /> DEFECT DETECTED
                </div>
              </div>
            )) : null}
          </div>
        </div>

        <button onClick={(e) => { e.stopPropagation(); onNext(); }} disabled={!hasNext} className="absolute right-6 z-30 flex h-16 w-16 items-center justify-center rounded-full bg-black/40 text-white opacity-0 transition-all hover:bg-black/80 hover:scale-110 active:scale-95 group-hover:opacity-100 disabled:hidden backdrop-blur-md border border-white/10">
          <ChevronRight className="h-10 w-10" />
        </button>

        {activeLabelIds.length > 0 && activeClassConfig ? (
          <div className={`absolute right-10 top-10 z-40 max-w-[55%] rounded-3xl px-5 py-3 shadow-2xl backdrop-blur-lg border-2 ${activeTone?.badge || 'bg-slate-500/20 text-slate-300 border-slate-500/50'} transition-all animate-in zoom-in-75 duration-300 pointer-events-none`}>
            <div className="flex flex-wrap items-center justify-end gap-2">
              {activeLabelIds.map((labelId) => {
                const cfg = getClassConfigById(classConfigs, labelId);
                const tone = cfg ? getToneClasses(cfg.tone, cfg.kind) : null;
                return (
                  <span key={labelId} className={`rounded-full border px-3 py-1 text-sm font-black uppercase tracking-widest ${tone?.chip || 'border-slate-500/50 bg-slate-500/20 text-slate-200'}`}>
                    {getClassLabel(labelId, classConfigs)}
                  </span>
                );
              })}
            </div>
          </div>
        ) : null}

        {image.aiSuggestion ? (
          <div className="absolute bottom-8 left-1/2 -translate-x-1/2 z-40 flex w-full max-w-[85%] items-start gap-5 rounded-2xl border border-indigo-500/30 bg-indigo-950/90 p-5 shadow-2xl backdrop-blur-2xl animate-in slide-in-from-bottom-8 duration-500 pointer-events-none">
            <div className="mt-0.5 flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-indigo-600/40 text-indigo-300 border border-indigo-500/20">
              <Sparkles className="h-7 w-7" />
            </div>
            <div className="flex flex-1 flex-col gap-1">
              <div className="flex items-center gap-3">
                <span className="text-[11px] font-black uppercase tracking-[0.2em] text-indigo-400">AI Intelligent Analysis</span>
                <span className={`rounded-md px-2 py-0.5 text-[10px] font-black uppercase border ${aiClassConfig?.kind === 'ok' ? 'bg-emerald-500/20 border-emerald-500/30 text-emerald-400' : 'bg-red-500/20 border-red-500/30 text-red-400'}`}>
                  {getClassLabel(image.aiSuggestion.status, classConfigs)} · {(image.aiSuggestion.confidence * 100).toFixed(1)}%
                </span>
              </div>
              <p className="text-sm leading-relaxed text-indigo-50 font-semibold tracking-tight">{image.aiSuggestion.reason}</p>
            </div>
          </div>
        ) : null}
      </div>

      <div className="mt-4 flex items-center justify-between text-[10px] text-slate-500 uppercase font-black tracking-widest shrink-0">
        <div className="flex gap-6">
          <div className="flex items-center gap-1.5"><span className="text-slate-600">FORMAT:</span><span className="text-slate-300 font-mono tracking-tighter">{image.file.type.split('/')[1] || 'IMG'}</span></div>
          <div className="flex items-center gap-1.5"><span className="text-slate-600">SIZE:</span><span className="text-slate-300 font-mono tracking-tighter">{(image.file.size / 1024).toFixed(1)} KB</span></div>
        </div>
        <div className="flex items-center gap-2 text-blue-500/80"><Maximize2 className="h-3.5 w-3.5" />{isZoomed ? `ZOOM ${Math.round(zoomScale * 100)}% (WHEEL / DRAG)` : 'ADAPTIVE FIT VIEW'}</div>
      </div>
    </div>
  );
};

export default ImageViewer;
