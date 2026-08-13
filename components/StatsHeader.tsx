import React from 'react';
import { ClassConfig, ClassificationStats } from '../types';
import { getToneClasses } from '../classSettings';

interface StatsHeaderProps {
  stats: ClassificationStats;
  classConfigs: ClassConfig[];
}

const StatsHeader: React.FC<StatsHeaderProps> = ({ stats, classConfigs }) => {
  const percentage = stats.total > 0 ? Math.round(((stats.total - stats.remaining) / stats.total) * 100) : 0;

  return (
    <div className="hidden items-center gap-6 xl:flex max-w-[780px] overflow-x-auto no-scrollbar">
      <div className="flex flex-col shrink-0">
        <span className="text-[9px] font-bold uppercase tracking-widest text-slate-500">Progress</span>
        <div className="flex items-center gap-2">
          <div className="h-1.5 w-24 overflow-hidden rounded-full bg-slate-800">
            <div className="h-full bg-blue-500 transition-all duration-500" style={{ width: `${percentage}%` }} />
          </div>
          <span className="text-xs font-mono font-semibold text-blue-400">{percentage}%</span>
        </div>
      </div>

      <div className="flex gap-4 border-l border-slate-800 pl-6 shrink-0">
        {classConfigs.filter((item) => item.enabled).map((item) => {
          const tone = getToneClasses(item.tone, item.kind);
          return (
            <div key={item.id} className="flex flex-col items-center min-w-[44px]">
              <span className="text-[9px] font-bold uppercase tracking-widest text-slate-500">{item.label}</span>
              <span className={`text-sm font-bold ${tone.text}`}>{stats.counts[item.id] || 0}</span>
            </div>
          );
        })}

        <div className="flex flex-col items-center min-w-[44px]">
          <span className="text-[9px] font-bold uppercase tracking-widest text-slate-500">Rem.</span>
          <span className="text-sm font-bold text-slate-400">{stats.remaining}</span>
        </div>
      </div>
    </div>
  );
};

export default StatsHeader;
