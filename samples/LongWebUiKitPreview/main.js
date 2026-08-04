const ui = window.LongUI;
const versionBadge = document.getElementById('versionBadge');
const statePreview = document.getElementById('statePreview');
const progress = document.getElementById('progress');
const progressFill = document.getElementById('progressFill');
const progressValue = document.getElementById('progressValue');

function renderEnvironment() {
  const language = ui?.language?.resolvedLanguage || document.documentElement.lang || 'unknown';
  const viewport = ui?.viewport || { width: 0, height: 0 };
  versionBadge.textContent = `UI Kit ${ui?.version ?? 'unavailable'} · ${language} · ${viewport.width}x${viewport.height}`;
}

ui?.onLanguageChanged(renderEnvironment);
ui?.onViewportChanged(renderEnvironment);
renderEnvironment();
progressFill.style.width = '64%';

function renderState(kind) {
  const copy = {
    empty: ['暂无内容', '尝试调整筛选条件。'],
    loading: ['正在加载', '请稍候。'],
    error: ['加载失败', '检查后可重新尝试。']
  };
  const [title, detail] = copy[kind] ?? copy.empty;
  ui?.renderState(statePreview, {
    kind,
    title,
    detail,
    actionLabel: kind === 'error' ? '重试' : undefined,
    onAction: kind === 'error' ? () => renderState('loading') : undefined
  });
}

document.querySelectorAll('[data-state]').forEach(button => {
  button.addEventListener('click', () => renderState(button.dataset.state));
});

document.getElementById('primaryButton').addEventListener('click', () => {
  const next = Number(progress.getAttribute('aria-valuenow')) >= 100 ? 16 : 100;
  progress.setAttribute('aria-valuenow', String(next));
  progressFill.style.width = `${next}%`;
  progressValue.textContent = `${next}%`;
  ui?.announce(`完成度 ${next}%`);
  ui?.showToast({ message: `完成度已更新为 ${next}%`, kind: 'success' });
});

document.getElementById('toastButton').addEventListener('click', () => {
  ui?.showToast({ message: '插件内容区反馈已就绪。', kind: 'info', duration: 0 });
});

document.getElementById('dialogButton').addEventListener('click', async () => {
  const accepted = await ui?.confirm({
    title: '确认示例操作',
    message: '确认框会串行显示，并在关闭后恢复触发按钮焦点。',
    confirmLabel: '确认',
    cancelLabel: '取消'
  });
  ui?.showToast({
    message: accepted ? '已确认示例操作。' : '已取消示例操作。',
    kind: accepted ? 'success' : 'info'
  });
});

renderState('empty');
