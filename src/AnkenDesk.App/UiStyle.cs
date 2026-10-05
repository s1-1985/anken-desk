using System.Drawing;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>画面の見た目（HANDOFF.md §5.5）。操作ボタンの高さは40px以上、状態は色だけでなく文字も添える。</summary>
    internal static class UiStyle
    {
        public static readonly Color Primary = ColorTranslator.FromHtml("#1D5C78");
        public static readonly Color Background = ColorTranslator.FromHtml("#F2F3F0");
        public static readonly Color Text = ColorTranslator.FromHtml("#1E2428");
        public static readonly Color Danger = ColorTranslator.FromHtml("#B3261E");

        public const int ButtonHeight = 40;

        public static void Apply(Form form)
        {
            form.Font = new Font("BIZ UDPGothic", 11F);
            form.BackColor = Background;
            form.ForeColor = Text;
            form.StartPosition = FormStartPosition.CenterParent;
        }

        public static Button CreateButton(string text, bool primary, int width)
        {
            var b = new Button
            {
                Text = text,
                Width = width,
                Height = ButtonHeight,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Primary : Color.White,
                ForeColor = primary ? Color.White : Text,
                Margin = new Padding(4),
            };
            b.FlatAppearance.BorderColor = Primary;
            return b;
        }
    }
}
