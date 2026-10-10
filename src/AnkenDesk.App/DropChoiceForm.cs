using System.Drawing;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>Outlookのメールをホームに落としたとき、「新しい案件として登録」か「既存の案件に取り込む」かを選ぶ。</summary>
    internal sealed class DropChoiceForm : Form
    {
        public enum Choice
        {
            None,
            NewAnken,
            ExistingAnken,
        }

        public Choice Selected { get; private set; } = Choice.None;

        public DropChoiceForm()
        {
            UiStyle.Apply(this);
            Text = "メールをどうしますか";
            ClientSize = new Size(640, 330);

            Controls.Add(new Label { Text = "Outlookのメールを受け取りました。どうしますか？", Font = new Font("BIZ UDPGothic", 13F, FontStyle.Bold), Left = 16, Top = 14, Width = 608, Height = 36 });

            var a = UiStyle.CreateButton("新しい案件として登録する（客先からの依頼メール）", true, 608);
            a.Left = 16; a.Top = 64; a.Height = 56;
            a.Click += (s, e) => { Selected = Choice.NewAnken; DialogResult = DialogResult.OK; };
            Controls.Add(new Label { Text = "件名から品番の候補を拾い、依頼日・添付（1.と2.へ振り分け）・.msg・本文のメモまで、まとめて登録します。", Left = 16, Top = 124, Width = 608, Height = 48, ForeColor = Color.FromArgb(90, 96, 100) });

            var b = UiStyle.CreateButton("既存の案件に取り込む（調達先の回答・客先とのやり取り）", false, 608);
            b.Left = 16; b.Top = 180; b.Height = 56;
            b.Click += (s, e) => { Selected = Choice.ExistingAnken; DialogResult = DialogResult.OK; };
            Controls.Add(new Label { Text = "案件の候補を件名の品番から推定して出します。調達先の回答か、客先とのやり取りかを選べます。", Left = 16, Top = 240, Width = 608, Height = 48, ForeColor = Color.FromArgb(90, 96, 100) });

            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 484; cancel.Top = 280;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
        }
    }
}
