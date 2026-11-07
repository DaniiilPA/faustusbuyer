using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace faustusbuyer
{
    public class PluginSettings : ISettings
    {
        public ToggleNode Enable { get; set; } = new ToggleNode(false);
        public HotkeyNode ToggleHotkey { get; set; } = new HotkeyNode(Keys.F5);
        public HotkeyNode SetHomeHotkey { get; set; } = new HotkeyNode(Keys.F6); // Запомнить текущую зону как родной Hideout

        // --- ВЕРИФИКАЦИЯ ПРЕДМЕТА (ЗАЩИТА ОТ ПОДМЕНЫ / СКАМА) ---
        public ToggleNode VerifyTargetName { get; set; } = new ToggleNode(true);
        public TextNode ManualTargetName { get; set; } = new TextNode(""); // Ручной фильтр (если нужно снайпить конкретный предмет)

        // --- НАСТРОЙКИ ПОКУПКИ ---
        public ToggleNode UseCtrlClick { get; set; } = new ToggleNode(true); // Ctrl + Click для быстрой покупки в инвентарь

        // --- АНТИФРОД И ЗАДЕРЖКИ (ИМИТАЦИЯ ЧЕЛОВЕКА) ---
        public RangeNode<int> MinReactionDelayMs { get; set; } = new RangeNode<int>(120, 40, 500);
        public RangeNode<int> MaxReactionDelayMs { get; set; } = new RangeNode<int>(260, 80, 1000);

        // --- ТАЙМАУТЫ И ЗАЩИТА ---
        public RangeNode<int> ShopOpenTimeoutMs { get; set; } = new RangeNode<int>(2200, 500, 5000);
        public RangeNode<int> FailsafeMaxTimeInForeignHideoutMs { get; set; } = new RangeNode<int>(5000, 2000, 10000);

        // --- ВОЗВРАТ ДОМОЙ ---
        public ToggleNode AutoReturnHome { get; set; } = new ToggleNode(true);
        public RangeNode<int> ReturnHomeDelayMs { get; set; } = new RangeNode<int>(350, 100, 2000);
    }
}
