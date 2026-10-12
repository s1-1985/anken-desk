using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>
    /// 別窓として作ってあるFormを、本体の画面の中（パネルやタブ）に表示するための共通部品。
    /// 窓の枠をなくし、親いっぱいに広げる。「閉じる」ボタンは、本体の中では要らないので隠す
    /// （Escなどで Close() されたときは、FormClosed で親が元の表示に戻す）。
    /// </summary>
    internal static class EmbeddedForm
    {
        /// <summary>親のControlsに足す前に呼ぶ。</summary>
        public static void Prepare(Form f)
        {
            f.TopLevel = false;
            f.FormBorderStyle = FormBorderStyle.None;
            f.Dock = DockStyle.Fill;
            f.AutoScroll = true; // 親が小さいときは、スクロールで全部の操作ができる
            HideCloseButtons(f);
        }

        /// <summary>フォームを親に入れて表示する。</summary>
        public static void Host(Control parent, Form f)
        {
            Prepare(f);
            parent.Controls.Add(f);
            f.Show();
        }

        private static void HideCloseButtons(Control c)
        {
            foreach (Control child in c.Controls)
            {
                var b = child as Button;
                if (b != null && b.Text == "閉じる")
                {
                    b.Visible = false;
                }

                HideCloseButtons(child);
            }
        }
    }
}
