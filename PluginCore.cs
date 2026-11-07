using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using SharpDX;
using RectangleF = SharpDX.RectangleF;
using Color = SharpDX.Color;
using Vector2 = System.Numerics.Vector2;

namespace faustusbuyer
{
    public enum BuyerState
    {
        Idle,
        WaitingForZoneLoad,
        WaitingForShopOpen,
        BuyingItem,
        CheckingConfirmationPopup,
        ReturningHome,
        WaitingForReturnLoad
    }

    public class FaustusBuyerPlugin : BaseSettingsPlugin<PluginSettings>
    {
        private InputController _input = null!;
        private BuyerState _state = BuyerState.Idle;
        private string _status = "Инициализация";
        private uint _homeAreaHash = 0;

        private readonly Stopwatch _stateTimer = new Stopwatch();
        private readonly Stopwatch _failsafeTimer = new Stopwatch();
        private readonly Random _random = new Random();

        private Vector2 _targetClickPos = Vector2.Zero;
        private int _currentReactionDelayTargetMs = 150;
        private bool _hasPurchased = false;
        private string _expectedTargetName = string.Empty;

        // Кэшированные задержки (фиксируются при входе в состояние)
        private int _cachedLoadJitterMs = 200;
        private int _cachedReturnDelayMs = 400;
        private int _cachedSubDelayMs = 80;
        private int _returnStep = 0;

        public override bool Initialise()
        {
            _input = new InputController(GameController);

            if (GameController?.Area?.CurrentArea != null)
            {
                _homeAreaHash = GameController.Area.CurrentArea.Hash;
            }

            _state = BuyerState.Idle;
            _status = "Ожидание в своем убежище";
            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            if (area == null) return;

            _stateTimer.Restart();

            if (_homeAreaHash == 0)
            {
                _homeAreaHash = area.Hash;
            }

            // Если мы ожидали возврат домой, фиксируем прибытие и обновляем хеш дома
            if (_state == BuyerState.WaitingForReturnLoad || _state == BuyerState.ReturningHome)
            {
                _homeAreaHash = area.Hash;
                _state = BuyerState.Idle;
                _failsafeTimer.Stop();
                _hasPurchased = false;
                _expectedTargetName = string.Empty;
                _status = "Успешно вернулись домой. Ожидание выкупа в браузере";
                return;
            }

            if (area.Hash != _homeAreaHash)
            {
                // Загрузились в чужую зону (убежище продавца)
                _state = BuyerState.WaitingForZoneLoad;
                _failsafeTimer.Restart();
                _hasPurchased = false;
                _cachedLoadJitterMs = _random.Next(150, 280);

                // Считываем целевое имя из буфера обмена (если передал скрипт браузера)
                _expectedTargetName = ResolveExpectedTargetName();

                _status = string.IsNullOrEmpty(_expectedTargetName)
                    ? "Прибыли в зону продавца. Ожидание витрины..."
                    : $"Прибыли за: '{_expectedTargetName}'. Ожидание витрины...";
            }
            else
            {
                // Вернулись в свое убежище
                _state = BuyerState.Idle;
                _failsafeTimer.Stop();
                _hasPurchased = false;
                _expectedTargetName = string.Empty;
                _status = "Дома. Ожидание выкупа в браузере";
            }
        }

        public override void Render()
        {
            if (Settings.Enable == null || !Settings.Enable.Value)
            {
                _status = "Отключен";
                DrawDebug();
                return;
            }

            // Переключение активности F5
            if (Settings.ToggleHotkey != null && Settings.ToggleHotkey.PressedOnce())
            {
                Settings.Enable.Value = !Settings.Enable.Value;
            }

            // Перекалибровка дома F6
            if (Settings.SetHomeHotkey != null && Settings.SetHomeHotkey.PressedOnce())
            {
                if (GameController?.Area?.CurrentArea != null)
                {
                    _homeAreaHash = GameController.Area.CurrentArea.Hash;
                    _status = $"Текущая зона установлена как ДОМ (Hash: {_homeAreaHash})";
                }
            }

            UpdateStateMachine();
            DrawDebug();
        }

        private void UpdateStateMachine()
        {
            if (GameController.IsLoading)
            {
                _status = "Загрузка экрана...";
                return;
            }

            var player = GameController.Player;
            if (player == null || !player.IsValid)
            {
                return;
            }

            // Защита от зависания в чужом хайдауте (Failsafe)
            if (_state != BuyerState.Idle && _failsafeTimer.IsRunning)
            {
                if (_failsafeTimer.ElapsedMilliseconds > Settings.FailsafeMaxTimeInForeignHideoutMs.Value)
                {
                    _status = "Превышен лимит времени в чужом хайдауте. Аварийный возврат домой...";
                    InitiateReturnHome();
                    return;
                }
            }

            switch (_state)
            {
                case BuyerState.Idle:
                    break;

                case BuyerState.WaitingForZoneLoad:
                    // Небольшая пауза после загрузки для стабилизации UI
                    if (_stateTimer.ElapsedMilliseconds > _cachedLoadJitterMs)
                    {
                        _state = BuyerState.WaitingForShopOpen;
                        _stateTimer.Restart();
                        _status = "Ожидание открытия витрины магазина...";
                    }
                    break;

                case BuyerState.WaitingForShopOpen:
                    var shopElement = FindShopWindow();
                    if (shopElement != null)
                    {
                        var targetItem = FindMatchingItem(shopElement, _expectedTargetName);
                        if (targetItem != null)
                        {
                            var rect = targetItem.GetClientRect();
                            if (rect != RectangleF.Empty && rect.Width > 15 && rect.Height > 15)
                            {
                                _targetClickPos = new Vector2(rect.Center.X, rect.Center.Y);

                                _currentReactionDelayTargetMs = _random.Next(
                                    Settings.MinReactionDelayMs.Value, 
                                    Settings.MaxReactionDelayMs.Value + 1
                                );

                                _state = BuyerState.BuyingItem;
                                _stateTimer.Restart();
                                _status = $"Товар верифицирован! Пауза реакции ({_currentReactionDelayTargetMs}мс)...";
                                return;
                            }
                        }
                    }

                    // Таймаут: если за отведенное время окно не открылось или товара нет (уже купили)
                    if (_stateTimer.ElapsedMilliseconds > Settings.ShopOpenTimeoutMs.Value)
                    {
                        _status = "Товар уже куплен или не прошел верификацию. Возврат домой...";
                        InitiateReturnHome();
                    }
                    break;

                case BuyerState.BuyingItem:
                    if (_stateTimer.ElapsedMilliseconds >= _currentReactionDelayTargetMs)
                    {
                        _status = "Выполняем покупку предмета...";
                        
                        bool clickSuccess = _input.ClickScreenPos(_targetClickPos, Settings.UseCtrlClick.Value);

                        if (clickSuccess)
                        {
                            _hasPurchased = true;
                            _state = BuyerState.CheckingConfirmationPopup;
                            _stateTimer.Restart();
                        }
                        else
                        {
                            _status = "Ошибка клика. Возврат домой...";
                            InitiateReturnHome();
                        }
                    }
                    break;

                case BuyerState.CheckingConfirmationPopup:
                    // Ждем модального окна "Confirm"
                    var confirmBtn = FindConfirmationButton();
                    if (confirmBtn != null)
                    {
                        var btnRect = confirmBtn.GetClientRect();
                        if (btnRect != RectangleF.Empty)
                        {
                            _status = "Подтверждаем диалог покупки (Confirm)...";
                            _input.ClickScreenPos(new Vector2(btnRect.Center.X, btnRect.Center.Y), false);
                        }
                    }

                    if (_stateTimer.ElapsedMilliseconds > 350)
                    {
                        _status = _hasPurchased 
                            ? "Покупка завершена! Закрываем окно и летим домой..."
                            : "Возврат домой...";
                        InitiateReturnHome();
                    }
                    break;

                case BuyerState.ReturningHome:
                    if (!Settings.AutoReturnHome.Value)
                    {
                        _state = BuyerState.Idle;
                        _failsafeTimer.Stop();
                        break;
                    }
                    switch (_returnStep)
                    {
                        case 0: // Ожидание начальной задержки, затем первый Escape
                            if (_stateTimer.ElapsedMilliseconds >= _cachedReturnDelayMs)
                            {
                                _status = "Закрываем витрину (Escape 1)...";
                                _input.PressEscape();
                                _returnStep = 1;
                                _cachedSubDelayMs = _random.Next(60, 110);
                                _stateTimer.Restart();
                            }
                            break;
                        case 1: // Второй Escape (на случай tooltip)
                            if (_stateTimer.ElapsedMilliseconds >= _cachedSubDelayMs)
                            {
                                _input.PressEscape();
                                _returnStep = 2;
                                _cachedSubDelayMs = _random.Next(60, 110);
                                _stateTimer.Restart();
                            }
                            break;
                        case 2: // Отправка /hideout
                            if (_stateTimer.ElapsedMilliseconds >= _cachedSubDelayMs)
                            {
                                _status = "Отправляем /hideout...";
                                _input.SendChatCommand("/hideout");
                                _returnStep = 0;
                                _state = BuyerState.WaitingForReturnLoad;
                                _stateTimer.Restart();
                            }
                            break;
                    }
                    break;

                case BuyerState.WaitingForReturnLoad:
                    if (_stateTimer.ElapsedMilliseconds > 3500)
                    {
                        _input.SendChatCommand("/hideout");
                        _stateTimer.Restart();
                    }
                    break;
            }
        }

        private void InitiateReturnHome()
        {
            _state = BuyerState.ReturningHome;
            _returnStep = 0;
            _cachedReturnDelayMs = Settings.ReturnHomeDelayMs.Value + _random.Next(40, 100);
            _stateTimer.Restart();
        }

        private string ResolveExpectedTargetName()
        {
            // 1. Ручная настройка в плагине (если задана)
            if (!string.IsNullOrWhiteSpace(Settings.ManualTargetName?.Value))
            {
                return Settings.ManualTargetName.Value.Trim();
            }

            // 2. Чтение из буфера обмена (если браузерный скрипт передал FAUSTUS_TARGET:Name)
            string clip = _input.ReadClipboardText();
            if (!string.IsNullOrEmpty(clip) && clip.StartsWith("FAUSTUS_TARGET:", StringComparison.OrdinalIgnoreCase))
            {
                string target = clip.Substring("FAUSTUS_TARGET:".Length).Trim();
                _input.ClearClipboard(); // Очищаем буфер после чтения
                return target;
            }

            return string.Empty;
        }

        private Element? FindShopWindow()
        {
            var ingameUi = GameController.IngameState?.IngameUi;
            if (ingameUi == null) return null;

            // 1. Панель офлайн-торговца / витрины Фостуса (OfflineMerchantPanel)
            if (ingameUi.OfflineMerchantPanel != null && ingameUi.OfflineMerchantPanel.IsVisible)
            {
                return ingameUi.OfflineMerchantPanel;
            }

            // 2. Окно покупки у торговца (PurchaseWindow)
            if (ingameUi.PurchaseWindow != null && ingameUi.PurchaseWindow.IsVisible)
            {
                return ingameUi.PurchaseWindow;
            }

            // 3. Стандартная вкладка Stash / Merchant
            if (ingameUi.StashElement != null && ingameUi.StashElement.IsVisible)
            {
                return ingameUi.StashElement;
            }

            // 4. Fallback: поиск крупного левого окна в UI
            var windowRect = GameController.Window.GetWindowRectangle();
            if (ingameUi.Children != null)
            {
                foreach (var child in ingameUi.Children)
                {
                    if (child != null && child.IsVisible)
                    {
                        var rect = child.GetClientRect();
                        if (rect.Width >= 380 && rect.Height >= 350 && rect.X < windowRect.Width * 0.75f)
                        {
                            return child;
                        }
                    }
                }
            }

            return null;
        }

        private Element? FindMatchingItem(Element shopWindow, string expectedName)
        {
            if (shopWindow == null) return null;

            var items = GetShopInventoryItems(shopWindow);
            if (items != null && items.Count > 0)
            {
                // 1. Если задано целевое имя, ищем строгое совпадение по сущности предмета
                if (Settings.VerifyTargetName.Value && !string.IsNullOrEmpty(expectedName))
                {
                    foreach (var invItem in items)
                    {
                        if (invItem?.Item == null || !invItem.IsVisible) continue;
                        if (IsItemMatchingName(invItem.Item, expectedName))
                        {
                            return invItem;
                        }
                    }

                    // Если целевое имя задано, но пока не найдено — ждем загрузки или таймаута
                    return null;
                }

                // 2. Поиск по нативной подсветке игры (когда имя не задано или проверка выключена)
                foreach (var invItem in items)
                {
                    if (invItem != null && invItem.IsVisible && HasHighlight(invItem))
                    {
                        return invItem;
                    }
                }

                // 3. Fallback: если во вкладке продавца всего один предмет
                if (items.Count == 1 && items[0] != null && items[0].IsVisible)
                {
                    return items[0];
                }
            }

            // Если проверка по имени активна — строгий запрет на слепой рекурсивный поиск
            if (Settings.VerifyTargetName.Value && !string.IsNullOrEmpty(expectedName))
            {
                return null;
            }

            return FindHighlightedChildRecursive(shopWindow, 0);
        }

        private System.Collections.Generic.List<NormalInventoryItem> GetShopInventoryItems(Element shopWindow)
        {
            var list = new System.Collections.Generic.List<NormalInventoryItem>();
            if (shopWindow == null) return list;

            var ingameUi = GameController.IngameState?.IngameUi;

            // 1. OfflineMerchantPanel
            if (ingameUi?.OfflineMerchantPanel != null && ingameUi.OfflineMerchantPanel.IsVisible)
            {
                var stashItems = ingameUi.OfflineMerchantPanel.VisibleStash?.VisibleInventoryItems;
                if (stashItems != null && stashItems.Count > 0)
                {
                    list.AddRange(stashItems);
                    return list;
                }
            }

            // 2. PurchaseWindow
            if (ingameUi?.PurchaseWindow != null && ingameUi.PurchaseWindow.IsVisible)
            {
                var purchaseItems = ingameUi.PurchaseWindow.TabContainer?.VisibleStash?.VisibleInventoryItems;
                if (purchaseItems != null && purchaseItems.Count > 0)
                {
                    list.AddRange(purchaseItems);
                    return list;
                }
            }

            // 3. StashElement
            if (ingameUi?.StashElement != null && ingameUi.StashElement.IsVisible)
            {
                var stashItems = ingameUi.StashElement.VisibleStash?.VisibleInventoryItems;
                if (stashItems != null && stashItems.Count > 0)
                {
                    list.AddRange(stashItems);
                    return list;
                }
            }

            // 4. Рекурсивный сбор элементов NormalInventoryItem из окна
            CollectInventoryItemsRecursive(shopWindow, list, 0);
            return list;
        }

        private void CollectInventoryItemsRecursive(Element root, System.Collections.Generic.List<NormalInventoryItem> results, int depth)
        {
            if (root == null || !root.IsVisible || depth > 6) return;

            if (root is NormalInventoryItem invItem && invItem.Item != null)
            {
                results.Add(invItem);
                return;
            }

            if (root.Children != null)
            {
                foreach (var child in root.Children)
                {
                    CollectInventoryItemsRecursive(child, results, depth + 1);
                }
            }
        }

        private bool IsItemMatchingName(Entity itemEntity, string expectedName)
        {
            if (itemEntity == null || string.IsNullOrWhiteSpace(expectedName)) return false;

            var modsComp = itemEntity.GetComponent<Mods>();
            var baseComp = itemEntity.GetComponent<Base>();
            string uniqueName = modsComp?.UniqueName?.Trim() ?? string.Empty;
            string renderName = itemEntity.RenderName?.Trim() ?? string.Empty;
            string baseName = baseComp?.Name?.Trim() ?? string.Empty;
            string path = itemEntity.Path ?? string.Empty;

            expectedName = expectedName.Trim();

            // 1. Проверка уникального названия (например "Headhunter" <-> "Headhunter Leather Belt")
            if (!string.IsNullOrEmpty(uniqueName))
            {
                if (uniqueName.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    expectedName.IndexOf(uniqueName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // 2. Проверка RenderName
            if (!string.IsNullOrEmpty(renderName))
            {
                if (renderName.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    expectedName.IndexOf(renderName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // 3. Проверка базового типа (например "Leather Belt" или "Divine Orb")
            if (!string.IsNullOrEmpty(baseName))
            {
                if (baseName.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    expectedName.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // 4. Проверка по внутреннему пути ассета
            if (!string.IsNullOrEmpty(path))
            {
                string cleanExpected = expectedName.Replace(" ", "");
                if (path.IndexOf(cleanExpected, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasHighlight(NormalInventoryItem invItem)
        {
            if (invItem == null) return false;

            // 1. Нативные флаги подсветки ExileCore
            if (invItem.isHighlighted || invItem.HasShinyHighlight)
                return true;

            // 2. Проверка дочерних элементов
            if (invItem.Children != null && invItem.Children.Count > 0)
            {
                var itemRect = invItem.GetClientRect();
                foreach (var child in invItem.Children)
                {
                    if (child != null && child.IsVisible)
                    {
                        if (child.isHighlighted || child.HasShinyHighlight)
                            return true;

                        var childRect = child.GetClientRect();
                        if (Math.Abs(childRect.Width - itemRect.Width) <= 18 && 
                            Math.Abs(childRect.Height - itemRect.Height) <= 18)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private Element? FindHighlightedChildRecursive(Element root, int depth)
        {
            if (root == null || !root.IsVisible || depth > 5) return null;

            if (root.isHighlighted || root.HasShinyHighlight)
            {
                return root;
            }

            if (root.Children != null)
            {
                foreach (var child in root.Children)
                {
                    var found = FindHighlightedChildRecursive(child, depth + 1);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private Element? FindConfirmationButton()
        {
            var ingameUi = GameController.IngameState?.IngameUi;
            if (ingameUi == null) return null;

            string[] confirmKeywords = { "accept", "confirm", "yes", "принять", "купить", "подтвердить" };
            return FindButtonWithTextRecursive(ingameUi, confirmKeywords, 0);
        }

        private Element? FindButtonWithTextRecursive(Element root, string[] keywords, int depth)
        {
            if (root == null || !root.IsVisible || depth > 5) return null;

            string text = (root.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                string lower = text.ToLowerInvariant();
                if (keywords.Any(k => lower.Equals(k, StringComparison.OrdinalIgnoreCase) || lower.Contains(k)))
                {
                    var rect = root.GetClientRect();
                    if (rect.Width > 20 && rect.Height > 12)
                    {
                        return root;
                    }
                }
            }

            if (root.Children != null)
            {
                foreach (var child in root.Children)
                {
                    var found = FindButtonWithTextRecursive(child, keywords, depth + 1);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private void DrawDebug()
        {
            var drawPos = new Vector2(100, 140);
            var textColor = (_state == BuyerState.BuyingItem || _state == BuyerState.WaitingForShopOpen) 
                ? Color.LimeGreen 
                : Color.Gold;

            string targetInfo = string.IsNullOrEmpty(_expectedTargetName) ? "Любой подсвеченный" : _expectedTargetName;
            string text = $"[FaustusBuyer] Состояние: {_state} | Цель: [{targetInfo}] | {_status}";
            Graphics.DrawText(text, drawPos, textColor, FontAlign.Left);

            if (_targetClickPos != Vector2.Zero && _state == BuyerState.BuyingItem)
            {
                Graphics.DrawFrame(new RectangleF(_targetClickPos.X - 15, _targetClickPos.Y - 15, 30, 30), Color.Cyan, 2);
            }
        }
    }
}
