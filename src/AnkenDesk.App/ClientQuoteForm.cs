using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>客先向けの見積書（Excel）を作る。提出単価は「提出単価と粗利」で入れたもの。仕入の情報は載せない。</summary>
    internal sealed class ClientQuoteForm : Form
    {
        private readonly AppServices _services;
        private readonly AnkenRecord _anken;
        private readonly TextBox _to = new TextBox();
        private readonly DateTimePicker _date = new DateTimePicker { Format = DateTimePickerFormat.Short };
        private readonly DateTimePicker _valid = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true };
        private readonly TextBox _delivery = new TextBox();
        private readonly TextBox _remarks = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly TextBox _company = new TextBox();
        private readonly TextBox _person = new TextBox();
        private readonly TextBox _contact = new TextBox();
        private readonly ListView _lines = new ListView { View = View.Details, GridLines = true, FullRowSelect = true };
        private IReadOnlyList<ClientQuoteLine> _built = new List<ClientQuoteLine>();

        /// <summary>保存したファイル（保存できたときだけ）。</summary>
        public string? SavedPath { get; private set; }

        public ClientQuoteForm(AppServices services, AnkenRecord anken)
        {
            _services = services;
            _anken = anken;
            UiStyle.Apply(this);
            Text = "客先見積書を作成　" + Home.Title(anken);
            ClientSize = new Size(900, 760);

            Controls.Add(new Label { Text = "客先見積書を作成", Font = new Font("BIZ UDPGothic", 16F, FontStyle.Bold), Left = 16, Top = 10, Width = 500, Height = 38 });
            Controls.Add(new Label
            {
                Text = "「提出単価と粗利」で入れた提出単価から作ります。調達先名・仕入単価・別費用は載せません。書式は汎用です【仮置き】。",
                Left = 16, Top = 48, Width = 868, Height = 26, ForeColor = Color.FromArgb(90, 96, 100),
            });

            var patterns = services.Db.ListQuantities(anken.Id);
            var prices = services.Db.ListClientPrices(anken.Id);
            _built = ClientQuoteSheet.BuildLines(patterns, prices);

            Field("宛名", _to, 16, 84, 520);
            _to.Text = anken.ClientName;
            Field("見積日", _date, 560, 84, 160);
            Field("納期", _delivery, 16, 154, 520);
            _delivery.Text = ClientQuoteSheet.DefaultDelivery(prices, services.Db.ListQuotes(anken.Id));
            Field("有効期限", _valid, 560, 154, 160);
            _valid.Value = DateTime.Today.AddDays(30);
            _valid.Checked = true;

            _lines.SetBounds(16, 250, 868, 134);
            _lines.Columns.Add("数量", 380);
            _lines.Columns.Add("単価", 200);
            _lines.Columns.Add("金額", 220);
            foreach (var l in _built)
            {
                var item = new ListViewItem(l.Pattern);
                item.SubItems.Add(Comparison.FormatPrice(l.UnitPrice));
                item.SubItems.Add(l.Amount.HasValue ? Comparison.FormatPrice(l.Amount.Value) : "（出しません）");
                _lines.Items.Add(item);
            }

            Controls.Add(new Label { Text = "明細（提出単価が入っている数量）", Left = 16, Top = 222, Width = 500, Height = 24 });
            Controls.Add(_lines);
            if (_built.Count == 0)
            {
                Controls.Add(new Label { Text = "提出単価が入っている数量がありません。先に「提出単価と粗利」で入れてください。", Left = 16, Top = 388, Width = 868, Height = 26, ForeColor = UiStyle.Danger, Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold) });
            }

            Field("備考（見積書の下に載せます）", _remarks, 16, 424, 868);
            _remarks.Height = 80;
            _remarks.Text = "価格は税抜です。" + Environment.NewLine + "型・治具などの費用は、別途ご相談です。";
            Field("自社名", _company, 16, 540, 420);
            Field("担当者", _person, 464, 540, 420);
            Field("連絡先", _contact, 16, 610, 868);
            _company.Text = services.CompanyName;
            _person.Text = services.PersonName;
            _contact.Text = services.Contact;

            var ok = UiStyle.CreateButton("Excelを作る", true, 180);
            ok.Left = 524; ok.Top = 696;
            ok.Enabled = _built.Count > 0;
            ok.Click += (s, e) => Save();
            var cancel = UiStyle.CreateButton("キャンセル", false, 160);
            cancel.Left = 716; cancel.Top = 696;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { ok, cancel });
            CancelButton = cancel;
        }

        private void Field(string caption, Control box, int x, int y, int width)
        {
            Controls.Add(new Label { Text = caption, Left = x, Top = y, Width = width, Height = 24 });
            box.SetBounds(x, y + 26, width, 32);
            Controls.Add(box);
        }

        private void Save()
        {
            var input = new ClientQuoteInput
            {
                Anken = _anken,
                ToName = _to.Text.Trim(),
                QuoteDate = _date.Value.Date,
                ValidUntil = _valid.Checked ? _valid.Value.Date : (DateTime?)null,
                Delivery = _delivery.Text.Trim(),
                Remarks = _remarks.Text,
                CompanyName = _company.Text.Trim(),
                PersonName = _person.Text.Trim(),
                Contact = _contact.Text.Trim(),
            };
            input.Lines.AddRange(_built);

            _services.CompanyName = input.CompanyName;
            _services.PersonName = input.PersonName;
            _services.Contact = input.Contact;
            try
            {
                SavedPath = ClientQuoteSheet.SaveToAnkenFolder(Path.Combine(_services.WorkspaceRoot, _anken.FolderPath), input);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "見積書を作れませんでした。\r\n\r\n" + ex.Message, "客先見積書", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
