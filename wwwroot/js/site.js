document.addEventListener('DOMContentLoaded', () => {
    const sidebar = document.getElementById('appSidebar');
    const backdrop = document.getElementById('mobileBackdrop');
    const toggle = document.getElementById('sidebarToggle');
    const closeSidebar = () => { sidebar?.classList.remove('open'); backdrop?.classList.remove('show'); };
    toggle?.addEventListener('click', () => { sidebar?.classList.toggle('open'); backdrop?.classList.toggle('show'); });
    backdrop?.addEventListener('click', closeSidebar);
    sidebar?.querySelectorAll('a').forEach(link => link.addEventListener('click', closeSidebar));
    document.querySelectorAll('[data-dismiss-alert]').forEach(button => button.addEventListener('click', () => button.closest('.app-alert')?.remove()));
});

function drawAutoCareBarChart(canvasId, labels, values, colour) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ratio = window.devicePixelRatio || 1;
    const width = Math.max(620, canvas.parentElement.clientWidth - 4);
    const height = 300;
    canvas.width = width * ratio; canvas.height = height * ratio;
    canvas.style.width = width + 'px'; canvas.style.height = height + 'px';
    const ctx = canvas.getContext('2d'); ctx.scale(ratio, ratio);
    const left = 58, right = 18, top = 18, bottom = 48;
    const chartWidth = width - left - right, chartHeight = height - top - bottom;
    const maximum = Math.max(...values.map(Number), 1);
    ctx.font = '12px Segoe UI, Arial'; ctx.fillStyle = '#64748b'; ctx.strokeStyle = '#e3e9f1'; ctx.lineWidth = 1;
    for (let line = 0; line <= 4; line++) { const y = top + chartHeight * line / 4; ctx.beginPath(); ctx.moveTo(left, y); ctx.lineTo(width - right, y); ctx.stroke(); const amount = maximum * (4 - line) / 4; ctx.fillText('RM ' + amount.toFixed(0), 2, y + 4); }
    const slot = chartWidth / Math.max(labels.length, 1), barWidth = Math.min(54, slot * .58);
    labels.forEach((label, index) => { const value = Number(values[index] || 0); const barHeight = chartHeight * value / maximum; const x = left + slot * index + (slot - barWidth) / 2; const y = top + chartHeight - barHeight; ctx.fillStyle = colour; ctx.fillRect(x, y, barWidth, barHeight); ctx.fillStyle = '#142033'; ctx.textAlign = 'center'; ctx.fillText('RM ' + value.toFixed(0), x + barWidth / 2, Math.max(top + 12, y - 6)); ctx.fillStyle = '#64748b'; ctx.fillText(label, x + barWidth / 2, height - 18); });
    ctx.textAlign = 'start';
}
