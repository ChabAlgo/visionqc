(() => {
  'use strict';

  const VERSION = '4.4.17';
  const POSITIONS = ['CA(TOP)', 'AN(TOP)', 'CA(BOT)', 'AN(BOT)'];
  const PAGE_KEY = 'visionqc-v43-active-page';
  const DB_NAME = 'visionqc-analysis-input-db-v1';
  const DB_VERSION = 1;
  const STORE_NAME = 'handles';
  const NG_ROOT_KEY = 'ng-root';
  const RESULT_PREFIX = 'result:';
  const THRESHOLD_KEY = 'visionqc-v439-tool-thresholds';
  const IMG_RE = /\.(png|jpe?g|bmp|gif|webp|tif?f)$/i;
  const LOCAL_AGENT_URL = 'http://127.0.0.1:17891';
  const safeStorageGet = (key) => { try { return localStorage.getItem(key); } catch (_) { return null; } };
  const safeStorageSet = (key, value) => { try { localStorage.setItem(key, value); } catch (_) { /* unavailable origin */ } };
  const safeJsonParse = (value, fallback = {}) => { try { return value ? JSON.parse(value) : fallback; } catch (_) { return fallback; } };
  const state = {
    page: safeStorageGet(PAGE_KEY) || 'classification',
    menuOpen: false,
    resultInputs: {},
    ngRootName: '',
    ngImages: [],
    ngWarnings: [],
    restoreWarnings: [],
    model: null,
    selectedMissPosition: 'CA(TOP)',
    analysisTool: '',
    analysisScope: 'TOOL_NG',
    analysisPosition: 'ALL',
    analysisScoreCutoff: 0.80,
    analysisScoreCompare: 'GTE',
    analysisPoints: [],
    analysisPointMap: new Map(),
    simulationMode: 'integrated',
    simulationAgent: { status: 'idle', version: '-', vpdl: '-', license: '-', gpu: '-', message: 'Local Agent 연결 전' },
    simulationChecking: false,
    simulationEvents: null,
    simulationProgress: { running:false, processed:0, total:0, ok:0, ng:0, current:'-', message:'Ready', error:'' },
    simulationForm: { outputRoot:'', gpuDevices:'0', jpegQuality:80, printEvery:100, useGpu:true, keepSubfolders:false, keepCropImages:false, heatmapImageSave:true, positions:{} },
    thresholds: safeJsonParse(safeStorageGet(THRESHOLD_KEY), {}),
    loading: '',
    modalMissKey: null,
    modalItem: null,
    modalIndex: 0,
    modalUrl: '',
    modalZoom: 1,
    modalPanX: 0,
    modalPanY: 0,
    modalDragging: false,
    modalDragStartX: 0,
    modalDragStartY: 0,
    chartModalZoom: 1,
    chartModalPanX: 0,
    chartModalPanY: 0,
    chartModalDragging: false,
    chartModalDragStartX: 0,
    chartModalDragStartY: 0,
    initialized: false
  };

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));
  const escapeHtml = (value) => String(value ?? '')
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#039;');
  const numberText = (value) => Number(value || 0).toLocaleString('ko-KR');
  const rateText = (value) => `${(Number(value || 0) * 100).toFixed(2)}%`;
  const scoreText = (value) => Number.isFinite(value) ? Number(value).toFixed(4) : '-';
  const normalizeHeader = (value) => String(value ?? '').trim();
  const normalizeHeaderKey = (value) => normalizeHeader(value).replace(/\s+/g, '').toLowerCase();
  const normalizePath = (value) => String(value || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, '');
  const normalizePositionToken = (value) => String(value ?? '').toUpperCase().replace(/[^A-Z]/g, '');
  const normalizePosition = (value) => POSITIONS.find((position) => normalizePositionToken(position) === normalizePositionToken(value)) || null;
  const extractCellId = (value) => {
    const match = String(value ?? '').match(/[JPB][A-Za-z0-9]{15}/);
    return match ? match[0].toUpperCase() : null;
  };
  const normalizeResult = (value) => {
    const text = String(value ?? '').trim().toUpperCase();
    if (['OK', 'PASS', 'GOOD'].includes(text)) return 'OK';
    if (['NG', 'FAIL', 'BAD'].includes(text)) return 'NG';
    return 'UNKNOWN';
  };
  const parseNumber = (value) => {
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    const text = String(value ?? '').trim();
    if (!text) return null;
    const parsed = Number(text.replace(/,/g, ''));
    return Number.isFinite(parsed) ? parsed : null;
  };
  const mean = (values) => values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null;
  const median = (values) => {
    if (!values.length) return null;
    const sorted = [...values].sort((a, b) => a - b);
    const middle = Math.floor(sorted.length / 2);
    return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
  };
  const resultKey = (position, cellId) => `${position}|${cellId}`;

  const thresholdKey = (position, tool) => `${position}|${tool}`;
  const clampScore = (value, fallback = 0.50) => {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? Math.max(0.50, Math.min(1.00, parsed)) : fallback;
  };
  const getThreshold = (position, tool) => clampScore(state.thresholds[thresholdKey(position, tool)], 0.50);
  const persistThresholds = () => safeStorageSet(THRESHOLD_KEY, JSON.stringify(state.thresholds));
  const setThreshold = (position, tool, value) => {
    state.thresholds[thresholdKey(position, tool)] = clampScore(value, 0.50);
    persistThresholds();
  };

  function showToast(message, error = false) {
    const toast = $('#vq43-toast');
    if (!toast) return;
    toast.textContent = message;
    toast.classList.toggle('error', error);
    toast.classList.add('show');
    clearTimeout(showToast.timer);
    showToast.timer = setTimeout(() => toast.classList.remove('show'), 3600);
  }

  function menuIconSvg() {
    return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/></svg>';
  }

  function createExtensionDom() {
    if (!$('#vq43-menu-button')) {
      document.body.insertAdjacentHTML('beforeend', `
        <button id="vq43-menu-button" type="button" title="메뉴 열기" aria-controls="vq43-drawer" aria-expanded="false">${menuIconSvg()}<span>메뉴</span></button>
        <div id="vq43-menu-backdrop" aria-hidden="true"></div>
        <aside id="vq43-drawer" aria-hidden="true">
          <div class="vq43-drawer-head"><div><strong>VisionQC 메뉴</strong><small>SIMULATION ANALYSIS</small></div><button class="vq43-close" type="button" aria-label="메뉴 닫기">×</button></div>
          <nav class="vq43-nav">
            <div class="vq43-nav-label">Navigation</div>
            ${navItem('main', '▣', '메인', 'Cell · Position · Tool NG율')}
            ${navItem('analysis', '⌁', '분석', 'Tool별 Score 세부 분석')}
            ${navItem('classification', '▤', '분류', '기존 이미지 분류 기능')}
            ${navItem('simulation', '▶', '시뮬레이션', 'VPDL Local Runtime · GPU')}
            ${navItem('settings', '⚙', '설정', '결과 파일 · 실제 NG 경로')}
          </nav>
          <div class="vq43-drawer-foot">VPDL Simulation은 사용자 PC의 Local Agent · Runtime · GPU에서 실행하도록 구성합니다.</div>
        </aside>
        <section id="vq43-shell"><div id="vq43-page" class="vq43-page"></div></section>
        <div id="vq43-chart-modal"></div>
        <div id="vq43-modal"></div>
        <div id="vq43-toast"></div>
      `);
    }

    const menuButton = $('#vq43-menu-button');
    const menuBackdrop = $('#vq43-menu-backdrop');
    const drawer = $('#vq43-drawer');
    const shell = $('#vq43-shell');
    const closeButton = $('#vq43-drawer .vq43-close');

    // 메뉴는 하나의 click 이벤트에서만 토글합니다.
    // pointerdown과 click을 함께 사용하면 누를 때 열리고 뗄 때 닫히는 이중 토글이 발생합니다.
    if (menuButton) {
      menuButton.onclick = (event) => {
        event.preventDefault();
        event.stopPropagation();
        toggleMenu(!state.menuOpen);
      };
    }
    if (menuBackdrop) menuBackdrop.onclick = () => toggleMenu(false);
    if (closeButton) closeButton.onclick = () => toggleMenu(false);

    // 메뉴 항목마다 직접 click handler를 연결합니다. 이벤트 위임이 차단돼도 페이지가 전환됩니다.
    $$('.vq43-nav-item', drawer || document).forEach((button) => {
      button.onclick = (event) => {
        event.preventDefault();
        event.stopPropagation();
        setPage(button.dataset.vqPage || 'classification');
      };
    });

    // 확장 화면은 렌더링 직후 각 컨트롤에 직접 이벤트를 연결합니다.
    // shell/body 위임을 겹쳐 쓰면 마우스 down/up 사이에 DOM이 다시 그려져
    // 메뉴와 드롭다운이 즉시 닫히는 현상이 발생할 수 있습니다.

    if (!window.__VISIONQC_V437_GLOBAL_EVENTS_BOUND__) {
      window.__VISIONQC_V437_GLOBAL_EVENTS_BOUND__ = true;
      // React 상단의 Input/Load 버튼만 body 단계에서 확인합니다.
      document.body.addEventListener('click', handleDelegatedClick);
      document.addEventListener('click', () => closeAnalysisDropdowns());

      window.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
          if ($('#vq43-modal')?.classList.contains('open')) closeModal();
          else if ($('#vq43-chart-modal')?.classList.contains('open')) closeChartModal();
          else if (state.menuOpen) toggleMenu(false);
        }
      });

      // 분류 페이지가 아닐 때 기존 React 분류 단축키가 실행되지 않도록 캡처 단계에서 차단합니다.
      window.addEventListener('keydown', (event) => {
        if (state.page === 'classification') return;
        const target = event.target;
        const isTyping = target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT' || target.isContentEditable);
        if (isTyping || event.ctrlKey || event.metaKey || event.altKey) return;
        const classificationKeys = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Enter', ' ', 'Backspace'];
        if (classificationKeys.includes(event.key) || event.key.length === 1) {
          event.preventDefault();
          event.stopImmediatePropagation();
        }
      }, true);

      window.addEventListener('visionqc:navigate', (event) => {
        setPage(event.detail?.page || 'classification');
      });
    }
  }

  function installInteractionGuards() {
    // v4.3.8: 전역 pointerdown 캡처를 사용하지 않습니다.
    // 캡처 단계에서 preventDefault/stopImmediatePropagation을 호출하면
    // select, button, file picker 등 다른 화면의 기본 클릭 동작까지 차단될 수 있습니다.
    if (window.__VISIONQC_V437_INTERACTION_GUARDS__) return;
    window.__VISIONQC_V437_INTERACTION_GUARDS__ = true;
  }

  function navItem(page, icon, title, subtitle) {
    return `<button class="vq43-nav-item" data-vq-page="${page}"><span class="vq43-nav-icon">${icon}</span><span><strong>${title}</strong><small>${subtitle}</small></span><span class="vq43-arrow">›</span></button>`;
  }

  function toggleMenu(open) {
    state.menuOpen = Boolean(open);
    const button = $('#vq43-menu-button');
    const backdrop = $('#vq43-menu-backdrop');
    const drawer = $('#vq43-drawer');
    button?.setAttribute('aria-expanded', String(state.menuOpen));
    if (button) button.title = state.menuOpen ? '메뉴 닫기' : '메뉴 열기';
    backdrop?.classList.toggle('open', state.menuOpen);
    drawer?.classList.toggle('open', state.menuOpen);
    drawer?.setAttribute('aria-hidden', String(!state.menuOpen));
    if (drawer) drawer.style.transform = state.menuOpen ? 'translateX(0)' : 'translateX(-102%)';
    if (backdrop) {
      backdrop.style.opacity = state.menuOpen ? '1' : '0';
      backdrop.style.pointerEvents = state.menuOpen ? 'auto' : 'none';
    }
  }

  function setPage(page) {
    if (!['main', 'analysis', 'classification', 'simulation', 'settings'].includes(page)) page = 'classification';
    state.page = page;
    document.body.dataset.vqPage = page;
    safeStorageSet(PAGE_KEY, page);
    toggleMenu(false);

    const shell = $('#vq43-shell');
    const hostMain = $('#root > div > main');
    const extensionVisible = page !== 'classification';

    if (page === 'classification') {
      // 분류 화면은 React 원본 main을 명시적으로 복원합니다.
      // 이전 분석 화면에서 남은 display/visibility/pointer-events 상태에 의존하지 않습니다.
      if (shell) {
        shell.classList.remove('visible');
        shell.style.setProperty('display', 'none', 'important');
        shell.style.setProperty('pointer-events', 'none', 'important');
      }
      if (hostMain) {
        hostMain.style.setProperty('display', 'flex', 'important');
        hostMain.style.setProperty('visibility', 'visible', 'important');
        hostMain.style.setProperty('pointer-events', 'auto', 'important');
        hostMain.setAttribute('aria-hidden', 'false');
      }
    } else {
      if (hostMain) {
        hostMain.style.setProperty('display', 'none', 'important');
        hostMain.style.setProperty('visibility', 'hidden', 'important');
        hostMain.style.setProperty('pointer-events', 'none', 'important');
        hostMain.setAttribute('aria-hidden', 'true');
      }
      if (shell) {
        shell.classList.add('visible');
        shell.style.setProperty('display', 'block', 'important');
        shell.style.setProperty('pointer-events', 'auto', 'important');
        shell.style.zIndex = '1000000';
      }
      renderCurrentPage();
    }

    $$('.vq43-nav-item').forEach((item) => item.classList.toggle('active', item.dataset.vqPage === page));
  }

  function patchReactHeader() {
    const header = $('#root > div > header');
    if (!header) return;
    header.classList.add('vq43-host-header');
    const children = Array.from(header.children);
    if (children[0]) children[0].classList.add('vq43-brand');
    if (children[1]) children[1].classList.add('vq43-stats');
    if (children[2]) children[2].classList.add('vq43-actions');
    // React가 관리하는 브랜드 DOM의 텍스트는 직접 수정하지 않습니다.
    // Chrome 프로필별 저장 상태와 React 재렌더 타이밍 차이로 중복 문구가 생길 수 있어,
    // 브랜드 표시는 CSS 가상 요소로 고정 렌더링합니다.
    const actionArea = children[2];
    if (actionArea) {
      actionArea.classList.add('vq43-actions-grid');
      const hiddenLegacyButtons = [];
      Array.from(actionArea.querySelectorAll('button')).forEach((button) => {
        const label = (button.textContent || '').replace(/\s+/g, ' ').trim();
        if (/^(Load Labels|Save Labels)$/i.test(label)) {
          button.classList.add('vq43-hidden-legacy-label-button');
          hiddenLegacyButtons.push(button);
        }
      });
      const legacyDivider = hiddenLegacyButtons[0]?.previousElementSibling;
      if (legacyDivider && legacyDivider.tagName === 'DIV') legacyDivider.classList.add('vq43-hidden-legacy-label-button');

      let resetButton = $('#vq43-reset-labels');
      if (!resetButton) {
        resetButton = document.createElement('button');
        resetButton.id = 'vq43-reset-labels';
        resetButton.type = 'button';
        resetButton.className = 'vq43-reset-labels';
        resetButton.innerHTML = '<span aria-hidden="true">↺</span><span>Label Reset</span>';
        resetButton.title = '현재 불러온 모든 이미지의 분류 라벨을 초기화합니다.';
        const zipButton = Array.from(actionArea.querySelectorAll('button')).find((button) => /^(ZIP)$/i.test((button.textContent || '').trim()));
        actionArea.insertBefore(resetButton, zipButton || null);
        resetButton.addEventListener('click', (event) => {
          event.preventDefault();
          event.stopPropagation();
          const confirmed = window.confirm('정말 모든 이미지의 라벨을 리셋하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다.');
          if (!confirmed) return;
          window.dispatchEvent(new CustomEvent('visionqc:reset-labels'));
          showToast('모든 이미지 라벨을 초기화했습니다.');
        });
      }

      // 상단 버튼을 2줄 고정 구조로 배치하기 위한 역할 지정
      Array.from(actionArea.querySelectorAll('button, label')).forEach((control) => {
        const label = (control.textContent || '').replace(/\s+/g, ' ').trim();
        control.removeAttribute('data-vq-header-role');
        if (/^Input Cell ID/i.test(label)) control.dataset.vqHeaderRole = 'input';
        else if (/^Load Folder/i.test(label)) control.dataset.vqHeaderRole = 'folder';
        else if (/^Load Files/i.test(label)) control.dataset.vqHeaderRole = 'files';
        else if (/Label Reset/i.test(label)) control.dataset.vqHeaderRole = 'reset';
        else if (/^ZIP$/i.test(label)) control.dataset.vqHeaderRole = 'zip';
        else if (/^Organize Folder/i.test(label)) control.dataset.vqHeaderRole = 'organize';
        else if (/^Export Folder/i.test(label)) control.dataset.vqHeaderRole = 'export';
      });
    }
    document.title = `VisionQC DirectExport v${VERSION}`;
    attachHorizontalWheel(children[1]);
    attachHorizontalWheel(children[2]);
  }

  function attachHorizontalWheel(element) {
    if (!element || element.dataset.vqWheel === '1') return;
    element.dataset.vqWheel = '1';
    element.addEventListener('wheel', (event) => {
      if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
      const scrollTarget = element.classList.contains('vq43-stats') ? element.children[1] : element;
      if (!scrollTarget || scrollTarget.scrollWidth <= scrollTarget.clientWidth) return;
      event.preventDefault();
      scrollTarget.scrollLeft += event.deltaY;
    }, { passive: false });
  }

  function runAction(control) {
    if (!control) return;
    const action = control.dataset.vqAction;
    if (!action) return;
    if (action === 'close-menu') toggleMenu(false);
    else if (action === 'open-settings') setPage('settings');
    else if (action === 'simulation-mode') {
      state.simulationMode = control.dataset.vqMode || 'integrated';
      renderSimulation();
      bindPageControls();
    }
    else if (action === 'simulation-agent-check') checkSimulationAgent();
    else if (action === 'simulation-agent-launch') launchSimulationAgent();
    else if (action === 'simulation-agent-info') showToast('VisionQC Web은 GUI만 담당하고 VPDL Runtime · GPU · Workspace 처리는 사용자 PC의 Local Agent가 수행합니다.');
    else if (action === 'simulation-browse') browseSimulationPath(control);
    else if (action === 'simulation-runtime-check') checkSimulationRuntime();
    else if (action === 'simulation-start') startSimulation();
    else if (action === 'simulation-stop') stopSimulation();
    else if (action === 'choose-result') chooseResultFile(control.closest('[data-vq-position]')?.dataset.vqPosition);
    else if (action === 'remove-result') removeResultInput(control.closest('[data-vq-position]')?.dataset.vqPosition);
    else if (action === 'choose-ng-folder') chooseNgFolder();
    else if (action === 'clear-inputs') clearAnalysisInputs();
    else if (action === 'miss-tab') {
      state.selectedMissPosition = control.closest('[data-vq-position]')?.dataset.vqPosition || state.selectedMissPosition;
      renderDashboard();
      bindPageControls();
    } else if (action === 'open-miss') {
      openMissModal(control.closest('[data-vq-key]')?.dataset.vqKey);
    } else if (action === 'download-misses') downloadMissCsv(state.selectedMissPosition);
    else if (action === 'download-all-results') downloadAllResultsCsv();
    else if (action === 'export-summary-report') exportSummaryReport();
    else if (action === 'download-score-filter') downloadScoreFilterCsv();
    else if (action === 'reset-thresholds') resetThresholds();
    else if (action === 'open-chart-modal') openChartModal();
    else if (action === 'close-chart-modal') closeChartModal();
    else if (action === 'chart-modal-reset') resetChartModalView();
    else if (action === 'close-modal') closeModal();
    else if (action === 'modal-prev') changeModalImage(-1);
    else if (action === 'modal-next') changeModalImage(1);
    else if (action === 'modal-reset') resetModalView();
  }

  function closeAnalysisDropdowns(except = null) {
    $$('.vq43-dropdown.open').forEach((dropdown) => {
      if (dropdown === except) return;
      dropdown.classList.remove('open');
      dropdown.querySelector('.vq43-dropdown-button')?.setAttribute('aria-expanded', 'false');
    });
  }

  function setAnalysisFilter(kind, value) {
    if (kind === 'position') state.analysisPosition = value;
    else if (kind === 'tool') state.analysisTool = value;
    else if (kind === 'scope') state.analysisScope = value;
    else if (kind === 'compare') state.analysisScoreCompare = value;
    else return;
    closeAnalysisDropdowns();
    renderAnalysis();
    bindPageControls();
  }

  function bindAnalysisDropdowns() {
    $$('.vq43-dropdown').forEach((dropdown) => {
      const button = dropdown.querySelector('.vq43-dropdown-button');
      if (!button) return;
      button.onclick = (event) => {
        event.preventDefault();
        event.stopPropagation();
        const nextOpen = !dropdown.classList.contains('open');
        closeAnalysisDropdowns(dropdown);
        dropdown.classList.toggle('open', nextOpen);
        button.setAttribute('aria-expanded', String(nextOpen));
      };
      dropdown.querySelectorAll('.vq43-dropdown-option').forEach((option) => {
        option.onclick = (event) => {
          event.preventDefault();
          event.stopPropagation();
          setAnalysisFilter(dropdown.dataset.vqDropdown, option.dataset.value || '');
        };
      });
    });
  }

  function bindPageControls() {
    const shell = $('#vq43-shell');
    if (!shell) return;

    $$('[data-vq-action]', shell).forEach((control) => {
      control.onclick = (event) => {
        event.preventDefault();
        event.stopPropagation();
        runAction(control);
      };
    });
    bindAnalysisDropdowns();

    const cutoffInput = $('#vq43-score-cutoff', shell);
    if (cutoffInput) {
      cutoffInput.onchange = () => {
        state.analysisScoreCutoff = clampScore(cutoffInput.value, 0.80);
        cutoffInput.value = state.analysisScoreCutoff.toFixed(2);
      };
    }

    $$('.vq43-threshold-input', shell).forEach((input) => {
      let timer = null;
      const commit = (restoreFocus = false) => {
        const position = input.dataset.position;
        const tool = input.dataset.tool;
        if (!position || !tool) return;
        const raw = Number(input.value);
        if (!Number.isFinite(raw)) return;
        const value = clampScore(raw, 0.50);
        setThreshold(position, tool, value);
        rebuildModel();
        if (restoreFocus) {
          requestAnimationFrame(() => {
            const next = $(`.vq43-threshold-input[data-position="${CSS.escape(position)}"][data-tool="${CSS.escape(tool)}"]`);
            if (next) {
              next.focus({ preventScroll: true });
              try { next.select(); } catch (_) { /* number inputs may not expose selection APIs */ }
            }
          });
        }
      };
      input.oninput = () => {
        const raw = Number(input.value);
        input.classList.toggle('invalid', input.value !== '' && (!Number.isFinite(raw) || raw < 0.50 || raw > 1.00));
        clearTimeout(timer);
        if (Number.isFinite(raw) && raw >= 0.50 && raw <= 1.00) timer = setTimeout(() => commit(true), 900);
      };
      input.onchange = () => {
        clearTimeout(timer);
        commit(false);
      };
      input.onkeydown = (event) => {
        event.stopPropagation();
        if (event.key === 'Enter') {
          event.preventDefault();
          clearTimeout(timer);
          commit(false);
        }
      };
      input.onclick = (event) => event.stopPropagation();
    });

    // data-sim-field는 입력 요소뿐 아니라 경로 '선택' 버튼에도 붙어 있습니다.
    // 버튼까지 여기서 onclick을 덮어쓰면 simulation-browse 액션이 사라지므로
    // 실제 편집 가능한 입력 요소에만 값 동기화 핸들러를 연결합니다.
    $$('input[data-sim-field], select[data-sim-field], textarea[data-sim-field]', shell).forEach((input) => {
      const sync = () => syncSimulationField(input);
      input.oninput = sync;
      input.onchange = sync;
      input.onclick = (event) => event.stopPropagation();
      input.onkeydown = (event) => event.stopPropagation();
    });
  }

  function handleDelegatedClick(event) {
    const hostLoadControl = event.target.closest?.('.vq43-actions button, .vq43-actions label');
    if (hostLoadControl) {
      const label = (hostLoadControl.textContent || '').replace(/\s+/g, ' ').trim();
      if (/^(Input Cell ID|Load Folder|Load Files)/i.test(label)) setPage('classification');
    }

    const pageButton = event.target.closest?.('[data-vq-page]');
    if (pageButton) {
      event.preventDefault();
      setPage(pageButton.dataset.vqPage);
      return;
    }
    const actionTarget = event.target.closest?.('[data-vq-action]');
    if (!actionTarget) return;
    event.preventDefault();
    runAction(actionTarget);
  }

  function handleDelegatedChange(event) {
    if (event.target.id === 'vq43-analysis-tool') state.analysisTool = event.target.value;
    else if (event.target.id === 'vq43-analysis-scope') state.analysisScope = event.target.value;
    else if (event.target.id === 'vq43-analysis-position') state.analysisPosition = event.target.value;
    else return;
    renderAnalysis();
  }

  function openDatabase() {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, DB_VERSION);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(STORE_NAME)) db.createObjectStore(STORE_NAME, { keyPath: 'key' });
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error || new Error('IndexedDB open failed.'));
      request.onblocked = () => reject(new Error('IndexedDB가 다른 탭에서 사용 중입니다.'));
    });
  }

  function requestPromise(request) {
    return new Promise((resolve, reject) => {
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error || new Error('IndexedDB request failed.'));
    });
  }

  function transactionDone(transaction) {
    return new Promise((resolve, reject) => {
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error || new Error('IndexedDB transaction failed.'));
      transaction.onabort = () => reject(transaction.error || new Error('IndexedDB transaction aborted.'));
    });
  }

  async function saveHandle(key, handle) {
    const db = await openDatabase();
    try {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      tx.objectStore(STORE_NAME).put({ key, handle, name: handle?.name || '', updatedAt: Date.now() });
      await transactionDone(tx);
    } finally { db.close(); }
  }

  async function deleteHandle(key) {
    const db = await openDatabase();
    try {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      tx.objectStore(STORE_NAME).delete(key);
      await transactionDone(tx);
    } finally { db.close(); }
  }

  async function clearHandles() {
    const db = await openDatabase();
    try {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      tx.objectStore(STORE_NAME).clear();
      await transactionDone(tx);
    } finally { db.close(); }
  }

  async function loadHandles() {
    const db = await openDatabase();
    try {
      const tx = db.transaction(STORE_NAME, 'readonly');
      const values = await requestPromise(tx.objectStore(STORE_NAME).getAll());
      await transactionDone(tx);
      return values || [];
    } finally { db.close(); }
  }

  async function hasPermission(handle) {
    if (!handle?.queryPermission) return true;
    return (await handle.queryPermission({ mode: 'read' })) === 'granted';
  }

  async function restoreInputs() {
    try {
      const handles = await loadHandles();
      for (const position of POSITIONS) {
        const saved = handles.find((item) => item.key === `${RESULT_PREFIX}${position}`);
        if (!saved?.handle) continue;
        if (!(await hasPermission(saved.handle))) {
          state.restoreWarnings.push(`${position}: 저장된 결과 파일 권한이 해제되어 다시 선택해야 합니다.`);
          continue;
        }
        try {
          const file = await saved.handle.getFile();
          state.resultInputs[position] = await parsePositionFile(file, position, saved.handle);
        } catch (error) {
          state.restoreWarnings.push(`${position}: ${error.message || '복원 실패'}`);
        }
      }
      const ngSaved = handles.find((item) => item.key === NG_ROOT_KEY);
      if (ngSaved?.handle) {
        if (await hasPermission(ngSaved.handle)) {
          try {
            const scanned = await scanNgDirectory(ngSaved.handle, false);
            state.ngRootName = scanned.rootName;
            state.ngImages = scanned.images;
            state.ngWarnings = scanned.warnings;
          } catch (error) {
            state.restoreWarnings.push(error.message || '실제 NG 폴더 복원 실패');
          }
        } else {
          state.ngRootName = ngSaved.name || '';
          state.restoreWarnings.push('저장된 실제 NG 폴더 권한이 해제되어 다시 선택해야 합니다.');
        }
      }
    } catch (error) {
      console.error(error);
    } finally {
      state.initialized = true;
      rebuildModel();
      setPage(state.page);
    }
  }

  function parseCsv(text) {
    const rows = [];
    let row = [], cell = '', quoted = false;
    for (let i = 0; i < text.length; i += 1) {
      const char = text[i], next = text[i + 1];
      if (char === '"') {
        if (quoted && next === '"') { cell += '"'; i += 1; }
        else quoted = !quoted;
      } else if (!quoted && char === ',') {
        row.push(cell); cell = '';
      } else if (!quoted && (char === '\n' || char === '\r')) {
        if (char === '\r' && next === '\n') i += 1;
        row.push(cell); cell = '';
        if (row.some((item) => String(item).trim())) rows.push(row);
        row = [];
      } else cell += char;
    }
    row.push(cell);
    if (row.some((item) => String(item).trim())) rows.push(row);
    return rows;
  }

  function columnIndex(letters) {
    let value = 0;
    for (const char of letters.toUpperCase()) value = value * 26 + char.charCodeAt(0) - 64;
    return value - 1;
  }

  async function parseXlsx(file) {
    if (!window.JSZip) throw new Error('XLSX 모듈을 불러오지 못했습니다.');
    const zip = await window.JSZip.loadAsync(await file.arrayBuffer());
    const workbookText = await zip.file('xl/workbook.xml')?.async('text');
    const relsText = await zip.file('xl/_rels/workbook.xml.rels')?.async('text');
    if (!workbookText || !relsText) throw new Error('XLSX workbook 정보를 읽을 수 없습니다.');
    const parser = new DOMParser();
    const workbookDoc = parser.parseFromString(workbookText, 'application/xml');
    const relsDoc = parser.parseFromString(relsText, 'application/xml');
    const firstSheet = workbookDoc.querySelector('sheet');
    const relId = firstSheet?.getAttribute('r:id') || firstSheet?.getAttributeNS('http://schemas.openxmlformats.org/officeDocument/2006/relationships', 'id');
    const relationship = Array.from(relsDoc.querySelectorAll('Relationship')).find((node) => node.getAttribute('Id') === relId);
    const target = relationship?.getAttribute('Target');
    if (!target) throw new Error('XLSX 첫 시트 경로를 찾지 못했습니다.');
    const shared = [];
    const sharedText = await zip.file('xl/sharedStrings.xml')?.async('text');
    if (sharedText) {
      const sharedDoc = parser.parseFromString(sharedText, 'application/xml');
      Array.from(sharedDoc.querySelectorAll('si')).forEach((node) => shared.push(Array.from(node.querySelectorAll('t')).map((part) => part.textContent || '').join('')));
    }
    let sheetPath = target.replace(/^\//, '');
    if (!sheetPath.startsWith('xl/')) sheetPath = `xl/${sheetPath.replace(/^\.\//, '')}`;
    const sheetText = await zip.file(sheetPath)?.async('text');
    if (!sheetText) throw new Error('XLSX 첫 시트를 읽지 못했습니다.');
    const sheetDoc = parser.parseFromString(sheetText, 'application/xml');
    const rows = [];
    Array.from(sheetDoc.querySelectorAll('sheetData > row')).forEach((rowNode) => {
      const output = [];
      Array.from(rowNode.children).filter((node) => node.localName === 'c').forEach((cellNode) => {
        const reference = cellNode.getAttribute('r') || 'A1';
        const letters = reference.match(/[A-Z]+/i)?.[0] || 'A';
        const index = columnIndex(letters);
        const type = cellNode.getAttribute('t') || '';
        const valueNode = Array.from(cellNode.children).find((node) => node.localName === 'v');
        const inlineNode = Array.from(cellNode.querySelectorAll('is t')).map((node) => node.textContent || '').join('');
        const raw = valueNode?.textContent ?? inlineNode;
        let value = raw;
        if (type === 's') value = shared[Number(raw)] ?? '';
        else if (type === 'b') value = raw === '1';
        else if (!['inlineStr', 'str'].includes(type) && raw !== '' && Number.isFinite(Number(raw))) value = Number(raw);
        while (output.length <= index) output.push('');
        output[index] = value;
      });
      if (output.some((item) => String(item ?? '').trim())) rows.push(output);
    });
    return rows;
  }

  async function parseTableFile(file) {
    const ext = file.name.split('.').pop().toLowerCase();
    if (ext === 'xlsx') return parseXlsx(file);
    if (ext === 'csv') {
      const buffer = await file.arrayBuffer();
      let text = new TextDecoder('utf-8').decode(buffer);
      if (text.includes('\uFFFD')) {
        try { text = new TextDecoder('euc-kr').decode(buffer); } catch (_) { /* keep UTF-8 */ }
      }
      return parseCsv(text.replace(/^\uFEFF/, ''));
    }
    throw new Error('CSV 또는 XLSX 파일만 지원합니다.');
  }

  async function parsePositionFile(file, selectedPosition, handle) {
    const table = await parseTableFile(file);
    const headerRow = table.findIndex((row) => row.some((value) => normalizeHeaderKey(value) === 'cellid') && row.some((value) => normalizeHeaderKey(value) === 'total_result'));
    if (headerRow < 0) throw new Error('Cell ID와 Total_result 열을 찾지 못했습니다.');
    const headers = table[headerRow].map(normalizeHeader);
    const keys = headers.map(normalizeHeaderKey);
    const cellIndex = keys.indexOf('cellid');
    const positionIndex = keys.indexOf('position');
    const totalIndex = keys.indexOf('total_result');
    const resultColumns = [];
    const scoreMap = new Map();
    headers.forEach((header, index) => {
      const resultMatch = header.match(/^(.*?)_result$/i);
      if (resultMatch && normalizeHeaderKey(header) !== 'total_result') resultColumns.push({ tool: resultMatch[1].trim(), index });
      const scoreMatch = header.match(/^(.*?)_score$/i);
      if (scoreMatch) scoreMap.set(scoreMatch[1].trim().toLowerCase(), index);
    });
    const warnings = [];
    if (!resultColumns.length) warnings.push('Tool별 *_result 열이 없습니다.');
    let invalidCell = 0, mismatch = 0;
    const rows = [];
    table.slice(headerRow + 1).forEach((row, offset) => {
      if (!row.some((value) => String(value ?? '').trim())) return;
      const cellId = extractCellId(row[cellIndex]);
      if (!cellId) { invalidCell += 1; return; }
      const inFilePosition = positionIndex >= 0 ? normalizePosition(row[positionIndex]) : null;
      if (inFilePosition && inFilePosition !== selectedPosition) mismatch += 1;
      const tools = {};
      resultColumns.forEach((column) => {
        const scoreIndex = scoreMap.get(column.tool.toLowerCase());
        tools[column.tool] = { tool: column.tool, result: normalizeResult(row[column.index]), score: scoreIndex === undefined ? null : parseNumber(row[scoreIndex]) };
      });
      rows.push({ sourceFileName: file.name, sourceRowNumber: headerRow + offset + 2, cellId, position: selectedPosition, totalResult: normalizeResult(row[totalIndex]), tools });
    });
    if (invalidCell) warnings.push(`Cell ID 추출 실패 ${numberText(invalidCell)}행 제외`);
    if (mismatch) warnings.push(`Position 불일치 ${numberText(mismatch)}행을 ${selectedPosition}으로 처리`);
    if (!rows.length) warnings.push('분석 가능한 데이터가 없습니다.');
    return { position: selectedPosition, fileName: file.name, fileSize: file.size, rows, warnings, handle, updatedAt: Date.now() };
  }

  async function chooseResultFile(position) {
    if (state.loading) return;
    try {
      let file, handle;
      if (window.showOpenFilePicker) {
        try {
          [handle] = await window.showOpenFilePicker({ multiple: false, types: [{ description: 'Simulation Result', accept: { 'text/csv': ['.csv'], 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['.xlsx'] } }] });
          file = await handle.getFile();
        } catch (error) {
          if (error.name === 'AbortError') return;
          throw error;
        }
      } else file = await pickFile('.csv,.xlsx');
      state.loading = `result:${position}`;
      renderSettings();
      state.resultInputs[position] = await parsePositionFile(file, position, handle);
      if (handle) await saveHandle(`${RESULT_PREFIX}${position}`, handle);
      rebuildModel();
      showToast(`${position} 결과 파일 ${numberText(state.resultInputs[position].rows.length)}행을 불러왔습니다.`);
    } catch (error) {
      console.error(error);
      showToast(`결과 파일을 읽지 못했습니다: ${error.message || error}`, true);
    } finally {
      state.loading = '';
      renderSettings();
    }
  }

  function pickFile(accept, directory = false) {
    return new Promise((resolve, reject) => {
      const input = document.createElement('input');
      input.type = 'file'; input.accept = accept || ''; input.multiple = directory;
      if (directory) { input.webkitdirectory = true; input.setAttribute('directory', ''); }
      input.onchange = () => directory ? resolve(Array.from(input.files || [])) : resolve(input.files?.[0]);
      input.oncancel = () => reject(new DOMException('Cancelled', 'AbortError'));
      input.click();
    });
  }

  async function removeResultInput(position) {
    delete state.resultInputs[position];
    try { await deleteHandle(`${RESULT_PREFIX}${position}`); } catch (error) { console.error(error); }
    rebuildModel();
    renderSettings();
  }

  async function chooseNgFolder() {
    if (state.loading) return;
    try {
      state.loading = 'ng'; renderSettings();
      if (window.showDirectoryPicker) {
        let handle;
        try { handle = await window.showDirectoryPicker({ mode: 'read' }); }
        catch (error) { if (error.name === 'AbortError') return; throw error; }
        const scanned = await scanNgDirectory(handle, true);
        state.ngRootName = scanned.rootName; state.ngImages = scanned.images; state.ngWarnings = scanned.warnings;
        await saveHandle(NG_ROOT_KEY, handle);
      } else {
        const files = await pickFile('image/*', true);
        const scanned = scanNgFiles(files);
        state.ngRootName = scanned.rootName; state.ngImages = scanned.images; state.ngWarnings = [...scanned.warnings, '새로고침 후 NG 폴더를 다시 선택해야 할 수 있습니다.'];
      }
      rebuildModel();
      showToast(`실제 NG 이미지 ${numberText(state.ngImages.length)}개를 불러왔습니다.`);
    } catch (error) {
      if (error.name !== 'AbortError') { console.error(error); showToast(`NG 폴더를 읽지 못했습니다: ${error.message || error}`, true); }
    } finally { state.loading = ''; renderSettings(); }
  }

  async function scanNgDirectory(rootHandle, requestPermission = true) {
    if (rootHandle.queryPermission) {
      let permission = await rootHandle.queryPermission({ mode: 'read' });
      if (permission !== 'granted' && requestPermission && rootHandle.requestPermission) permission = await rootHandle.requestPermission({ mode: 'read' });
      if (permission !== 'granted') throw new Error('실제 NG 폴더 접근 권한이 없습니다.');
    }
    const images = [], warnings = [];
    let invalidCell = 0, unknownPosition = 0;
    async function walk(directory, parts) {
      for await (const [name, handle] of directory.entries()) {
        const next = [...parts, name];
        if (handle.kind === 'directory') await walk(handle, next);
        else if (handle.kind === 'file' && IMG_RE.test(name)) {
          const position = next.map(normalizePosition).find(Boolean);
          if (!position) { unknownPosition += 1; continue; }
          const cellId = extractCellId(name) || extractCellId(next.join('/'));
          if (!cellId) { invalidCell += 1; continue; }
          const file = await handle.getFile();
          const relativePath = normalizePath(next.join('/'));
          images.push({ key: `${position}|${cellId}|${relativePath.toLowerCase()}`, position, cellId, file, relativePath });
        }
      }
    }
    await walk(rootHandle, [rootHandle.name || '']);
    if (invalidCell) warnings.push(`Cell ID 추출 실패 이미지 ${numberText(invalidCell)}개 제외`);
    if (unknownPosition) warnings.push(`Position 폴더 밖 이미지 ${numberText(unknownPosition)}개 제외`);
    if (!images.length) warnings.push('분석 가능한 실제 NG 이미지가 없습니다.');
    return { rootName: rootHandle.name || 'NG Images', images, warnings };
  }

  function scanNgFiles(files) {
    const images = [], warnings = [];
    let invalidCell = 0, unknownPosition = 0;
    files.forEach((file) => {
      const relativePath = normalizePath(file.webkitRelativePath || file.name);
      const position = relativePath.split('/').map(normalizePosition).find(Boolean);
      if (!position) { unknownPosition += 1; return; }
      const cellId = extractCellId(file.name) || extractCellId(relativePath);
      if (!cellId) { invalidCell += 1; return; }
      images.push({ key: `${position}|${cellId}|${relativePath.toLowerCase()}`, position, cellId, file, relativePath });
    });
    if (invalidCell) warnings.push(`Cell ID 추출 실패 이미지 ${numberText(invalidCell)}개 제외`);
    if (unknownPosition) warnings.push(`Position 폴더 밖 이미지 ${numberText(unknownPosition)}개 제외`);
    const firstPath = files[0]?.webkitRelativePath || '';
    return { rootName: firstPath.split('/')[0] || 'NG Images', images, warnings };
  }

  async function clearAnalysisInputs() {
    if (!window.confirm('Position 결과 파일과 실제 NG 이미지 설정을 모두 초기화할까요?')) return;
    state.resultInputs = {}; state.ngRootName = ''; state.ngImages = []; state.ngWarnings = []; state.restoreWarnings = [];
    try { await clearHandles(); } catch (error) { console.error(error); }
    rebuildModel(); renderSettings();
  }

  function aggregateRows(rows) {
    const groups = new Map();
    rows.forEach((row) => {
      const key = resultKey(row.position, row.cellId);
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(row);
    });
    return Array.from(groups.entries()).map(([key, sourceRows]) => {
      const toolNames = [...new Set(sourceRows.flatMap((row) => Object.keys(row.tools)))];
      const tools = {};
      toolNames.forEach((tool) => {
        const observations = sourceRows.map((row) => row.tools[tool]).filter(Boolean);
        const baseResult = observations.some((item) => item.result === 'NG') ? 'NG' : observations.some((item) => item.result === 'OK') ? 'OK' : 'UNKNOWN';
        const scores = observations.map((item) => item.score).filter(Number.isFinite);
        const ngScores = observations.filter((item) => item.result === 'NG' && Number.isFinite(item.score)).map((item) => item.score);
        const okScores = observations.filter((item) => item.result === 'OK' && Number.isFinite(item.score)).map((item) => item.score);
        const representative = baseResult === 'NG' ? ngScores : baseResult === 'OK' ? okScores : scores;
        tools[tool] = {
          tool,
          baseResult,
          result: baseResult,
          scores,
          ngScores,
          okScores,
          representativeScore: representative.length ? Math.min(...representative) : null,
          threshold: 0.50
        };
      });
      const baseTotalResult = sourceRows.some((row) => row.totalResult === 'NG') ? 'NG' : sourceRows.some((row) => row.totalResult === 'OK') ? 'OK' : 'UNKNOWN';
      return { key, cellId: sourceRows[0].cellId, position: sourceRows[0].position, baseTotalResult, totalResult: baseTotalResult, tools, sourceRows, duplicateCount: Math.max(0, sourceRows.length - 1) };
    });
  }

  function applyThresholdSimulation(records) {
    records.forEach((record) => {
      Object.values(record.tools).forEach((tool) => {
        const threshold = getThreshold(record.position, tool.tool);
        tool.threshold = threshold;
        if (tool.baseResult === 'NG') {
          // CSV의 Score는 선택된 결과에 대한 confidence이므로, 원래 NG인 결과만 Threshold로 거릅니다.
          // 원래 OK였던 결과를 NG로 뒤집지는 않습니다.
          tool.result = tool.ngScores.length ? (tool.ngScores.some((score) => score >= threshold) ? 'NG' : 'OK') : 'NG';
        } else {
          tool.result = tool.baseResult;
        }
      });
      const toolValues = Object.values(record.tools);
      record.totalResult = toolValues.length
        ? (toolValues.some((tool) => tool.result === 'NG') ? 'NG' : 'OK')
        : record.baseTotalResult;
    });
  }

  function rebuildModel() {
    const rows = POSITIONS.flatMap((position) => state.resultInputs[position]?.rows || []);
    const records = aggregateRows(rows);
    applyThresholdSimulation(records);
    const recordMap = new Map(records.map((record) => [record.key, record]));
    const actualMap = new Map();
    state.ngImages.forEach((image) => {
      const key = resultKey(image.position, image.cellId);
      if (!actualMap.has(key)) actualMap.set(key, []);
      actualMap.get(key).push(image);
    });
    const cells = new Map();
    records.forEach((record) => {
      if (!cells.has(record.cellId)) cells.set(record.cellId, []);
      cells.get(record.cellId).push(record);
    });
    const ngCellCount = Array.from(cells.values()).filter((items) => items.some((item) => item.totalResult === 'NG')).length;
    const misses = [], detectedActual = new Set();
    actualMap.forEach((images, key) => {
      const record = recordMap.get(key);
      if (!record) return;
      if (record.totalResult === 'OK') misses.push({ key, cellId: record.cellId, position: record.position, record, images });
      if (record.totalResult === 'NG') detectedActual.add(key);
    });
    misses.sort((a, b) => POSITIONS.indexOf(a.position) - POSITIONS.indexOf(b.position) || a.cellId.localeCompare(b.cellId));
    const tools = [...new Set(records.flatMap((record) => Object.keys(record.tools)))].sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));
    const positionSummaries = POSITIONS.map((position) => {
      const positionRecords = records.filter((record) => record.position === position);
      const actualKeys = Array.from(actualMap.keys()).filter((key) => key.startsWith(`${position}|`));
      const ng = positionRecords.filter((record) => record.totalResult === 'NG').length;
      const ok = positionRecords.filter((record) => record.totalResult === 'OK').length;
      return { position, input: !!state.resultInputs[position], total: positionRecords.length, ng, ok, ngRate: positionRecords.length ? ng / positionRecords.length : 0, actualNg: actualKeys.length, detected: actualKeys.filter((key) => detectedActual.has(key)).length, misses: misses.filter((item) => item.position === position).length, unmatched: actualKeys.filter((key) => !recordMap.has(key)).length };
    });
    const positionToolSummaries = POSITIONS.map((position) => {
      const positionRecords = records.filter((record) => record.position === position);
      const positionNgRecords = positionRecords.filter((record) => record.totalResult === 'NG');
      const positionTools = [...new Set(positionRecords.flatMap((record) => Object.keys(record.tools)))].sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));
      return {
        position,
        input: !!state.resultInputs[position],
        totalNg: positionNgRecords.length,
        tools: positionTools.map((tool) => {
          const ng = positionNgRecords.filter((record) => record.tools[tool]?.result === 'NG').length;
          const actualMatchedRecords = positionRecords.filter((record) => actualMap.has(record.key));
          const rawNgScores = actualMatchedRecords.flatMap((record) => record.sourceRows.map((row) => row.tools[tool]).filter((item) => item?.result === 'NG' && Number.isFinite(item.score)).map((item) => item.score));
          return {
            tool,
            ng,
            denominator: positionNgRecords.length,
            rate: positionNgRecords.length ? ng / positionNgRecords.length : 0,
            minNgScore: rawNgScores.length ? Math.min(...rawNgScores) : null,
            threshold: getThreshold(position, tool)
          };
        })
      };
    });
    const actualKeys = Array.from(actualMap.keys());
    const matchedActualKeys = actualKeys.filter((key) => recordMap.has(key));
    const unmatchedActualKeys = actualKeys.filter((key) => !recordMap.has(key));
    const resultCellIdSamples = [...new Set(records.map((record) => record.cellId))].slice(0, 3);
    const actualCellIdSamples = [...new Set(state.ngImages.map((image) => image.cellId))].slice(0, 3);
    state.model = {
      records,
      recordMap,
      actualMap,
      uniqueCellCount: cells.size,
      ngCellCount,
      ngCellRate: cells.size ? ngCellCount / cells.size : 0,
      misses,
      detectedActual,
      tools,
      positionSummaries,
      positionToolSummaries,
      actualUniqueCount: actualKeys.length,
      matchedActualCount: matchedActualKeys.length,
      unmatchedActualCount: unmatchedActualKeys.length,
      resultCellIdSamples,
      actualCellIdSamples,
      duplicates: records.filter((record) => record.duplicateCount > 0)
    };
    if (misses.length && !misses.some((item) => item.position === state.selectedMissPosition)) state.selectedMissPosition = misses[0].position;
    if (!tools.includes(state.analysisTool)) state.analysisTool = tools[0] || '';
    if (state.page !== 'classification' && state.initialized) renderCurrentPage();
  }

  function renderCurrentPage() {
    if (!state.initialized) {
      $('#vq43-page').innerHTML = '<div class="vq43-empty"><div class="vq43-empty-card"><div class="vq43-empty-symbol">◌</div><h2>저장된 Input 확인 중</h2><p>분석 결과 파일과 실제 NG 폴더 연결 상태를 확인하고 있습니다.</p></div></div>';
      return;
    }
    if (state.page === 'main') renderDashboard();
    else if (state.page === 'analysis') renderAnalysis();
    else if (state.page === 'simulation') renderSimulation();
    else if (state.page === 'settings') renderSettings();
    bindPageControls();
  }

  function emptyPage(title, description) {
    $('#vq43-page').innerHTML = `<div class="vq43-empty"><div class="vq43-empty-card"><div class="vq43-empty-symbol">▦</div><h2>${escapeHtml(title)}</h2><p>${escapeHtml(description)}</p><button class="vq43-btn vq43-btn-blue" style="margin-top:22px" data-vq-action="open-settings">Input 설정 열기</button></div></div>`;
    bindPageControls();
  }

  function rateBar(rate, red = false) {
    return `<div class="vq43-rate${red ? ' red' : ''}"><i style="width:${Math.max(0, Math.min(100, rate * 100))}%"></i></div>`;
  }


  function reportDateText(date = new Date()) {
    const pad = (value) => String(value).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
  }

  function reportFileName(date = new Date()) {
    const pad = (value) => String(value).padStart(2, '0');
    return `VisionQC_Summary_Report_${date.getFullYear()}${pad(date.getMonth()+1)}${pad(date.getDate())}_${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`;
  }

  function buildSummaryReportHtml(model, generatedAt = new Date()) {
    const reportTitle = 'VisionQC 검사 요약 리포트';
    const positionColors = { 'CA(TOP)': '#2563eb', 'AN(TOP)': '#ef3340', 'CA(BOT)': '#0f9f9a', 'AN(BOT)': '#ff8500' };
    const resultFiles = POSITIONS.map((position) => state.resultInputs[position]?.fileName ? `${position}: ${state.resultInputs[position].fileName}` : `${position}: 미입력`);
    const totalPositionRows = model.positionSummaries.reduce((sum, item) => sum + item.total, 0);
    const totalPositionNg = model.positionSummaries.reduce((sum, item) => sum + item.ng, 0);
    const totalActual = model.positionSummaries.reduce((sum, item) => sum + item.actualNg, 0);
    const totalDetected = model.positionSummaries.reduce((sum, item) => sum + item.detected, 0);
    const totalMisses = model.misses.length;
    const totalUnmatched = model.positionSummaries.reduce((sum, item) => sum + item.unmatched, 0);
    const positionNgRate = totalPositionRows ? totalPositionNg / totalPositionRows : 0;
    const maxPositionRate = Math.max(0.01, ...model.positionSummaries.map((item) => item.ngRate));
    const escapeAttr = (value) => escapeHtml(String(value)).replace(/"/g, '&quot;');
    const chunk = (items, size) => Array.from({length: Math.ceil(items.length / size)}, (_, index) => items.slice(index * size, (index + 1) * size));
    const positionRows = model.positionSummaries.map((item) => `
      <tr>
        <td><strong>${escapeHtml(item.position)}</strong></td><td>${numberText(item.total)}</td><td class="ng">${numberText(item.ng)}</td>
        <td class="ng">${rateText(item.ngRate)}</td><td>${numberText(item.actualNg)}</td><td>${numberText(item.actualNg-item.unmatched)}</td>
        <td class="ok">${numberText(item.detected)}</td><td class="miss">${numberText(item.misses)}</td><td>${numberText(item.unmatched)}</td>
      </tr>`).join('');
    const positionBars = model.positionSummaries.map((item) => {
      const color = positionColors[item.position];
      const width = Math.max(0, Math.min(100, item.ngRate / maxPositionRate * 92));
      return `<div class="bar-row"><b>${escapeHtml(item.position)}</b><div class="bar-track"><i style="width:${width}%;background:${color}"></i></div><strong style="color:${color}">${rateText(item.ngRate)}</strong></div>`;
    }).join('');
    const missTotal = Math.max(1, totalMisses);
    let cursor = 0;
    const stops = model.positionSummaries.map((item) => {
      const start = cursor; cursor += item.misses / missTotal * 100;
      return `${positionColors[item.position]} ${start.toFixed(3)}% ${cursor.toFixed(3)}%`;
    }).join(',');
    const missLegend = model.positionSummaries.map((item) => `<div><i style="background:${positionColors[item.position]}"></i><span>${escapeHtml(item.position)}</span><b>${numberText(item.misses)}</b></div>`).join('');
    const toolCards = model.positionToolSummaries.map((position) => {
      const color = positionColors[position.position];
      const rows = position.tools.map((tool) => `<div class="tool-mini-row"><span>${escapeHtml(tool.tool)}</span><i><em style="width:${Math.min(100,tool.rate*100)}%;background:${color}"></em></i><b>${(tool.rate*100).toFixed(1)}%</b></div>`).join('') || '<div class="empty">미입력</div>';
      return `<section class="tool-mini" style="--accent:${color}"><h3>${escapeHtml(position.position)}</h3>${rows}</section>`;
    }).join('');

    const detailBlocks = [];
    POSITIONS.forEach((position) => {
      const misses = model.misses.filter((item) => item.position === position);
      const chunks = chunk(misses, 90);
      (chunks.length ? chunks : [[]]).forEach((items, chunkIndex) => {
        if (!items.length) return;
        const tools = [...new Set(items.flatMap((item) => Object.keys(item.record.tools)))].sort((a,b)=>a.localeCompare(b,undefined,{numeric:true,sensitivity:'base'}));
        const averages = tools.map((tool) => {
          const values = items.map((item)=>item.record.tools[tool]).filter((obs)=>obs?.result==='OK' && Number.isFinite(obs.representativeScore)).map((obs)=>obs.representativeScore);
          return { tool, value: values.length ? mean(values) : null, count: values.length };
        }).filter((item)=>Number.isFinite(item.value));
        const list = items.map((item,index)=>`<li><span>${String(chunkIndex*90+index+1).padStart(2,'0')}</span><b>${escapeHtml(item.cellId)}</b></li>`).join('');
        const scoreBars = averages.map((item)=>`<div class="score-ref-row"><span>${escapeHtml(item.tool)}</span><i><em style="width:${Math.max(0,Math.min(100,item.value*100))}%;background:${positionColors[position]}"></em></i><b>${item.value.toFixed(4)}</b></div>`).join('') || '<div class="empty">유효한 OK Score 없음</div>';
        detailBlocks.push({position, color:positionColors[position], chunkIndex, chunkCount:chunks.length, count:misses.length, items, html:`
          <section class="miss-detail" style="--accent:${positionColors[position]}">
            <header><div><strong>${escapeHtml(position)}</strong><span>미검 ${numberText(misses.length)}건${chunks.length>1?` · ${chunkIndex+1}/${chunks.length}`:''}</span></div><small>결과 CSV + NG Image 매칭 · Threshold 적용 후 OK</small></header>
            <div class="miss-detail-body"><ol class="id-list">${list}</ol><aside><h4>미검 시 Tool별 평균 OK Score</h4>${scoreBars}<p>이 페이지에 표시된 미검 ${numberText(items.length)}건 기준</p></aside></div>
          </section>`});
      });
    });
    const detailPages = chunk(detailBlocks, 2);
    const totalPages = 1 + detailPages.length;
    const pageFooter = (pageNo) => `<footer class="page-foot"><strong>VisionQC</strong><span>현재 적용된 Threshold 기준 재계산 결과</span><b>${pageNo} / ${totalPages}</b></footer>`;
    const detailSummaryBars = model.positionSummaries.map((item)=>`<div class="simple-bar"><b>${escapeHtml(item.position)}</b><i><em style="width:${totalMisses?item.misses/Math.max(...model.positionSummaries.map(x=>x.misses),1)*100:0}%;background:${positionColors[item.position]}"></em></i><strong>${numberText(item.misses)}</strong></div>`).join('');
    const emptyDetailBlock = `<section class="miss-detail placeholder"><header><div><strong>미검 상세</strong><span>표시 데이터 없음</span></div><small>동일 디자인 유지 영역</small></header><div class="placeholder-body">이 페이지에 추가로 표시할 미검 Cell ID가 없습니다.</div></section>`;
    const renderDetailPage = (blocks, pageIndex) => {
      const normalizedBlocks = [...blocks];
      while (normalizedBlocks.length < 2) normalizedBlocks.push({ html: emptyDetailBlock });
      return `<section class="report-page detail-page">
        <header class="page-head"><div><h1>${reportTitle}</h1><p>미검 상세 리포트 ${pageIndex+1}/${detailPages.length}</p></div><div class="meta"><strong>생성 시각</strong> ${reportDateText(generatedAt)}<br><strong>NG 이미지 경로</strong> ${escapeHtml(state.ngRootName || '미입력')}</div></header>
        <div class="detail-charts"><div><h3>Position별 미검 수 비교</h3>${detailSummaryBars}</div><div><h3>미검 구성 비율</h3><div class="small-donut" style="background:conic-gradient(${stops||'#e2e8f0 0 100%'})"><span><b>${numberText(totalMisses)}</b><small>총 미검</small></span></div></div></div>
        <div class="detail-guide">아래 목록은 결과 CSV와 NG Image 폴더에 동일 Cell ID + Position이 존재하고, 현재 Threshold 적용 후 OK로 판정된 전체 미검 Cell입니다.</div>
        <div class="detail-blocks">${normalizedBlocks.map((block)=>block.html).join('')}</div>${pageFooter(pageIndex+2)}</section>`;
    };
    const detailPageHtml = detailPages.map(renderDetailPage).join('');

    return `<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>${reportFileName(generatedAt)}</title>
      <style>
        @page{size:A4 portrait;margin:0}*{box-sizing:border-box}html,body{margin:0;padding:0;background:#e8edf5;color:#10214a;font-family:Arial,"Malgun Gothic","Noto Sans KR",sans-serif;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        .actions{position:sticky;top:0;z-index:20;display:flex;justify-content:flex-end;gap:8px;padding:10px 14px;background:#081226}.actions button{border:1px solid #334155;border-radius:9px;padding:9px 14px;background:#172033;color:#fff;font-weight:700;cursor:pointer}.actions .primary{background:#2563eb;border-color:#2563eb}
        .report-page{position:relative;width:210mm;min-height:297mm;margin:10px auto;background:#fff;padding:9mm 9mm 12mm;box-shadow:0 8px 30px rgba(15,23,42,.2);break-after:page;page-break-after:always;overflow:hidden}.report-page:last-child{break-after:auto;page-break-after:auto}
        .page-head{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #173b8f;padding-bottom:5mm}.page-head h1{font-size:26px;margin:0;color:#0c2d73;letter-spacing:-.8px}.page-head p{font-size:12px;margin:4px 0 0;color:#2e4d89;font-weight:700}.meta{font-size:8.5px;line-height:1.55;color:#475569;background:#f8fbff;border:1px solid #b9c9e8;border-radius:8px;padding:6px 9px;max-width:84mm}.meta strong{color:#10214a}
        .section-title{font-size:14px;margin:6mm 0 2.5mm;color:#10214a;display:flex;align-items:center;gap:7px}.section-title:before{content:"";width:5px;height:18px;border-radius:3px;background:#173b8f}.kpis{display:grid;grid-template-columns:repeat(4,1fr);gap:3mm;margin-top:5mm}.kpi{border:1px solid #c8d5ec;border-radius:10px;padding:4mm;background:linear-gradient(180deg,#fff,#f8fbff)}.kpi span{display:block;font-size:8px;color:#64748b;font-weight:700}.kpi strong{display:block;font-size:23px;margin-top:2mm;color:#123d97}.kpi.ng strong{color:#e31d36}.kpi.miss strong{color:#f47b10}
        table{width:100%;border-collapse:collapse;font-size:8px}th{background:#123d97;color:#fff;padding:2.2mm 1.2mm;border:1px solid #8ba4d5;text-align:center}td{padding:2.2mm 1.2mm;border:1px solid #c8d5ec;text-align:center}td:first-child{text-align:left}.ng{color:#e31d36;font-weight:700}.ok{color:#059669;font-weight:700}.miss{color:#f47b10;font-weight:700}
        .chart-grid{display:grid;grid-template-columns:1fr 1fr;gap:4mm;margin-top:4mm}.chart-card{border:1px solid #c8d5ec;border-radius:10px;padding:4mm;min-height:52mm}.chart-card h3{font-size:12px;margin:0 0 4mm}.bar-row{display:grid;grid-template-columns:20mm 1fr 18mm;gap:3mm;align-items:center;margin:3.2mm 0;font-size:8.5px}.bar-track{height:6mm;background:#eef3fa;border-radius:2px;overflow:hidden}.bar-track i{display:block;height:100%}.donut-wrap{display:grid;grid-template-columns:1fr 1fr;align-items:center}.donut{width:42mm;height:42mm;border-radius:50%;margin:auto;display:grid;place-items:center}.donut>span{width:25mm;height:25mm;background:#fff;border-radius:50%;display:grid;place-items:center;text-align:center}.donut b{font-size:17px}.donut small{font-size:7px}.legend div{display:grid;grid-template-columns:4mm 1fr 10mm;gap:2mm;align-items:center;font-size:8px;margin:2.5mm 0}.legend i{width:3mm;height:3mm;border-radius:50%}
        .tool-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:3mm}.tool-mini{border:1px solid var(--accent);border-radius:9px;padding:3mm}.tool-mini h3{margin:0 0 2mm;color:var(--accent);font-size:11px;text-align:center}.tool-mini-row{display:grid;grid-template-columns:17mm 1fr 10mm;gap:1.5mm;align-items:center;font-size:7px;margin:2mm 0}.tool-mini-row i,.score-ref-row i,.simple-bar i{height:3mm;background:#e8edf5;border-radius:2px;overflow:hidden}.tool-mini-row em,.score-ref-row em,.simple-bar em{display:block;height:100%}
        .note{margin-top:4mm;border:1px solid #9db5e5;background:#f5f8ff;border-radius:8px;padding:3mm;font-size:8px;line-height:1.45}.page-foot{position:absolute;left:9mm;right:9mm;bottom:6mm;display:grid;grid-template-columns:1fr 2fr auto;align-items:center;border-top:1px solid #173b8f;padding-top:2mm;font-size:8px;color:#536789}.page-foot strong{font-size:12px;color:#173b8f}.page-foot b{font-size:11px;color:#10214a}
        .detail-charts{display:grid;grid-template-columns:1.45fr .8fr;gap:4mm;margin-top:5mm;border:1px solid #c8d5ec;border-radius:10px;padding:4mm}.detail-charts h3{font-size:11px;margin:0 0 3mm}.simple-bar{display:grid;grid-template-columns:20mm 1fr 10mm;gap:2mm;align-items:center;margin:2mm 0;font-size:7.5px}.small-donut{width:35mm;height:35mm;border-radius:50%;display:grid;place-items:center;margin:auto}.small-donut span{width:21mm;height:21mm;background:#fff;border-radius:50%;display:grid;place-items:center;text-align:center}.small-donut b{font-size:15px}.small-donut small{font-size:6.5px}
        .detail-guide{margin-top:3mm;border:1px solid #9db5e5;background:#f5f8ff;border-radius:8px;padding:2.5mm 3mm;font-size:7.5px;line-height:1.45;color:#314a79}.detail-blocks{display:grid;grid-template-rows:1fr 1fr;gap:4mm;margin-top:3mm;height:202mm}.miss-detail{border:1px solid var(--accent);border-radius:10px;overflow:hidden;min-height:0}.miss-detail>header{display:flex;justify-content:space-between;align-items:center;padding:2.5mm 3mm;border-bottom:1px solid var(--accent);background:#f8fbff}.miss-detail>header div{display:flex;gap:4mm;align-items:center}.miss-detail>header strong{font-size:13px;color:var(--accent)}.miss-detail>header span{font-size:10px;font-weight:700;color:var(--accent)}.miss-detail>header small{font-size:7px;color:#64748b}.miss-detail-body{display:grid;grid-template-columns:1fr 47mm;gap:3mm;padding:3mm;height:calc(100% - 15mm)}.id-list{columns:3;column-gap:4mm;margin:0;padding:0;list-style:none;font-size:6.8px;line-height:1.35}.id-list li{break-inside:avoid;display:flex;gap:1.5mm}.id-list li span{color:var(--accent);font-weight:700;min-width:5mm}.id-list li b{font-weight:600;color:#10214a}.miss-detail aside{border:1px solid #c8d5ec;border-radius:8px;padding:3mm;background:#fbfdff}.miss-detail aside h4{font-size:9px;margin:0 0 3mm;color:var(--accent)}.score-ref-row{display:grid;grid-template-columns:17mm 1fr 12mm;gap:2mm;align-items:center;font-size:7px;margin:3mm 0}.miss-detail aside p{font-size:6.5px;color:#64748b;margin-top:4mm}.miss-detail.placeholder{border-color:#c8d5ec}.miss-detail.placeholder>header strong,.miss-detail.placeholder>header span{color:#64748b}.placeholder-body{height:calc(100% - 15mm);display:grid;place-items:center;color:#94a3b8;font-size:8px;background:#fbfdff}.empty{font-size:7px;color:#94a3b8}
        @media print{body{background:#fff}.actions{display:none}.report-page{margin:0;box-shadow:none}}
      </style></head><body><div class="actions"><button onclick="window.close()">닫기</button><button class="primary" onclick="window.print()">PDF로 저장</button></div>
      <section class="report-page summary-page"><header class="page-head"><div><h1>${reportTitle}</h1><p>Threshold 적용 시뮬레이션 결과</p></div><div class="meta"><strong>생성 시각</strong> ${reportDateText(generatedAt)}<br><strong>NG 이미지 폴더</strong> ${escapeHtml(state.ngRootName || '미입력')}<br><strong>결과 파일</strong><br>${resultFiles.map(escapeHtml).join('<br>')}</div></header>
      <div class="kpis"><div class="kpi"><span>고유 검사 CELL</span><strong>${numberText(model.uniqueCellCount)}</strong></div><div class="kpi ng"><span>NG CELL</span><strong>${numberText(model.ngCellCount)}</strong></div><div class="kpi"><span>CELL NG율</span><strong>${rateText(model.ngCellRate)}</strong></div><div class="kpi miss"><span>미검 CELL-POSITION</span><strong>${numberText(totalMisses)}</strong></div></div>
      <h2 class="section-title">Position별 결과</h2><table><thead><tr><th>Position</th><th>검사</th><th>NG</th><th>NG율</th><th>실제 NG</th><th>CSV 매칭</th><th>정상 검출</th><th>미검</th><th>미매칭</th></tr></thead><tbody>${positionRows}<tr><td><strong>합계</strong></td><td>${numberText(totalPositionRows)}</td><td class="ng">${numberText(totalPositionNg)}</td><td class="ng">${rateText(positionNgRate)}</td><td>${numberText(totalActual)}</td><td>${numberText(totalActual-totalUnmatched)}</td><td class="ok">${numberText(totalDetected)}</td><td class="miss">${numberText(totalMisses)}</td><td>${numberText(totalUnmatched)}</td></tr></tbody></table>
      <div class="chart-grid"><section class="chart-card"><h3>Position별 NG율 비교</h3>${positionBars}</section><section class="chart-card"><h3>미검 구성 비율</h3><div class="donut-wrap"><div class="donut" style="background:conic-gradient(${stops||'#e2e8f0 0 100%'})"><span><b>${numberText(totalMisses)}</b><small>총 미검</small></span></div><div class="legend">${missLegend}</div></div></section></div>
      <h2 class="section-title">Position별 Tool NG 구성 요약</h2><div class="tool-grid">${toolCards}</div><div class="note"><strong>계산 기준:</strong> Cell NG율 = NG 고유 Cell / 전체 고유 Cell. Position NG율 = Position NG / Position 검사 건수. Tool 비율 = 해당 Tool NG / 해당 Position 최종 NG. 미검 = 결과 CSV와 NG Image에 동일 Cell ID + Position이 존재하고 Threshold 적용 후 최종 OK인 경우입니다. 동일 Cell에서 여러 Tool이 NG일 수 있어 Tool 비율 합계는 100%를 초과할 수 있습니다.</div>${pageFooter(1)}</section>${detailPageHtml}</body></html>`;
  }

  function exportSummaryReport() {
    const model = state.model;
    if (!model || !model.records.length) {
      showToast('리포트로 내보낼 분석 결과가 없습니다.', true);
      return;
    }
    const reportWindow = window.open('', '_blank', 'width=1280,height=900');
    if (!reportWindow) {
      showToast('리포트 창이 차단되었습니다. 브라우저 팝업 허용을 확인하십시오.', true);
      return;
    }
    const now = new Date();
    reportWindow.opener = null;
    reportWindow.document.open();
    reportWindow.document.write(buildSummaryReportHtml(model, now));
    reportWindow.document.close();
    showToast('요약 리포트를 열었습니다. PDF로 저장 버튼을 누르십시오.');
  }

  function renderDashboard() {
    const model = state.model;
    if (!model?.records.length) return emptyPage('분석 Input이 없습니다', '설정 메뉴에서 Position별 시뮬레이션 결과 파일과 실제 NG 이미지 폴더를 입력하세요. 결과 파일은 1개 Position만 입력해도 분석할 수 있습니다.');
    const inputCount = POSITIONS.filter((position) => state.resultInputs[position]).length;
    const misses = model.misses.filter((item) => item.position === state.selectedMissPosition);
    $('#vq43-page').innerHTML = `
      <div class="vq43-content">
        <div class="vq43-topline"><div><div class="vq43-eyebrow">Main Dashboard</div><h1 class="vq43-title">시뮬레이션 결과 전체 View</h1><p class="vq43-subtitle">Tool별 Threshold를 적용해 Position·Cell NG율과 실제 NG 기준 미검을 다시 계산합니다.</p></div><div class="vq43-top-actions"><button class="vq43-btn vq43-btn-blue" data-vq-action="export-summary-report">요약 PDF 리포트</button><button class="vq43-btn vq43-btn-green" data-vq-action="download-all-results">전체 결과 CSV 저장</button><button class="vq43-btn" data-vq-action="open-settings">Input 변경</button></div></div>
        <section class="vq43-section"><div class="vq43-section-title"><span class="vq43-step">1</span><div><h3>Cell별 NG</h3><p>입력 Position 중 한 곳이라도 Threshold 적용 후 NG인 Cell</p></div></div><div class="vq43-kpi-grid">
          ${kpi('검사 Cell', numberText(model.uniqueCellCount), `Position ${inputCount}개 입력`)}
          ${kpi('NG Cell', numberText(model.ngCellCount), '한 Position 이상 NG', 'red')}
          ${kpi('Cell NG율', rateText(model.ngCellRate), rateBar(model.ngCellRate, true), 'blue', true)}
          ${kpi('미검 Cell·Position', numberText(model.misses.length), '실제 NG + 시뮬레이션 OK', 'amber')}
        </div></section>
        <section class="vq43-section"><div class="vq43-section-title"><span class="vq43-step">2</span><div><h3>Position별 NG</h3><p>Threshold 적용 결과이며 입력하지 않은 Position은 미입력으로 구분</p></div></div><div class="vq43-position-grid">${model.positionSummaries.map(positionCard).join('')}</div></section>
        <section class="vq43-section"><div class="vq43-section-title vq43-threshold-section-title"><span class="vq43-step">3</span><div><h3>Position별 Tool NG 구성</h3><p>원래 NG 결과 중 Score가 Threshold 이상인 Tool만 NG로 유지하여 재계산</p></div><button class="vq43-btn vq43-btn-amber" data-vq-action="reset-thresholds">Threshold 0.50 초기화</button></div>${positionToolCharts(model.positionToolSummaries)}<div class="vq43-note" style="margin-top:12px">Threshold는 CSV에서 원래 NG로 판정된 결과만 필터링합니다. 원래 OK 결과를 NG로 전환하지 않습니다. 동일 Cell에서 여러 Tool이 동시에 NG일 수 있어 Tool 비율 합계는 100%를 초과할 수 있습니다.</div></section>
        ${matchDiagnostic(model)}
        <section class="vq43-section" style="padding-bottom:28px"><div class="vq43-section-title"><span class="vq43-step amber">!</span><div><h3>Position별 미검 Cell ID</h3><p>실제 NG 이미지가 존재하지만 Threshold 적용 시뮬레이션 결과가 OK인 Cell</p></div></div>
          <div class="vq43-miss-toolbar"><div class="vq43-tabs">${POSITIONS.map((position) => `<button class="vq43-tab ${state.selectedMissPosition === position ? 'active' : ''}" data-vq-action="miss-tab" data-vq-position="${position}">${position}<b>${model.misses.filter((item) => item.position === position).length}</b></button>`).join('')}</div><button class="vq43-btn vq43-btn-green" data-vq-action="download-misses" ${misses.length ? '' : 'disabled'}>선택 Position 미검 CSV</button></div>
          <div class="vq43-miss-table"><div class="vq43-miss-row head"><span>Cell ID</span><span>시뮬레이션</span><span>Tool별 Score</span><span>실제 이미지</span></div>${misses.length ? misses.map(missRow).join('') : `<div class="vq43-no-data">${state.selectedMissPosition} 미검 없음</div>`}</div>
        </section>
        ${model.duplicates.length ? `<div class="vq43-note">⚠ 중복 Cell ID + Position ${numberText(model.duplicates.length)}건은 하나라도 NG이면 NG로 통합했습니다.</div>` : ''}
      </div>`;
  }

  function kpi(label, value, detail, tone = '', rawDetail = false) {
    return `<div class="vq43-kpi ${tone}"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong>${rawDetail ? detail : `<small>${escapeHtml(detail)}</small>`}</div>`;
  }

  function positionCard(summary) {
    if (!summary.input) return `<div class="vq43-position-card disabled"><div class="vq43-card-head"><strong>${summary.position}</strong><span class="vq43-pill">미입력</span></div><div class="vq43-no-data">설정에서 결과 파일을 선택하세요.</div></div>`;
    const matched = Math.max(0, summary.actualNg - summary.unmatched);
    return `<div class="vq43-position-card"><div class="vq43-card-head"><strong>${summary.position}</strong><span class="vq43-pill ready">입력됨</span></div><div class="vq43-flex-between"><div><div class="vq43-rate-label">NG Rate</div><div class="vq43-rate-value">${rateText(summary.ngRate)}</div></div><div class="vq43-count"><b>${numberText(summary.ng)}</b> / ${numberText(summary.total)}</div></div>${rateBar(summary.ngRate, true)}<div class="vq43-mini-grid four"><div><span>실제 NG</span><b>${numberText(summary.actualNg)}</b></div><div><span>CSV 매칭</span><b class="blue">${numberText(matched)}</b></div><div><span>정상 검출</span><b class="green">${numberText(summary.detected)}</b></div><div><span>미검</span><b class="amber">${numberText(summary.misses)}</b></div></div>${summary.unmatched ? `<div class="vq43-unmatched warn">CSV Cell ID와 미매칭 실제 NG ${numberText(summary.unmatched)}건</div>` : ''}</div>`;
  }

  function positionToolCharts(summaries) {
    const available = summaries.filter((summary) => summary.input);
    if (!available.length) return '<div class="vq43-note" style="margin-top:15px">Tool별 *_result 열을 찾지 못했습니다.</div>';
    return `<div class="vq43-tool-position-grid">${available.map((summary) => {
      const maxRate = summary.tools.length ? Math.max(...summary.tools.map((tool) => tool.rate)) : -1;
      const body = summary.tools.length
        ? summary.tools.map((tool) => toolDonut(summary.position, tool, Math.abs(tool.rate - maxRate) < 1e-12)).join('')
        : '<div class="vq43-no-data">Tool별 *_result 열이 없습니다.</div>';
      const columns = Math.max(1, Math.min(5, summary.tools.length));
      return `<div class="vq43-tool-position-card"><div class="vq43-card-head"><strong>${summary.position}</strong><span class="vq43-pill ready">Position NG ${numberText(summary.totalNg)}</span></div><div class="vq43-tool-donut-grid tool-count-${columns}">${body}</div></div>`;
    }).join('')}</div>`;
  }

  function toolDonut(position, tool, isMax = false) {
    const percent = Math.max(0, Math.min(100, tool.rate * 100));
    return `<div class="vq43-tool-donut-item ${isMax ? 'is-max' : ''}">${isMax ? '<span class="vq43-max-badge">최고</span>' : ''}<div class="vq43-donut" style="--vq-rate:${percent.toFixed(2)}"><div><strong>${percent.toFixed(1)}%</strong><span>${numberText(tool.ng)} / ${numberText(tool.denominator)}</span></div></div><b>${escapeHtml(tool.tool)}</b><div class="vq43-tool-score-meta"><span>실제 NG 최소 <strong>${scoreText(tool.minNgScore)}</strong></span><label>Threshold<input class="vq43-threshold-input" type="number" inputmode="decimal" min="0.50" max="1.00" step="0.01" value="${tool.threshold.toFixed(2)}" data-position="${escapeHtml(position)}" data-tool="${escapeHtml(tool.tool)}" aria-label="${escapeHtml(position)} ${escapeHtml(tool.tool)} Threshold"></label></div></div>`;
  }

  function matchDiagnostic(model) {
    if (!model.actualUniqueCount) return '';
    const zeroMatch = model.matchedActualCount === 0;
    const resultSample = model.resultCellIdSamples.join(', ') || '-';
    const actualSample = model.actualCellIdSamples.join(', ') || '-';
    return `<div class="vq43-match-status ${zeroMatch ? 'error' : 'ok'}"><div><strong>${zeroMatch ? 'Cell ID 매칭 0건' : `실제 NG 매칭 ${numberText(model.matchedActualCount)}건`}</strong><span>실제 NG 고유 Cell·Position ${numberText(model.actualUniqueCount)}건 중 미매칭 ${numberText(model.unmatchedActualCount)}건</span></div>${zeroMatch ? `<p>결과 CSV와 이미지 폴더의 Cell ID가 서로 다릅니다. 랜덤으로 만든 테스트 CSV는 실제 이미지 파일과 자동으로 매칭되지 않습니다.</p><code>CSV 예: ${escapeHtml(resultSample)}</code><code>이미지 예: ${escapeHtml(actualSample)}</code>` : ''}</div>`;
  }

  function missRow(miss) {
    const scores = Object.values(miss.record.tools).map((tool) => `<span class="vq43-score-chip">${escapeHtml(tool.tool)} <b>${scoreText(tool.representativeScore)}</b></span>`).join('');
    return `<div class="vq43-miss-row"><button class="vq43-cell-link" data-vq-action="open-miss" data-vq-key="${escapeHtml(miss.key)}">${escapeHtml(miss.cellId)}</button><span class="vq43-result-ok">OK</span><span class="vq43-score-list">${scores || '-'}</span><button class="vq43-btn vq43-btn-blue vq43-image-view-btn" data-vq-action="open-miss" data-vq-key="${escapeHtml(miss.key)}">이미지 보기</button></div>`;
  }

  function csvCell(value) {
    const text = String(value ?? '');
    return /[\",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
  }

  async function saveCsvFile(fileName, lines, successMessage) {
    const content = `\uFEFF${lines.join('\r\n')}`;
    const blob = new Blob([content], { type: 'text/csv;charset=utf-8' });
    if (window.showSaveFilePicker && window.isSecureContext) {
      try {
        const handle = await window.showSaveFilePicker({
          suggestedName: fileName,
          types: [{ description: 'CSV 파일', accept: { 'text/csv': ['.csv'] } }]
        });
        const writable = await handle.createWritable();
        await writable.write(blob);
        await writable.close();
        showToast(successMessage || `${fileName} 저장 완료`);
        return true;
      } catch (error) {
        if (error?.name === 'AbortError') return false;
        console.warn('showSaveFilePicker failed; falling back to browser download.', error);
      }
    }
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.style.display = 'none';
    document.body.appendChild(link);
    link.dispatchEvent(new MouseEvent('click', { view: window, bubbles: false, cancelable: true }));
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 3000);
    showToast(successMessage || `${fileName} 다운로드를 시작했습니다.`);
    return true;
  }

  function downloadMissCsv(position) {
    const misses = (state.model?.misses || []).filter((item) => item.position === position);
    if (!misses.length) return showToast(`${position} 미검 데이터가 없습니다.`, true);
    const tools = [...new Set(misses.flatMap((miss) => Object.keys(miss.record.tools)))].sort();
    const headers = ['Cell ID', 'Position', 'Simulated_Total_result'];
    tools.forEach((tool) => headers.push(`${tool}_result`, `${tool}_score`, `${tool}_threshold`));
    headers.push('Actual_Image_Count', 'Actual_Image_Path');
    const lines = [headers.map(csvCell).join(',')];
    misses.forEach((miss) => {
      const row = [miss.cellId, miss.position, miss.record.totalResult];
      tools.forEach((tool) => {
        const observation = miss.record.tools[tool];
        row.push(observation?.result || '', Number.isFinite(observation?.representativeScore) ? observation.representativeScore.toFixed(4) : '', observation ? observation.threshold.toFixed(2) : '');
      });
      row.push(miss.images.length, miss.images.map((image) => image.relativePath).join(' | '));
      lines.push(row.map(csvCell).join(','));
    });
    saveCsvFile(`VisionQC_${position.replace(/[^A-Z]/g, '_')}_미검_${misses.length}건.csv`, lines, `${position} 미검 ${numberText(misses.length)}건 CSV 저장 완료`);
  }

  function downloadAllResultsCsv() {
    const model = state.model;
    if (!model?.records.length) return showToast('저장할 결과 데이터가 없습니다.', true);
    const tools = model.tools;
    const headers = ['Cell ID', 'Position', 'Total_result'];
    tools.forEach((tool) => headers.push(`${tool}_result`, `${tool}_score`));
    const lines = [headers.map(csvCell).join(',')];
    const sorted = [...model.records].sort((a, b) => POSITIONS.indexOf(a.position) - POSITIONS.indexOf(b.position) || a.cellId.localeCompare(b.cellId));
    sorted.forEach((record) => {
      const row = [record.cellId, record.position, record.baseTotalResult];
      tools.forEach((toolName) => {
        const tool = record.tools[toolName];
        row.push(tool?.baseResult || '', Number.isFinite(tool?.representativeScore) ? tool.representativeScore.toFixed(4) : '');
      });
      lines.push(row.map(csvCell).join(','));
    });
    saveCsvFile(`VisionQC_전체_Position_결과_${sorted.length}건.csv`, lines, `전체 Position 결과 ${numberText(sorted.length)}건 CSV 저장 완료`);
  }

  function resetThresholds() {
    if (!window.confirm('모든 Position과 Tool의 Threshold를 0.50으로 초기화할까요?')) return;
    state.thresholds = {};
    persistThresholds();
    rebuildModel();
    showToast('모든 Tool Threshold를 0.50으로 초기화했습니다.');
  }

  function scorePoints(tool, scope, position) {
    const model = state.model;
    const points = [];
    model.records.forEach((record) => {
      if (position !== 'ALL' && record.position !== position) return;
      const hasActualImage = model.actualMap.has(record.key);
      if ((scope === 'ACTUAL_NG_TOOL_NG' || scope === 'ACTUAL_NG_TOOL_OK') && !hasActualImage) return;
      record.sourceRows.forEach((row, index) => {
        const observation = row.tools[tool];
        if (!observation || !Number.isFinite(observation.score)) return;
        if (scope === 'TOOL_OK' && observation.result !== 'OK') return;
        if (scope === 'TOOL_NG' && observation.result !== 'NG') return;
        if (scope === 'ACTUAL_NG_TOOL_NG' && observation.result !== 'NG') return;
        if (scope === 'ACTUAL_NG_TOOL_OK' && observation.result !== 'OK') return;
        const key = `${record.key}|${tool}|${index}`;
        points.push({ key, recordKey: record.key, cellId: record.cellId, position: record.position, result: observation.result, score: observation.score, hasActualImage, sourceFileName: row.sourceFileName || '', sourceRowNumber: row.sourceRowNumber || '' });
      });
    });
    return points;
  }

  function downloadScoreFilterCsv() {
    const cutoffInput = $('#vq43-score-cutoff');
    state.analysisScoreCutoff = clampScore(cutoffInput?.value ?? state.analysisScoreCutoff, 0.80);
    const points = scorePoints(state.analysisTool, state.analysisScope, state.analysisPosition);
    const filtered = points.filter((point) => state.analysisScoreCompare === 'LTE' ? point.score <= state.analysisScoreCutoff : point.score >= state.analysisScoreCutoff);
    if (!filtered.length) return showToast('조건에 맞는 Score 데이터가 없습니다.', true);
    const lines = [['Cell ID', 'Position', 'Tool', 'Result', 'Score', 'Condition', 'Source_File', 'Source_Row'].map(csvCell).join(',')];
    const condition = `Score ${state.analysisScoreCompare === 'LTE' ? '<=' : '>='} ${state.analysisScoreCutoff.toFixed(2)}`;
    filtered.forEach((point) => lines.push([point.cellId, point.position, state.analysisTool, point.result, point.score.toFixed(4), condition, point.sourceFileName, point.sourceRowNumber].map(csvCell).join(',')));
    const scopeLabel = state.analysisScope === 'TOOL_OK' ? 'TOOL_OK' : state.analysisScope === 'TOOL_NG' ? 'TOOL_NG' : state.analysisScope === 'ACTUAL_NG_TOOL_NG' ? 'ACTUAL_NG_DETECTED' : 'ACTUAL_NG_MISSED';
    saveCsvFile(`VisionQC_${state.analysisTool}_${scopeLabel}_${state.analysisScoreCompare}_${state.analysisScoreCutoff.toFixed(2)}_${filtered.length}건.csv`, lines, `Score 조건 ${numberText(filtered.length)}건 CSV 저장 완료`);
  }

  function customDropdown(kind, label, value, options) {
    const selected = options.find((option) => option.value === value) || options[0];
    return `<div class="vq43-dropdown-field"><label>${escapeHtml(label)}</label><div class="vq43-dropdown" data-vq-dropdown="${kind}"><button type="button" class="vq43-dropdown-button" aria-haspopup="listbox" aria-expanded="false"><span>${escapeHtml(selected?.label || '')}</span><b aria-hidden="true">⌄</b></button><div class="vq43-dropdown-menu" role="listbox">${options.map((option) => `<button type="button" class="vq43-dropdown-option ${option.value === value ? 'selected' : ''}" data-value="${escapeHtml(option.value)}" role="option" aria-selected="${option.value === value}">${escapeHtml(option.label)}</button>`).join('')}</div></div></div>`;
  }

  function renderAnalysis() {
    const model = state.model;
    if (!model?.records.length || !model.tools.length) return emptyPage('Score 분석 데이터가 없습니다', '설정에서 *_result 및 *_score 열이 포함된 Position 결과 파일을 입력하세요.');
    if (!model.tools.includes(state.analysisTool)) state.analysisTool = model.tools[0];
    const allowedScopes = ['TOOL_OK', 'TOOL_NG', 'ACTUAL_NG_TOOL_NG', 'ACTUAL_NG_TOOL_OK'];
    if (!allowedScopes.includes(state.analysisScope)) state.analysisScope = 'TOOL_NG';
    const points = scorePoints(state.analysisTool, state.analysisScope, state.analysisPosition);
    state.analysisPoints = points;
    state.analysisPointMap = new Map(points.map((point) => [point.key, point]));
    const values = points.map((point) => point.score);
    const avg = mean(values), min = values.length ? Math.min(...values) : null, max = values.length ? Math.max(...values) : null, med = median(values);
    const okValues = points.filter((point) => point.result === 'OK').map((point) => point.score);
    const ngValues = points.filter((point) => point.result === 'NG').map((point) => point.score);
    const scopeInfo = {
      TOOL_OK: { text: '실제 NG 이미지 경로와 관계없이 선택 Tool의 원본 OK 판정 Score만 표시합니다.', label: '선택 Tool의 OK Score', color: '초록: Tool OK Score' },
      TOOL_NG: { text: '실제 NG 이미지 경로와 관계없이 선택 Tool의 원본 NG 판정 Score만 표시합니다.', label: '선택 Tool의 NG Score', color: '빨강: Tool NG Score' },
      ACTUAL_NG_TOOL_NG: { text: 'NG 이미지 폴더에 동일 Cell ID + Position 이미지가 있고, 선택 Tool이 NG로 판정한 Score만 표시합니다.', label: 'NG Image를 NG로 검출한 Score', color: '빨강: 실제 NG 이미지 중 Tool NG Score' },
      ACTUAL_NG_TOOL_OK: { text: 'NG 이미지 폴더에 동일 Cell ID + Position 이미지가 있고, 선택 Tool이 OK로 판정한 Score만 표시합니다.', label: 'NG Image를 OK로 판정한 Score', color: '초록: 실제 NG 이미지 중 Tool OK Score' }
    }[state.analysisScope];
    $('#vq43-page').innerHTML = `
      <div class="vq43-content"><div class="vq43-eyebrow" style="color:#a78bfa">Detailed Analysis</div><h1 class="vq43-title">Tool별 Score 분석</h1><p class="vq43-subtitle">Tool 판정 결과와 Score를 조건별로 분리하여 분석합니다.</p>
        <div class="vq43-filter">${customDropdown('position', 'Position', state.analysisPosition, [{ value: 'ALL', label: '전체 Position' }, ...POSITIONS.map((position) => ({ value: position, label: position }))])}${customDropdown('tool', 'Tool', state.analysisTool, model.tools.map((tool) => ({ value: tool, label: tool })))}${customDropdown('scope', '분석 범위', state.analysisScope, [{ value: 'TOOL_OK', label: '선택 Tool의 OK Score' }, { value: 'TOOL_NG', label: '선택 Tool의 NG Score' }, { value: 'ACTUAL_NG_TOOL_NG', label: 'NG Image를 NG로 검출한 Score' }, { value: 'ACTUAL_NG_TOOL_OK', label: 'NG Image를 OK로 판정한 Score' }])}</div>
        <div class="vq43-kpi-grid">${kpi('Score 데이터', numberText(points.length), `OK ${okValues.length} · NG ${ngValues.length}`)}${kpi('평균 Score', scoreText(avg), '그래프 파란 점선', 'blue')}${kpi('최소 Score', scoreText(min), '그래프 노란 강조', 'amber')}${kpi('최대 / 중앙값', scoreText(max), `Median ${scoreText(med)}`)}</div>
        <div class="vq43-analysis-export"><div><strong>Score 조건 CSV 저장</strong><span>현재 Position · Tool · 분석 범위 안에서 Score 조건으로 필터합니다.</span></div><label>Score<input id="vq43-score-cutoff" type="number" inputmode="decimal" min="0.50" max="1.00" step="0.01" value="${state.analysisScoreCutoff.toFixed(2)}"></label>${customDropdown('compare', '조건', state.analysisScoreCompare, [{ value: 'GTE', label: '이상 (≥)' }, { value: 'LTE', label: '이하 (≤)' }])}<button class="vq43-btn vq43-btn-green" data-vq-action="download-score-filter">CSV 저장</button></div>
        <div class="vq43-note" style="margin-top:16px">${escapeHtml(scopeInfo.text)}</div>
        <div class="vq43-chart-grid"><div class="vq43-chart-card"><div class="vq43-chart-head"><div><h3>Score 분포</h3><p>${escapeHtml(scopeInfo.color)}</p></div><span>▥</span></div><div class="vq43-chart-area">${points.length ? histogramSvg(points) : '<div class="vq43-chart-empty">해당 조건의 Score가 없습니다.</div>'}</div></div><div class="vq43-chart-card"><div class="vq43-chart-head"><div><h3>Cell별 Score</h3><p>낮은 Score부터 정렬 · 점에 마우스를 올리면 Cell ID · Position · Score 표시</p></div><button class="vq43-chart-expand-btn" type="button" data-vq-action="open-chart-modal" ${points.length ? '' : 'disabled'}>⛶ 확대 보기</button></div><div class="vq43-chart-area">${points.length ? scatterSvg(points) : '<div class="vq43-chart-empty">해당 조건의 Score가 없습니다.</div>'}</div></div></div>
        <div class="vq43-table vq43-summary-table" style="margin-bottom:30px"><div class="vq43-table-row head"><span>구분</span><span>개수</span><span>평균</span><span>최소</span><span>최대</span><span>중앙값</span></div>${scoreSummaryRow('OK', okValues)}${scoreSummaryRow('NG', ngValues)}</div>
      </div>`;
  }

  function scoreSummaryRow(label, values) {
    return `<div class="vq43-table-row"><span class="tool ${label === 'NG' ? 'vq43-red' : 'vq43-green'}">${label}</span><span>${values.length}</span><span class="vq43-mono">${scoreText(mean(values))}</span><span class="vq43-mono vq43-amber">${scoreText(values.length ? Math.min(...values) : null)}</span><span class="vq43-mono">${scoreText(values.length ? Math.max(...values) : null)}</span><span class="vq43-mono">${scoreText(median(values))}</span></div>`;
  }

  function histogramSvg(points) {
    const width = 900, height = 280, left = 54, right = 18, top = 24, bottom = 44;
    const minScore = 0.50, maxScore = 1.00, step = 0.05;
    const bucketCount = Math.round((maxScore - minScore) / step);
    const plotW = width - left - right, plotH = height - top - bottom;
    const buckets = Array.from({ length: bucketCount }, (_, i) => ({ start: minScore + i * step, end: minScore + (i + 1) * step, ok: 0, ng: 0 }));
    points.forEach((point) => {
      const clamped = Math.max(minScore, Math.min(maxScore, point.score));
      const index = Math.min(bucketCount - 1, Math.max(0, Math.floor((clamped - minScore) / step)));
      if (point.result === 'NG') buckets[index].ng += 1; else buckets[index].ok += 1;
    });
    const maxCount = Math.max(1, ...buckets.flatMap((bucket) => [bucket.ok, bucket.ng]));
    const groupW = plotW / bucketCount, barW = groupW * .31;
    const countGrid = [0,.25,.5,.75,1].map((ratio) => {
      const y = top + plotH * (1-ratio);
      return `<line x1="${left}" x2="${width-right}" y1="${y}" y2="${y}" stroke="#1e293b"/><text x="${left-9}" y="${y+4}" text-anchor="end" font-size="13" fill="#64748b">${Math.round(maxCount*ratio)}</text>`;
    }).join('');
    const bars = buckets.map((bucket, i) => {
      const center = left + groupW*i + groupW/2;
      const okH = plotH*bucket.ok/maxCount, ngH = plotH*bucket.ng/maxCount;
      return `<rect x="${center-barW-1}" y="${top+plotH-okH}" width="${barW}" height="${okH}" rx="2" fill="#10b981"><title>${bucket.start.toFixed(2)}~${bucket.end.toFixed(2)} OK ${bucket.ok}</title></rect><rect x="${center+1}" y="${top+plotH-ngH}" width="${barW}" height="${ngH}" rx="2" fill="#ef4444"><title>${bucket.start.toFixed(2)}~${bucket.end.toFixed(2)} NG ${bucket.ng}</title></rect><text x="${center}" y="${height-15}" text-anchor="middle" font-size="12" fill="#64748b">${bucket.start.toFixed(2)}</text>`;
    }).join('');
    return `<svg viewBox="0 0 ${width} ${height}">${countGrid}${bars}<line x1="${left}" x2="${width-right}" y1="${top+plotH}" y2="${top+plotH}" stroke="#475569"/><text x="${width-right}" y="${height-3}" text-anchor="end" font-size="12" fill="#64748b">Score (0.05 간격)</text></svg>`;
  }

  function scatterSvg(points, options = {}) {
    const sorted = [...points].sort((a,b) => a.score-b.score), values = sorted.map((point) => point.score), avg = mean(values), min = values.length ? Math.min(...values) : null;
    const width=options.width || 900,height=options.height || 300,left=options.large?72:54,right=options.large?34:20,top=options.large?38:24,bottom=options.large?58:42,plotW=width-left-right,plotH=height-top-bottom;
    const minScore = 0.50, maxScore = 1.00, step = 0.05;
    const y = (score) => {
      const clamped = Math.max(minScore, Math.min(maxScore, score));
      return top + plotH * (1 - (clamped - minScore) / (maxScore - minScore));
    };
    const x = (index) => left+(sorted.length<=1?plotW/2:plotW*index/(sorted.length-1));
    const ticks = Array.from({ length: 11 }, (_, i) => minScore + i * step);
    const tickFont = options.large ? 17 : 12;
    const grid=ticks.map((value)=>`<line x1="${left}" x2="${width-right}" y1="${y(value)}" y2="${y(value)}" stroke="#1e293b"/><text x="${left-11}" y="${y(value)+5}" text-anchor="end" font-size="${tickFont}" fill="#7c8da8">${value.toFixed(2)}</text>`).join('');
    const lines=`${avg!==null?`<line x1="${left}" x2="${width-right}" y1="${y(avg)}" y2="${y(avg)}" stroke="#38bdf8" stroke-width="2" stroke-dasharray="6 5"/><text x="${width-right}" y="${y(avg)-8}" text-anchor="end" font-size="${options.large?18:13}" fill="#7dd3fc">AVG ${avg.toFixed(4)}</text>`:''}${min!==null?`<line x1="${left}" x2="${width-right}" y1="${y(min)}" y2="${y(min)}" stroke="#f59e0b" stroke-width="1.5" stroke-dasharray="3 4"/><text x="${left+5}" y="${y(min)-8}" font-size="${options.large?18:13}" fill="#fbbf24">MIN ${min.toFixed(4)}</text>`:''}`;
    const poly=sorted.length>1?`<polyline points="${sorted.map((point,i)=>`${x(i)},${y(point.score)}`).join(' ')}" fill="none" stroke="#334155" stroke-width="${options.large?1.5:1}"/>`:'';
    const dots=sorted.map((point,i)=>{
      const attrs = options.interactive ? ` class="vq43-scatter-point" data-vq-point-key="${escapeHtml(point.key)}" tabindex="0" role="button" aria-label="${escapeHtml(point.cellId)} ${escapeHtml(point.position)} Score ${point.score.toFixed(4)}"` : '';
      const radius = point.score===min ? (options.large?8:5) : (options.large?6:3.5);
      const imageStroke = point.hasActualImage ? '#a78bfa' : '#0f172a';
      return `<circle${attrs} cx="${x(i)}" cy="${y(point.score)}" r="${radius}" fill="${point.result==='NG'?'#ef4444':'#10b981'}" stroke="${point.score===min?'#fbbf24':imageStroke}" stroke-width="${point.score===min?3:(point.hasActualImage?2:1)}"><title>${escapeHtml(point.cellId)} · ${escapeHtml(point.position)} · ${point.result} · Score ${point.score.toFixed(4)}${point.hasActualImage?' · 이미지 있음':''}</title></circle>`;
    }).join('');
    return `<svg viewBox="0 0 ${width} ${height}" data-vq-scatter-svg="1">${grid}${lines}${poly}${dots}<text x="${left}" y="${height-(options.large?18:12)}" font-size="${options.large?17:13}" fill="#7c8da8">낮은 Score</text><text x="${width-right}" y="${height-(options.large?18:12)}" text-anchor="end" font-size="${options.large?17:13}" fill="#7c8da8">높은 Score</text></svg>`;
  }


  function ensureSimulationForm() {
    const form = state.simulationForm || (state.simulationForm = {});
    form.outputRoot = form.outputRoot || '';
    form.gpuDevices = form.gpuDevices || '0';
    form.jpegQuality = Number(form.jpegQuality || 80);
    form.printEvery = Number(form.printEvery || 100);
    if (typeof form.useGpu !== 'boolean') form.useGpu = true;
    if (typeof form.keepSubfolders !== 'boolean') form.keepSubfolders = false;
    if (typeof form.keepCropImages !== 'boolean') form.keepCropImages = false;
    if (typeof form.heatmapImageSave !== 'boolean') form.heatmapImageSave = true;
    form.positions = form.positions || {};
    [['CA(TOP)','CA_TOP'],['AN(TOP)','AN_TOP'],['CA(BOT)','CA_BOT'],['AN(BOT)','AN_BOT']].forEach(([label,key]) => {
      form.positions[key] = Object.assign({
        key, displayName:label, enabled:true, workspacePath:'', greenWorkspacePath:'', blueWorkspacePath:'', imageRoot:'',
        streamName:'기본값', greenStreamName:'기본값', blueStreamName:'기본값', blueToolName:'Locate', keyword:''
      }, form.positions[key] || {});
    });
    return form;
  }

  function syncSimulationField(input) {
    const form = ensureSimulationForm();
    const field = input.dataset.simField;
    const key = input.dataset.simKey;
    const value = input.type === 'checkbox' ? input.checked : input.type === 'number' ? Number(input.value) : input.value;
    if (key) {
      form.positions[key] = form.positions[key] || { key };
      form.positions[key][field] = value;
    } else {
      form[field] = value;
    }
  }

  async function agentFetch(path, options = {}) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), options.timeout || 6000);
    try {
      const response = await fetch(`${LOCAL_AGENT_URL}${path}`, {
        method: options.method || 'GET',
        cache: 'no-store',
        headers: options.body ? { 'Content-Type': 'application/json' } : undefined,
        body: options.body ? JSON.stringify(options.body) : undefined,
        signal: controller.signal
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || `HTTP ${response.status}`);
      return data;
    } finally { clearTimeout(timer); }
  }

  async function checkSimulationAgent() {
    if (state.simulationChecking) return;
    state.simulationChecking = true;
    state.simulationAgent = { ...state.simulationAgent, status: 'checking', message: '127.0.0.1 Local Agent 확인 중...' };
    if (state.page === 'simulation') { renderSimulation(); bindPageControls(); }
    try {
      const data = await agentFetch('/api/status', { timeout: 2500 });
      state.simulationAgent = {
        status: 'connected', version: data.agentVersion || '-', vpdl: data.vpdlVersion || '-',
        license: data.license || 'Simulation Start 시 확인', gpu: data.gpu || '-',
        message: `Local Agent 연결됨 · Engine ${data.engineVersion || '-'}`
      };
      if (data.state) state.simulationProgress = { ...state.simulationProgress, ...data.state };
      connectSimulationEvents();
    } catch (_) {
      state.simulationAgent = { status: 'offline', version: '-', vpdl: '-', license: '-', gpu: '-', message: 'Local Agent가 실행 중이지 않습니다.' };
      closeSimulationEvents();
    } finally {
      state.simulationChecking = false;
      if (state.page === 'simulation') { renderSimulation(); bindPageControls(); }
    }
  }

  function launchSimulationAgent() {
    showToast('Local Agent 실행 요청을 보냈습니다. Chrome 확인창이 나오면 열기를 허용하세요.');
    window.location.href = 'visionqc-agent://start';
    setTimeout(checkSimulationAgent, 1800);
    setTimeout(checkSimulationAgent, 4000);
  }

  async function checkSimulationRuntime() {
    const form = ensureSimulationForm();
    try {
      const data = await agentFetch('/api/runtime/check', { method:'POST', body:{ useGpu:form.useGpu, gpuDevices:form.gpuDevices }, timeout:12000 });
      state.simulationAgent.license = data.license || (data.ok ? 'Runtime OK' : 'Runtime Error');
      state.simulationAgent.vpdl = data.vpdlVersion || state.simulationAgent.vpdl;
      state.simulationAgent.gpu = data.gpu || state.simulationAgent.gpu;
      state.simulationAgent.message = data.ok ? 'VPDL Runtime 생성 확인 완료' : (data.error || 'Runtime 확인 실패');
      renderSimulation(); bindPageControls();
    } catch (error) { showToast(`Runtime 확인 실패: ${error.message}`, true); }
  }

  function connectSimulationEvents() {
    closeSimulationEvents();
    try {
      const events = new EventSource(`${LOCAL_AGENT_URL}/api/events`);
      ['status','progress','completed','stopped','error'].forEach((name) => events.addEventListener(name, (event) => {
        try { state.simulationProgress = { ...state.simulationProgress, ...JSON.parse(event.data) }; updateSimulationStatusDom(); } catch (_) { }
      }));
      events.onerror = () => { /* status button can explicitly reconnect */ };
      state.simulationEvents = events;
    } catch (_) { }
  }

  function closeSimulationEvents() {
    try { state.simulationEvents?.close(); } catch (_) { }
    state.simulationEvents = null;
  }

  async function browseSimulationPath(control) {
    if (state.simulationAgent.status !== 'connected') { showToast('먼저 Local Agent를 실행/연결하세요.', true); return; }
    const form = ensureSimulationForm();
    const key = control.dataset.simKey || '';
    const field = control.dataset.simField;
    const kind = control.dataset.simKind || 'folder';
    const current = key ? (form.positions[key]?.[field] || '') : (form[field] || '');
    const originalText = control.textContent;
    control.disabled = true;
    control.textContent = '여는 중...';
    showToast(kind === 'file' ? 'Windows 파일 선택창을 여는 중입니다.' : 'Windows 폴더 선택창을 여는 중입니다.');
    try {
      const data = await agentFetch(kind === 'file' ? '/api/pick/file' : '/api/pick/folder', {
        method:'POST', body:{ initialPath:current }, timeout:120000
      });
      if (!data.ok || !data.path) return;
      if (key) form.positions[key][field] = data.path; else form[field] = data.path;
      const selector = `input[data-sim-field="${CSS.escape(field)}"]${key ? `[data-sim-key="${CSS.escape(key)}"]` : ':not([data-sim-key])'}`;
      const input = $(selector);
      if (input) input.value = data.path;
    } catch (error) {
      showToast(`선택 실패: ${error.message}`, true);
    } finally {
      if (control && control.isConnected) {
        control.disabled = false;
        control.textContent = originalText || '선택';
      }
    }
  }

  function buildSimulationRequest() {
    const form = ensureSimulationForm();
    return {
      mode: state.simulationMode || 'integrated', outputRoot: form.outputRoot, useGpu: !!form.useGpu, gpuDevices: String(form.gpuDevices || '0'),
      jpegQuality: Number(form.jpegQuality || 80), printEvery: Number(form.printEvery || 100), keepSubfolders: !!form.keepSubfolders,
      heatmapImageSave: !!form.heatmapImageSave, keepCropImages: !!form.keepCropImages,
      positions: Object.values(form.positions).map(p => ({ ...p }))
    };
  }

  async function startSimulation() {
    if (state.simulationAgent.status !== 'connected') { launchSimulationAgent(); return; }
    const request = buildSimulationRequest();
    try {
      const data = await agentFetch('/api/simulation/start', { method:'POST', body:request, timeout:10000 });
      if (!data.ok) throw new Error(data.error || 'Simulation 시작 실패');
      state.simulationProgress = { ...state.simulationProgress, ...(data.state || {}), running:true };
      updateSimulationStatusDom();
    } catch (error) { showToast(error.message, true); }
  }

  async function stopSimulation() {
    try { await agentFetch('/api/simulation/stop', { method:'POST', body:{}, timeout:5000 }); }
    catch (error) { showToast(`중지 실패: ${error.message}`, true); }
  }

  function updateSimulationStatusDom() {
    if (state.page !== 'simulation') return;
    const s = state.simulationProgress || {};
    const total = Number(s.total || 0), processed = Number(s.processed || 0);
    const rate = total > 0 ? Math.min(100, Math.max(0, processed * 100 / total)) : 0;
    const count = $('#vq43-sim-progress-count'); if (count) count.textContent = `${numberText(processed)} / ${numberText(total)}`;
    const pct = $('#vq43-sim-progress-pct'); if (pct) pct.textContent = `${rate.toFixed(2)}%`;
    const bar = $('#vq43-sim-progress-bar'); if (bar) bar.style.width = `${rate}%`;
    const ok = $('#vq43-sim-ok'); if (ok) ok.textContent = numberText(s.ok || 0);
    const ng = $('#vq43-sim-ng'); if (ng) ng.textContent = numberText(s.ng || 0);
    const cur = $('#vq43-sim-current'); if (cur) cur.textContent = s.current || '-';
    const log = $('#vq43-sim-log'); if (log) log.innerHTML = `<span>${s.error ? '[ERROR]' : s.running ? '[RUN]' : '[READY]'}</span> ${escapeHtml(s.message || 'Ready')}`;
    const start = $('#vq43-sim-start'); if (start) start.disabled = !!s.running || state.simulationAgent.status !== 'connected';
    const stop = $('#vq43-sim-stop'); if (stop) stop.disabled = !s.running;
  }

  function simulationModeLabel(mode) {
    if (mode === 'green') return 'Green Simulation';
    if (mode === 'blue') return 'Blue Crop';
    return 'Integrated Simulation';
  }

  function simPathField(key, field, title, placeholder, kind='file') {
    const form = ensureSimulationForm();
    const value = form.positions[key]?.[field] || '';
    return `<div class="vq43-sim-path"><span>${title}</span><div><input data-sim-field="${field}" data-sim-key="${key}" value="${escapeHtml(value)}" placeholder="${escapeHtml(placeholder)}"><button type="button" data-vq-action="simulation-browse" data-sim-kind="${kind}" data-sim-field="${field}" data-sim-key="${key}">선택</button></div></div>`;
  }

  function simulationPositionRows() {
    const mode = state.simulationMode || 'integrated';
    const form = ensureSimulationForm();
    return [['CA(TOP)','CA_TOP'],['AN(TOP)','AN_TOP'],['CA(BOT)','CA_BOT'],['AN(BOT)','AN_BOT']].map(([label,key]) => {
      const p = form.positions[key];
      const use = `<label class="vq43-sim-use"><input type="checkbox" data-sim-field="enabled" data-sim-key="${key}" ${p.enabled ? 'checked' : ''}><span>${label}</span></label>`;
      const image = simPathField(key, 'imageRoot', 'Image Folder', '로컬 이미지 폴더', 'folder');
      if (mode === 'integrated') return `<div class="vq43-sim-position-row integrated">${use}${simPathField(key,'greenWorkspacePath','Green Workspace','Green Runtime Workspace')}${simPathField(key,'blueWorkspacePath','Blue Workspace','Blue Runtime Workspace')}${image}<div class="vq43-sim-stream-stack"><label><span>Green Stream</span><input data-sim-field="greenStreamName" data-sim-key="${key}" value="${escapeHtml(p.greenStreamName)}"></label><label><span>Blue Stream / Tool</span><div><input data-sim-field="blueStreamName" data-sim-key="${key}" value="${escapeHtml(p.blueStreamName)}"><input data-sim-field="blueToolName" data-sim-key="${key}" value="${escapeHtml(p.blueToolName)}"></div></label></div></div>`;
      const extra = mode === 'blue' ? `<div class="vq43-sim-stream-stack"><label><span>Stream</span><input data-sim-field="streamName" data-sim-key="${key}" value="${escapeHtml(p.streamName)}"></label><label><span>Blue Tool</span><input data-sim-field="blueToolName" data-sim-key="${key}" value="${escapeHtml(p.blueToolName)}"></label></div>` : `<label class="vq43-sim-stream"><span>Stream</span><input data-sim-field="streamName" data-sim-key="${key}" value="${escapeHtml(p.streamName)}"></label>`;
      return `<div class="vq43-sim-position-row">${use}${simPathField(key,'workspacePath','Workspace','Runtime Workspace')}${image}${extra}</div>`;
    }).join('');
  }

  function renderSimulation() {
    const page = $('#vq43-page'); if (!page) return;
    const form = ensureSimulationForm();
    const agent = state.simulationAgent || {}, connected = agent.status === 'connected';
    const checking = state.simulationChecking || agent.status === 'checking';
    const statusClass = connected ? 'ready' : checking ? 'checking' : 'offline';
    const statusText = connected ? 'Connected' : checking ? 'Checking...' : 'Stopped';
    const mode = state.simulationMode || 'integrated';
    const modeText = simulationModeLabel(mode);
    const s = state.simulationProgress || {};
    const total = Number(s.total || 0), processed = Number(s.processed || 0), rate = total > 0 ? Math.min(100, processed * 100 / total) : 0;
    const modeDescription = mode === 'integrated' ? 'Blue Crop → Green 검사를 사용자 PC VPDL Runtime에서 연속 실행합니다.' : mode === 'green' ? 'Green Tool 단독 시뮬레이션입니다.' : 'Blue Tool Locate 결과 기준 Crop 시뮬레이션입니다.';

    page.innerHTML = `<div class="vq43-content vq43-sim-page">
      <div class="vq43-topline"><div><div class="vq43-eyebrow">VPDL Local Simulation</div><h1 class="vq43-title">VPDL 시뮬레이션</h1><p class="vq43-subtitle">GUI는 VisionQC Web, Runtime · License · GPU · 이미지 작업은 사용자 로컬 PC에서 실행합니다.</p></div><div class="vq43-top-actions"><button class="vq43-btn" data-vq-action="simulation-agent-info">구조 안내</button><button class="vq43-btn" data-vq-action="simulation-agent-launch">Agent 실행</button><button class="vq43-btn vq43-btn-blue" data-vq-action="simulation-agent-check">${checking?'◌ 확인 중...':'연결 확인'}</button></div></div>
      <section class="vq43-sim-agent-card ${statusClass}"><div class="vq43-sim-agent-title"><div><span class="vq43-sim-dot"></span><strong>Local Engine</strong></div><b>${statusText}</b></div><div class="vq43-sim-agent-grid"><div><span>Agent</span><strong>${escapeHtml(agent.version||'-')}</strong></div><div><span>VPDL Runtime</span><strong>${escapeHtml(agent.vpdl||'-')}</strong></div><div><span>License</span><strong>${escapeHtml(agent.license||'-')}</strong></div><div><span>GPU</span><strong>${escapeHtml(agent.gpu||'-')}</strong></div></div><p>${escapeHtml(agent.message||'Local Agent 연결 전')}</p>${connected?'<button class="vq43-btn vq43-btn-blue vq43-sim-runtime-check" data-vq-action="simulation-runtime-check">Runtime / License 실제 확인</button>':''}</section>
      <div class="vq43-sim-tabs"><button class="${mode==='integrated'?'active':''}" data-vq-action="simulation-mode" data-vq-mode="integrated">Integrated Simulation</button><button class="${mode==='green'?'active':''}" data-vq-action="simulation-mode" data-vq-mode="green">Green Simulation</button><button class="${mode==='blue'?'active':''}" data-vq-action="simulation-mode" data-vq-mode="blue">Blue Crop</button></div>
      <section class="vq43-sim-panel"><div class="vq43-sim-panel-head"><div><strong>${modeText}</strong><span>${modeDescription}</span></div><span class="vq43-sim-local-badge">LOCAL PC</span></div><div class="vq43-sim-position-list">${simulationPositionRows()}</div></section>
      <div class="vq43-sim-two-col"><section class="vq43-sim-panel"><div class="vq43-sim-panel-head"><div><strong>Output / Runtime</strong><span>Local Agent에 전달할 실행 설정</span></div></div><div class="vq43-sim-form-grid"><label class="wide"><span>Output Folder</span><div class="vq43-sim-inline"><input data-sim-field="outputRoot" value="${escapeHtml(form.outputRoot)}" placeholder="결과 저장 폴더"><button type="button" data-vq-action="simulation-browse" data-sim-kind="folder" data-sim-field="outputRoot">선택</button></div></label><label><span>GPU Devices</span><input data-sim-field="gpuDevices" value="${escapeHtml(form.gpuDevices)}"></label><label><span>JPEG Quality</span><input data-sim-field="jpegQuality" type="number" value="${form.jpegQuality}" min="1" max="100"></label><label><span>Progress Update</span><input data-sim-field="printEvery" type="number" value="${form.printEvery}" min="1"></label><label class="vq43-sim-check"><input data-sim-field="useGpu" type="checkbox" ${form.useGpu?'checked':''}><span>GPU 사용</span></label><label class="vq43-sim-check"><input data-sim-field="keepSubfolders" type="checkbox" ${form.keepSubfolders?'checked':''}><span>하위 폴더 구조 유지</span></label>${mode==='integrated'?`<label class="vq43-sim-check"><input data-sim-field="keepCropImages" type="checkbox" ${form.keepCropImages?'checked':''}><span>Blue Crop 이미지 저장</span></label>`:''}${mode!=='blue'?`<label class="vq43-sim-check"><input data-sim-field="heatmapImageSave" type="checkbox" ${form.heatmapImageSave?'checked':''}><span>HeatMap Image Save</span></label>`:''}</div></section>
      <section class="vq43-sim-panel"><div class="vq43-sim-panel-head"><div><strong>Simulation Status</strong><span>Local Agent 실시간 결과</span></div></div><div class="vq43-sim-progress-head"><strong id="vq43-sim-progress-count">${numberText(processed)} / ${numberText(total)}</strong><b id="vq43-sim-progress-pct">${rate.toFixed(2)}%</b></div><div class="vq43-sim-progress"><i id="vq43-sim-progress-bar" style="width:${rate}%"></i></div><div class="vq43-sim-kpis"><div><span>OK</span><b id="vq43-sim-ok">${numberText(s.ok||0)}</b></div><div><span>NG</span><b id="vq43-sim-ng">${numberText(s.ng||0)}</b></div><div><span>Current</span><b id="vq43-sim-current">${escapeHtml(s.current||'-')}</b></div></div><div class="vq43-sim-log" id="vq43-sim-log"><span>${s.error?'[ERROR]':s.running?'[RUN]':'[READY]'}</span> ${escapeHtml(s.message || (connected?'Local Agent 연결 완료':'Local Agent 연결 전'))}</div></section></div>
      <section class="vq43-sim-runbar"><div><strong>Local VPDL 실행</strong><span>Simulation을 시작하면 Agent가 로컬 Runtime/GPU에서 처리하고 결과를 실시간 전달합니다.</span></div><div><button id="vq43-sim-stop" class="vq43-btn vq43-btn-red" data-vq-action="simulation-stop" ${s.running?'':'disabled'}>Stop</button><button id="vq43-sim-start" class="vq43-btn vq43-btn-blue" data-vq-action="simulation-start" ${connected&&!s.running?'':'disabled'}>Simulation Start</button></div></section>
    </div>`;
  }

  function renderSettings() {
    const model = state.model || { uniqueCellCount:0, misses:[], duplicates:[] };
    const warningList = [...state.restoreWarnings, ...POSITIONS.flatMap((position) => (state.resultInputs[position]?.warnings || []).map((warning) => `${position}: ${warning}`)), ...state.ngWarnings];
    const ngCounts = Object.fromEntries(POSITIONS.map((position) => [position, state.ngImages.filter((image) => image.position === position).length]));
    $('#vq43-page').innerHTML = `
      <div class="vq43-content"><div class="vq43-topline"><div><div class="vq43-eyebrow" style="color:#22d3ee">Input & Configuration</div><h1 class="vq43-title">분석 Input 설정</h1><p class="vq43-subtitle">Position 결과 파일은 1개만 입력해도 되며 입력하지 않은 Position은 분석에서 제외합니다.</p></div><button class="vq43-btn vq43-btn-red" data-vq-action="clear-inputs">전체 초기화</button></div>
        <section class="vq43-settings-card"><div class="vq43-settings-title"><span class="vq43-settings-icon">▦</span><div><h3>1. Position별 시뮬레이션 결과 파일</h3><p>CSV 또는 XLSX · Cell ID, Total_result, Tool_result, Tool_score 열 자동 인식</p></div></div><div class="vq43-input-list">${POSITIONS.map(resultInputRow).join('')}</div></section>
        <section class="vq43-settings-card"><div class="vq43-settings-title"><span class="vq43-settings-icon amber">▣</span><div><h3>2. 실제 최종 NG 이미지 경로</h3><p>루트 아래 CA(TOP), AN(TOP), CA(BOT), AN(BOT) 폴더에서 이미지와 Cell ID를 찾습니다.</p></div></div><div class="vq43-folder-box"><div class="vq43-folder-info"><div class="vq43-folder-label">Selected Folder</div><div class="vq43-folder-name">${escapeHtml(state.ngRootName || '실제 NG 이미지 폴더 미입력')}</div><div class="vq43-folder-counts">${POSITIONS.map((position) => `<span>${position}<b>${ngCounts[position]}</b></span>`).join('')}</div></div><button class="vq43-btn vq43-btn-amber vq43-btn-icon" data-vq-action="choose-ng-folder">${state.loading==='ng'?'◌ 읽는 중...':'▣ NG 폴더 선택'}</button></div><div class="vq43-folder-example">${POSITIONS.map((position)=>`<code>실제_NG_이미지 / ${position} / ...이미지</code>`).join('')}</div></section>
        <section class="vq43-settings-card"><div class="vq43-settings-title"><span class="vq43-settings-icon green">✓</span><div><h3>분석 준비 상태</h3><p>현재 입력 데이터의 집계 결과</p></div></div><div class="vq43-ready-grid"><div><span>결과 Position</span><b>${POSITIONS.filter((position)=>state.resultInputs[position]).length} / 4</b></div><div><span>고유 Cell</span><b>${numberText(model.uniqueCellCount)}</b></div><div><span>실제 NG 고유값</span><b>${numberText(model.actualUniqueCount || 0)}</b></div><div><span>CSV 매칭</span><b class="vq43-blue">${numberText(model.matchedActualCount || 0)}</b></div><div><span>미매칭</span><b class="vq43-red">${numberText(model.unmatchedActualCount || 0)}</b></div><div><span>미검</span><b class="vq43-amber">${numberText(model.misses.length)}</b></div></div>${model.actualUniqueCount ? matchDiagnostic(model) : ''}</section>
        ${(warningList.length || model.duplicates.length) ? `<section class="vq43-warning"><strong>⚠ 확인 필요</strong>${warningList.map((warning)=>`<div>• ${escapeHtml(warning)}</div>`).join('')}${model.duplicates.length?`<div>• 중복 Cell ID + Position ${numberText(model.duplicates.length)}건: 하나라도 NG이면 NG로 통합하고 Score 원본 행은 유지합니다.</div>`:''}</section>`:''}
      </div>`;
  }

  function resultInputRow(position) {
    const input = state.resultInputs[position], loading = state.loading === `result:${position}`;
    return `<div class="vq43-input-row" data-vq-position="${position}"><div class="vq43-position-label">${position}</div><div>${input?`<div class="vq43-input-name">${escapeHtml(input.fileName)}</div><div class="vq43-input-meta">${numberText(input.rows.length)}행 · ${formatBytes(input.fileSize)} · Tool ${Object.keys(input.rows[0]?.tools || {}).length}개</div>`:'<div class="vq43-input-name" style="color:#475569">결과 파일 미입력</div>'}</div><button class="vq43-btn vq43-btn-blue" data-vq-action="choose-result" ${state.loading?'disabled':''}>${loading?'읽는 중...':input?'교체':'파일 선택'}</button>${input?'<button class="vq43-icon-btn" data-vq-action="remove-result" title="제거">×</button>':'<span></span>'}</div>`;
  }

  function resetChartModalView() {
    state.chartModalZoom = 1;
    state.chartModalPanX = 0;
    state.chartModalPanY = 0;
    state.chartModalDragging = false;
    applyChartModalTransform();
  }

  function applyChartModalTransform() {
    const canvas = $('#vq43-chart-modal-canvas');
    const indicator = $('#vq43-chart-modal-zoom-value');
    if (canvas) canvas.style.transform = `translate(${state.chartModalPanX}px, ${state.chartModalPanY}px) scale(${state.chartModalZoom})`;
    if (indicator) indicator.textContent = `${Math.round(state.chartModalZoom * 100)}%`;
  }

  function openChartModal() {
    if (!state.analysisPoints.length) return showToast('확대할 Score 데이터가 없습니다.', true);
    resetChartModalView();
    renderChartModal();
  }

  function renderChartModal() {
    const modal = $('#vq43-chart-modal');
    const points = state.analysisPoints;
    if (!modal || !points.length) return;
    modal.innerHTML = `<div class="vq43-chart-modal-card"><div class="vq43-chart-modal-head"><div><small>Expanded Score Chart</small><strong>${escapeHtml(state.analysisPosition === 'ALL' ? '전체 Position' : state.analysisPosition)} · ${escapeHtml(state.analysisTool)}</strong></div><div><span id="vq43-chart-modal-zoom-value">100%</span><button type="button" data-vq-action="chart-modal-reset">원위치</button><button type="button" class="vq43-close" data-vq-action="close-chart-modal">×</button></div></div><div id="vq43-chart-modal-viewport" class="vq43-chart-modal-viewport"><div id="vq43-chart-modal-canvas" class="vq43-chart-modal-canvas">${scatterSvg(points,{width:1800,height:720,large:true,interactive:true})}</div><div class="vq43-chart-modal-help">휠: 확대/축소 · 빈 공간 드래그: 이동 · 점 클릭: 실제 NG 이미지 보기</div><div id="vq43-chart-tooltip" class="vq43-chart-tooltip"></div></div></div>`;
    modal.classList.add('open');
    applyChartModalTransform();
    bindChartModalControls();
  }

  function bindChartModalControls() {
    const modal = $('#vq43-chart-modal');
    const viewport = $('#vq43-chart-modal-viewport');
    const canvas = $('#vq43-chart-modal-canvas');
    const tooltip = $('#vq43-chart-tooltip');
    if (!modal || !viewport || !canvas) return;
    modal.querySelector('[data-vq-action="close-chart-modal"]')?.addEventListener('click', (event) => { event.preventDefault(); event.stopPropagation(); closeChartModal(); });
    modal.querySelector('[data-vq-action="chart-modal-reset"]')?.addEventListener('click', (event) => { event.preventDefault(); event.stopPropagation(); resetChartModalView(); });
    modal.querySelector('.vq43-chart-modal-card')?.addEventListener('click', (event) => event.stopPropagation());
    modal.onclick = (event) => { if (event.target === modal) closeChartModal(); };
    viewport.addEventListener('wheel', (event) => {
      event.preventDefault();
      const rect = viewport.getBoundingClientRect();
      const px = event.clientX - rect.left;
      const py = event.clientY - rect.top;
      const previous = state.chartModalZoom;
      const next = Math.max(0.75, Math.min(5, previous * (event.deltaY < 0 ? 1.14 : 1 / 1.14)));
      state.chartModalPanX = px - (px - state.chartModalPanX) * (next / previous);
      state.chartModalPanY = py - (py - state.chartModalPanY) * (next / previous);
      state.chartModalZoom = next;
      applyChartModalTransform();
    }, { passive:false });
    viewport.addEventListener('pointerdown', (event) => {
      if (event.button !== 0 || event.target.closest?.('.vq43-scatter-point') || event.target.closest?.('button')) return;
      state.chartModalDragging = true;
      state.chartModalDragStartX = event.clientX - state.chartModalPanX;
      state.chartModalDragStartY = event.clientY - state.chartModalPanY;
      viewport.setPointerCapture?.(event.pointerId);
      viewport.classList.add('dragging');
    });
    viewport.addEventListener('pointermove', (event) => {
      if (!state.chartModalDragging) return;
      state.chartModalPanX = event.clientX - state.chartModalDragStartX;
      state.chartModalPanY = event.clientY - state.chartModalDragStartY;
      applyChartModalTransform();
    });
    const endDrag = (event) => {
      state.chartModalDragging = false;
      viewport.classList.remove('dragging');
      try { viewport.releasePointerCapture?.(event.pointerId); } catch (_) { /* already released */ }
    };
    viewport.addEventListener('pointerup', endDrag);
    viewport.addEventListener('pointercancel', endDrag);
    modal.querySelectorAll('.vq43-scatter-point').forEach((dot) => {
      const show = (event) => {
        const point = state.analysisPointMap.get(dot.dataset.vqPointKey || '');
        if (!point || !tooltip) return;
        tooltip.innerHTML = `<strong>${escapeHtml(point.cellId)}</strong><span>${escapeHtml(point.position)} · ${point.result} · Score ${point.score.toFixed(4)}</span>${point.hasActualImage?'<em>클릭하면 실제 NG 이미지 표시</em>':'<em>매칭 실제 NG 이미지 없음</em>'}`;
        tooltip.style.left = `${event.clientX - viewport.getBoundingClientRect().left + 14}px`;
        tooltip.style.top = `${event.clientY - viewport.getBoundingClientRect().top + 14}px`;
        tooltip.classList.add('show');
      };
      dot.addEventListener('pointerenter', show);
      dot.addEventListener('pointermove', show);
      dot.addEventListener('pointerleave', () => tooltip?.classList.remove('show'));
      dot.addEventListener('click', (event) => { event.preventDefault(); event.stopPropagation(); openScorePointImage(dot.dataset.vqPointKey || ''); });
      dot.addEventListener('keydown', (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); openScorePointImage(dot.dataset.vqPointKey || ''); } });
    });
  }

  function closeChartModal() {
    const modal = $('#vq43-chart-modal');
    modal?.classList.remove('open');
    if (modal) modal.innerHTML = '';
    resetChartModalView();
  }

  function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 ** 2) return `${(bytes/1024).toFixed(1)} KB`;
    return `${(bytes/1024**2).toFixed(1)} MB`;
  }

  function resetModalView() {
    state.modalZoom = 1;
    state.modalPanX = 0;
    state.modalPanY = 0;
    state.modalDragging = false;
    applyModalTransform();
  }

  function applyModalTransform() {
    const image = $('#vq43-modal-zoom-image');
    const indicator = $('#vq43-modal-zoom-value');
    if (image) image.style.transform = `translate(${state.modalPanX}px, ${state.modalPanY}px) scale(${state.modalZoom})`;
    if (indicator) indicator.textContent = `${Math.round(state.modalZoom * 100)}%`;
  }

  function bindModalImageControls() {
    const viewport = $('#vq43-modal-viewport');
    const image = $('#vq43-modal-zoom-image');
    if (!viewport || !image) return;
    viewport.addEventListener('wheel', (event) => {
      event.preventDefault();
      const previous = state.modalZoom;
      const factor = event.deltaY < 0 ? 1.16 : 1 / 1.16;
      state.modalZoom = Math.max(1, Math.min(8, previous * factor));
      if (state.modalZoom === 1) { state.modalPanX = 0; state.modalPanY = 0; }
      applyModalTransform();
    }, { passive: false });
    viewport.addEventListener('pointerdown', (event) => {
      if (event.button !== 0 || event.target.closest?.('button')) return;
      state.modalDragging = true;
      state.modalDragStartX = event.clientX - state.modalPanX;
      state.modalDragStartY = event.clientY - state.modalPanY;
      viewport.setPointerCapture?.(event.pointerId);
      viewport.classList.add('dragging');
    });
    viewport.addEventListener('pointermove', (event) => {
      if (!state.modalDragging) return;
      state.modalPanX = event.clientX - state.modalDragStartX;
      state.modalPanY = event.clientY - state.modalDragStartY;
      applyModalTransform();
    });
    const endDrag = (event) => {
      state.modalDragging = false;
      viewport.classList.remove('dragging');
      try { viewport.releasePointerCapture?.(event.pointerId); } catch (_) { /* already released */ }
    };
    viewport.addEventListener('pointerup', endDrag);
    viewport.addEventListener('pointercancel', endDrag);
    viewport.addEventListener('dblclick', resetModalView);
  }

  function openMissModal(key) {
    const miss = state.model?.misses.find((item) => item.key === key);
    if (!miss) return;
    state.modalMissKey = key;
    state.modalItem = { ...miss, label: 'Missed Actual NG' };
    state.modalIndex = 0;
    resetModalView();
    renderModal();
  }

  function openScorePointImage(pointKey) {
    const point = state.analysisPointMap.get(pointKey);
    if (!point) return;
    const images = state.model?.actualMap.get(point.recordKey) || [];
    if (!images.length) return showToast('이 Cell ID + Position에 매칭된 실제 NG 이미지가 없습니다.', true);
    const record = state.model?.recordMap.get(point.recordKey);
    if (!record) return;
    state.modalMissKey = null;
    state.modalItem = { key: point.recordKey, cellId: point.cellId, position: point.position, record, images, label: 'Actual NG Image' };
    state.modalIndex = 0;
    resetModalView();
    renderModal();
  }

  function renderModal() {
    const modal = $('#vq43-modal');
    const miss = state.modalItem;
    if (!miss) return closeModal();
    if (state.modalUrl) URL.revokeObjectURL(state.modalUrl);
    const image = miss.images[state.modalIndex];
    state.modalUrl = URL.createObjectURL(image.file);
    const scores = Object.values(miss.record.tools).map((tool) => `<span class="vq43-score-chip">${escapeHtml(tool.tool)}: <b style="color:${tool.result==='NG'?'#f87171':'#34d399'}">${tool.result}</b> <b>${scoreText(tool.representativeScore)}</b></span>`).join('');
    modal.innerHTML = `<div class="vq43-modal-card"><div class="vq43-modal-head"><div><small>${escapeHtml(miss.label || 'Actual NG Image')}</small><strong>${escapeHtml(miss.cellId)} · ${miss.position}</strong></div><button class="vq43-close" data-vq-action="close-modal">×</button></div><div class="vq43-modal-image" id="vq43-modal-viewport"><img id="vq43-modal-zoom-image" draggable="false" src="${state.modalUrl}" alt="${escapeHtml(image.file.name || image.relativePath)}"><div class="vq43-modal-zoom-tools"><span id="vq43-modal-zoom-value">100%</span><button data-vq-action="modal-reset">원위치</button></div><div class="vq43-modal-help">마우스 휠: 확대/축소 · 드래그: 이동 · 더블클릭: 원위치</div>${miss.images.length>1?`<button class="vq43-modal-nav prev" data-vq-action="modal-prev" ${state.modalIndex===0?'disabled':''}>‹</button><button class="vq43-modal-nav next" data-vq-action="modal-next" ${state.modalIndex>=miss.images.length-1?'disabled':''}>›</button>`:''}</div><div class="vq43-modal-foot"><div class="vq43-modal-path"><span>${escapeHtml(image.relativePath)}</span><b>${state.modalIndex+1} / ${miss.images.length}</b></div><div class="vq43-modal-scores">${scores}</div></div></div>`;
    modal.classList.add('open');
    applyModalTransform();
    bindModalImageControls();

    // 모달 버튼은 body 이벤트 위임을 사용하지 않고 직접 연결합니다.
    // 이미지 드래그의 pointer capture와 무관하게 X/이전/다음/원위치가 동작합니다.
    modal.querySelector('[data-vq-action="close-modal"]')?.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      closeModal();
    });
    modal.querySelector('[data-vq-action="modal-reset"]')?.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      resetModalView();
    });
    modal.querySelector('[data-vq-action="modal-prev"]')?.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      changeModalImage(-1);
    });
    modal.querySelector('[data-vq-action="modal-next"]')?.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      changeModalImage(1);
    });
    modal.querySelector('.vq43-modal-card')?.addEventListener('click', (event) => event.stopPropagation());
    modal.onclick = (event) => { if (event.target === modal) closeModal(); };
  }

  function changeModalImage(delta) {
    const miss = state.modalItem;
    if (!miss) return;
    state.modalIndex = Math.max(0, Math.min(miss.images.length-1, state.modalIndex+delta));
    resetModalView();
    renderModal();
  }

  function closeModal() {
    const modal = $('#vq43-modal');
    modal?.classList.remove('open');
    if (modal) modal.innerHTML = '';
    if (state.modalUrl) URL.revokeObjectURL(state.modalUrl);
    state.modalUrl = '';
    state.modalMissKey = null;
    state.modalItem = null;
    state.modalIndex = 0;
    state.modalZoom = 1;
    state.modalPanX = 0;
    state.modalPanY = 0;
    state.modalDragging = false;
  }


  function installDebugApi() {
    if (!new URLSearchParams(location.search).has('vqDebug') && !window.__VQ_DEBUG_REQUESTED__) return;
    window.__VISIONQC_DEBUG__ = {
      seedAnalysis() {
        const tools = (crackResult, crackScore) => ({ Crack:{tool:'Crack',result:crackResult,score:crackScore}, Edge:{tool:'Edge',result:'NG',score:0.72}, FoilDamage:{tool:'FoilDamage',result:'OK',score:0.84}, Trimming:{tool:'Trimming',result:'NG',score:0.68}, Welding:{tool:'Welding',result:'OK',score:0.91} });
        const rows = [
          { sourceFileName:'debug.csv', sourceRowNumber:2, cellId:'P163GG22M2100001', position:'CA(TOP)', totalResult:'NG', tools:{ Crack:{tool:'Crack',result:'NG',score:0.79}, FoilDamage:{tool:'FoilDamage',result:'OK',score:0.88}, Welding:{tool:'Welding',result:'OK',score:0.91}, Trimming:{tool:'Trimming',result:'NG',score:0.67} } },
          { sourceFileName:'debug.csv', sourceRowNumber:3, cellId:'P163GG22M2100002', position:'CA(TOP)', totalResult:'OK', tools:{ Crack:{tool:'Crack',result:'OK',score:0.88}, FoilDamage:{tool:'FoilDamage',result:'OK',score:0.94}, Welding:{tool:'Welding',result:'OK',score:0.93}, Trimming:{tool:'Trimming',result:'OK',score:0.90} } },
          { sourceFileName:'debug.csv', sourceRowNumber:4, cellId:'P163GG22M2100003', position:'AN(TOP)', totalResult:'NG', tools:tools('NG',0.63) },
          { sourceFileName:'debug.csv', sourceRowNumber:5, cellId:'P163GG22M2100004', position:'AN(TOP)', totalResult:'OK', tools:tools('OK',0.86) }
        ];
        state.resultInputs = { 'CA(TOP)':{rows:rows.filter(r=>r.position==='CA(TOP)'),fileName:'debug.csv',fileSize:100,warnings:[]}, 'AN(TOP)':{rows:rows.filter(r=>r.position==='AN(TOP)'),fileName:'debug.csv',fileSize:100,warnings:[]} };
        const svg = (text) => new File([`<svg xmlns="http://www.w3.org/2000/svg" width="800" height="500"><rect width="100%" height="100%" fill="#111827"/><text x="50%" y="50%" fill="white" font-size="42" text-anchor="middle">${text}</text></svg>`], `${text}.svg`, {type:'image/svg+xml'});
        state.ngImages = [
          {position:'CA(TOP)',cellId:'P163GG22M2100001',file:svg('actual-ng-detected'),relativePath:'NG/CA(TOP)/actual-ng-detected.svg'},
          {position:'CA(TOP)',cellId:'P163GG22M2100002',file:svg('actual-ng-tool-ok'),relativePath:'NG/CA(TOP)/actual-ng-tool-ok.svg'},
          {position:'AN(TOP)',cellId:'P163GG22M2100003',file:svg('actual-ng-five-tools'),relativePath:'NG/AN(TOP)/actual-ng-five-tools.svg'}
        ];
        state.initialized = true;
        rebuildModel();
        state.analysisPosition = 'ALL'; state.analysisTool = 'Crack'; state.analysisScope = 'TOOL_NG'; setPage('analysis');
      },
      seedDashboard() {
        this.seedAnalysis();
        setPage('main');
      },
      seedReport() {
        const resultInputs = {};
        const ngImages = [];
        POSITIONS.forEach((position, positionIndex) => {
          const rows = [];
          for (let index = 1; index <= 100; index += 1) {
            const cellId = `${['P','J','B','P'][positionIndex]}${String(positionIndex + 1).padStart(2,'0')}${String(index).padStart(13,'0')}`.slice(0,16);
            const tools = position === 'AN(TOP)' ? ['Crack','Edge','FoilDamage','Trimming','Welding'] : position.includes('BOT') ? ['Crack','FoilDamage','Welding'] : ['Crack','FoilDamage','Trimming','Welding'];
            const toolData = {};
            tools.forEach((tool, toolIndex) => { toolData[tool] = { tool, result:'OK', score:0.72 + ((index + toolIndex) % 25) / 100 }; });
            rows.push({ sourceFileName:`debug_${position}.csv`, sourceRowNumber:index+1, cellId, position, totalResult:'OK', tools:toolData });
            const svg = new File([`<svg xmlns="http://www.w3.org/2000/svg" width="400" height="240"><rect width="100%" height="100%" fill="#111827"/><text x="50%" y="50%" fill="white" text-anchor="middle">${position} ${cellId}</text></svg>`], `${cellId}.svg`, {type:'image/svg+xml'});
            ngImages.push({position,cellId,file:svg,relativePath:`NG/${position}/${cellId}.svg`});
          }
          resultInputs[position] = { rows, fileName:`debug_${position}.csv`, fileSize:100, warnings:[] };
        });
        state.resultInputs = resultInputs;
        state.ngImages = ngImages;
        state.initialized = true;
        rebuildModel();
        setPage('main');
      },
      openModal() {
        const key = 'CA(TOP)|P163GG22M2100001';
        const blob = new Blob(['debug image'], { type: 'image/png' });
        const miss = { key, cellId: 'P163GG22M2100001', position: 'CA(TOP)', images: [{ file: blob, relativePath: 'debug/CA(TOP)/sample.png' }], record: { tools: { Crack: { tool: 'Crack', result: 'OK', representativeScore: 0.75 } } } };
        if (!state.model) state.model = { tools: ['Crack'], records: [], misses: [], detectedActual: new Set() };
        state.model.misses = [miss]; openMissModal(key);
      },
      snapshot() { return { page: state.page, menuOpen: state.menuOpen, modalOpen: $('#vq43-modal')?.classList.contains('open'), chartModalOpen: $('#vq43-chart-modal')?.classList.contains('open'), openDropdowns: $$('.vq43-dropdown.open').length, analysisScope: state.analysisScope, analysisPointCount: state.analysisPoints.length }; },
      getThreshold(position, tool) { return getThreshold(position, tool); },
      buildReportHtml() { return buildSummaryReportHtml(state.model, new Date('2026-07-31T08:37:00')); }
    };
  }

  async function init() {
    createExtensionDom();
    installInteractionGuards();
    patchReactHeader();
    const observer = new MutationObserver(() => patchReactHeader());
    const root = $('#root');
    if (root) observer.observe(root, { childList: true, subtree: true });

    // 저장 데이터 복원이 끝날 때까지 화면을 막지 않습니다.
    // Chrome 프로필마다 IndexedDB/폴더 핸들 상태가 달라도 즉시 기본 화면으로 진입합니다.
    state.initialized = true;
    rebuildModel();
    setPage(state.page);
    installDebugApi();

    restoreInputs().catch((error) => {
      console.error('Background restore failed:', error);
      state.restoreWarnings.push(error?.message || '저장된 Input 복원 실패');
      rebuildModel();
      if (state.page !== 'classification') renderCurrentPage();
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
