// ==UserScript==
// @name         PoE Faustus Auto-Travel (Live Search)
// @namespace    https://github.com/DaniiilPA/faustusbuyer
// @version      1.5
// @description  Автоматически кликает 'Travel to Hideout' ТОЛЬКО для новых лотов в Live Search с передачей цели в плагин
// @author       Antigravity
// @match        *://*.pathofexile.com/trade*
// @match        *://*.pathofexile.com/trade2*
// @match        *://pathofexile.com/trade*
// @match        *://pathofexile.com/trade2*
// @run-at       document-idle
// @grant        GM_setClipboard
// ==/UserScript==

(function () {
    'use strict';

    console.log('[FaustusAutoBuyer] Скрипт загружен на странице trade');

    let isEnabled = true;
    let isTraveling = false; // Блокировка от повторных кликов, пока идет телепорт
    let minDelayMs = 60;
    let maxDelayMs = 180;
    let travelCooldownMs = 15000; // 15 секунд кулдаун после клика, чтобы дать время на выкуп в игре

    // Звуковой сигнал при срабатывании выкупа
    function playBeep() {
        try {
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.frequency.value = 880; // нота A5
            gain.gain.value = 0.2;
            osc.start();
            setTimeout(() => { osc.stop(); ctx.close(); }, 140);
        } catch (e) { }
    }

    // Помечаем все УЖЕ существующие кнопки на странице как старые, чтобы не кликать по ним
    function markExistingButtonsAsSeen() {
        const buttons = document.querySelectorAll('button, a.btn');
        let count = 0;
        buttons.forEach(btn => {
            const text = (btn.innerText || btn.textContent || '').trim();
            if (isTravelButtonText(text, btn)) {
                btn.dataset.autoClicked = 'true';
                count++;
            }
        });
        console.log(`[FaustusAutoBuyer] Инициализация: пропущено ${count} старых лотов. Ожидаем новые!`);
    }

    function isTravelButtonText(text, btn) {
        if (!text && !btn) return false;
        const lower = text.toLowerCase();
        return lower.includes('travel to hideout') ||
               lower.includes('travel') ||
               lower.includes('отправиться в убежище') ||
               lower.includes('убежище') ||
               (btn.classList && btn.classList.contains('btn-travel')) ||
               btn.getAttribute('data-action') === 'travel';
    }

    // Плавающий виджет управления
    function createFloatingToggle() {
        if (document.getElementById('faustus-auto-panel')) return;

        const panel = document.createElement('div');
        panel.id = 'faustus-auto-panel';
        panel.style.cssText = `
            position: fixed !important;
            top: 70px !important;
            right: 20px !important;
            z-index: 2147483647 !important;
            background: rgba(18, 18, 18, 0.95) !important;
            border: 2px solid #af8f42 !important;
            border-radius: 6px !important;
            padding: 8px 14px !important;
            color: #f1f1f1 !important;
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
            font-size: 13px !important;
            display: flex !important;
            align-items: center !important;
            gap: 12px !important;
            box-shadow: 0 4px 16px rgba(0,0,0,0.7) !important;
            cursor: pointer !important;
            user-select: none !important;
        `;

        function updateUI() {
            let statusText = isEnabled ? (isTraveling ? 'ВЫКУП...' : 'АКТИВЕН') : 'ПАУЗА';
            let statusColor = isEnabled ? (isTraveling ? '#f39c12' : '#2ecc71') : '#e74c3c';

            panel.innerHTML = `
                <span style="display:inline-block; width:10px; height:10px; border-radius:50%; background:${statusColor}; box-shadow: 0 0 6px ${statusColor};"></span>
                <strong>Auto-Travel:</strong> <span style="color:${statusColor}; font-weight:bold;">${statusText}</span>
            `;
        }

        panel.addEventListener('click', () => {
            isEnabled = !isEnabled;
            isTraveling = false;
            updateUI();
        });

        window.updateFaustusUI = updateUI;
        updateUI();

        if (document.body) {
            document.body.appendChild(panel);
            console.log('[FaustusAutoBuyer] Плавающий виджет успешно добавлен на экран');
        } else {
            document.addEventListener('DOMContentLoaded', () => {
                document.body.appendChild(panel);
                console.log('[FaustusAutoBuyer] Плавающий виджет добавлен после DOMContentLoaded');
            });
        }
    }

    // Извлечение имени предмета из строки/карточки лота
    function extractItemName(btn) {
        const row = btn.closest('.row, .result-item, .item-card, .result') || btn.parentElement?.closest('.row');
        if (!row) return '';

        const itemHeader = row.querySelector('.itemHeader') || row;
        const nameEl = itemHeader.querySelector('.itemName');
        const typeEl = itemHeader.querySelector('.typeLine');

        let namePart = nameEl ? (nameEl.innerText || nameEl.textContent || '').trim() : '';
        let typePart = typeEl ? (typeEl.innerText || typeEl.textContent || '').trim() : '';

        let targetName = '';
        if (namePart && typePart) {
            targetName = `${namePart} ${typePart}`;
        } else if (namePart) {
            targetName = namePart;
        } else if (typePart) {
            targetName = typePart;
        } else {
            const fallbackEl = row.querySelector('.title, .item-name, [data-field="name"]');
            if (fallbackEl) targetName = (fallbackEl.innerText || fallbackEl.textContent || '').trim();
        }

        return targetName.replace(/\s+/g, ' ').trim();
    }

    // Запись в буфер обмена с обходом ограничений фокуса через GM_setClipboard
    function copyTargetToClipboard(targetName) {
        if (!targetName) return;
        const payload = `FAUSTUS_TARGET:${targetName}`;

        if (typeof GM_setClipboard === 'function') {
            GM_setClipboard(payload, 'text');
            console.log(`[FaustusAutoBuyer] Скопировано в буфер через GM_setClipboard: '${payload}'`);
        } else if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(payload).catch(() => {});
        }
    }

    // Поиск и клик по НОВЫМ кнопкам 'Travel to Hideout'
    function processNewResults() {
        if (!isEnabled || isTraveling) return;

        const buttons = document.querySelectorAll('button, a.btn');

        for (const btn of buttons) {
            if (btn.dataset.autoClicked === 'true') continue;

            const text = (btn.innerText || btn.textContent || '').trim();
            if (isTravelButtonText(text, btn)) {
                btn.dataset.autoClicked = 'true';
                isTraveling = true;
                if (window.updateFaustusUI) window.updateFaustusUI();

                const targetItemName = extractItemName(btn);
                copyTargetToClipboard(targetItemName);

                const delay = Math.floor(Math.random() * (maxDelayMs - minDelayMs + 1)) + minDelayMs;
                console.log(`[FaustusAutoBuyer] 🚀 Обнаружен новый лот: '${targetItemName}'! Клик через ${delay}мс...`);

                setTimeout(() => {
                    btn.click();
                    playBeep();
                    console.log('[FaustusAutoBuyer] Клик Travel to Hideout выполнен. Ожидание завершения сделки в игре...');

                    // Разблокируем возможность следующего выкупа через кулдаун
                    setTimeout(() => {
                        isTraveling = false;
                        if (window.updateFaustusUI) window.updateFaustusUI();
                    }, travelCooldownMs);
                }, delay);

                break; // Выкупаем строго по одному лоту за раз
            }
        }
    }

    // Запуск слушателя
    function start() {
        createFloatingToggle();
        setTimeout(() => {
            markExistingButtonsAsSeen();

            let throttleTimer = null;
            const observer = new MutationObserver(() => {
                if (throttleTimer) return;
                throttleTimer = setTimeout(() => {
                    throttleTimer = null;
                    processNewResults();
                }, 40);
            });

            if (document.body) {
                observer.observe(document.body, { childList: true, subtree: true });
            }
            console.log('[FaustusAutoBuyer] v1.5: Слушатель Live Search готов к работе!');
        }, 1000);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
