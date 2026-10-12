using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// 催促が要る案件（見積依頼を送ったのに未回答の調達先があり、期限が明日以前）をまとめて出し、
    /// 選んだ案件の催促メールの画面を順に開く。送信は、1通ごとの確認画面で人が決める。
    /// </summary>
    internal sealed class ReminderBatchForm : Form
    {
        private readonly AppServices _services;
        private readonly IReadOnlyList<HomeRow> _rows;
        private readonly CheckedListBox _list = new CheckedListBox();

        public ReminderBatchForm(AppServices services, IReadOnlyList<HomeRow> rows)
        {
            _services = services;
            _rows = rows;
            UiStyle.Apply(this);
            Text = "催促が必要な案件";
            ClientSize = new Size(960, 560);

            Controls.Add(new Label
            {
                Text = "催促が必要な案件",
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 10, Width = 600, Height = 40,
            });
            Controls.Add(new Label
            {
                Text = "見積依頼を送ったのに回答が無く、回答期限が明日以前（または超過）の案件です。チェックした案件の催促メールの画面を、1件ずつ開きます。\r\n送るかどうかは、各メールの確認画面で決めます。",
                Left = 16, Top = 52, Width = 928, Height = 50,
                ForeColor = Color.FromArgb(90, 96, 100),
            });

            _list.SetBounds(16, 108, 928, 380);
            _list.CheckOnClick = true;
            _list.HorizontalScrollbar = true;
            foreach (var r in rows)
            {
                var due = Comparison.DueStatus(r.Anken.ReplyDueDate, r.Anken.ReplyDueDate.AddDays(-r.DaysToDue));
                _list.Items.Add(Home.StatusMark(r) + due + "　" + r.Anken.ClientName + "　" + Home.Title(r.Anken) + "　未回答: " + string.Join("、", r.PendingNames), true);
            }

            Controls.Add(_list);

            var open = UiStyle.CreateButton("選んだ案件のメール画面を開く", true, 320);
            open.Left = 470; open.Top = 504;
            open.Click += (s, e) => OpenAll();
            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 804; close.Top = 504;
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { open, close });
            CancelButton = close;
        }

        private void OpenAll()
        {
            var chosen = _list.CheckedIndices.Cast<int>().Select(i => _rows[i]).ToList();
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "案件を選んでください。", "催促が必要な案件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            for (var i = 0; i < chosen.Count; i++)
            {
                var r = chosen[i];
                using (var dlg = new ComposeMailForm(_services, r.Anken, MailKind.Reminder, r.RemindSupplierIds.ToList()))
                {
                    dlg.Text = "催促メール（" + (i + 1) + " / " + chosen.Count + "）　" + Home.Title(r.Anken);
                    dlg.ShowDialog(this);
                }

                if (i + 1 < chosen.Count
                    && MessageBox.Show(this, "次の案件（" + Home.Title(chosen[i + 1].Anken) + "）の催促メールを開きます。\r\n続けますか？", "催促が必要な案件",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    break;
                }
            }

            DialogResult = DialogResult.OK;
        }
    }
}
