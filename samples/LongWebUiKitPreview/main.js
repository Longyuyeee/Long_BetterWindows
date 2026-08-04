const ui = window.LongUI;
const versionBadge = document.getElementById('versionBadge');
const statePreview = document.getElementById('statePreview');
const dialog = document.getElementById('confirmDialog');
const progress = document.getElementById('progress');
const progressFill = document.getElementById('progressFill');
const progressValue = document.getElementById('progressValue');

versionBadge.textContent = `UI Kit ${ui?.version ?? 'unavailable'}`;
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
});

document.getElementById('dialogButton').addEventListener('click', () => dialog.showModal());
document.getElementById('cancelButton').addEventListener('click', () => dialog.close('cancel'));
document.getElementById('confirmButton').addEventListener('click', () => dialog.close('confirm'));

renderState('empty');
