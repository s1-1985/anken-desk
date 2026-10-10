using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>ファイルを案件にドロップしたとき、どこへ保存するかを選ぶ（客先の依頼ファイル／調達先の見積書／指定のサブフォルダ）。コピーするだけで、元のファイルは動かさない。</summary>
    internal sealed class FileTargetForm : Form
    {
        public enum Target
        {
            ClientRequest,
            SupplierQuote,
            Subfolder,
        }

        private readonly RadioButton _client = new RadioButton();
        private readonly RadioButton _quote = new RadioButton();
        private readonly RadioButton _sub = new RadioButton();
        private readonly ComboBox _supplier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _folder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly IReadOnlyList<AnkenSupplier> _suppliers;

        public Target Selected { get; private set; }
        public AnkenSupplier? Supplier { get; private set; }
        public int SubfolderIndex { get; private set; }

        public FileTargetForm(IReadOnlyList<string> files, IReadOnlyList<AnkenSupplier> suppliers)
        {
            _suppliers = suppliers;
            UiStyle.Apply(this);
            Text = "ファイルの保存先";
            ClientSize = new Size(700, 460);

            Controls.Add(new Label { Text = "ファイルをどこへ保存しますか？（コピーして保存。元は動かしません）", Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold), Left = 16, Top = 12, Width = 668, Height = 30 });
            var names = string.Join("、", files.Take(4).Select(Path.GetFileName)) + (files.Count > 4 ? " ほか" + (files.Count - 4) + "件" : "");
            Controls.Add(new Label { Text = files.Count + " 個: " + names, Left = 16, Top = 46, Width = 668, Height = 50, ForeColor = Color.FromArgb(90, 96, 100) });

            _client.SetBounds(16, 108, 668, 30);
            _client.Text = "客先の依頼ファイル（Excel・その他は「1.」、PDFは「2.図面」へ自動で）";
            _client.Checked = true;

            _quote.SetBounds(16, 160, 668, 30);
            _quote.Text = "調達先の見積書（「5.調達先見積もり」へ、略称つきの名前で）";
            _supplier.SetBounds(48, 196, 420, 32);
            _supplier.Items.AddRange(suppliers.Select(s => (object)s.SupplierName).ToArray());

            // ファイル名から調達先が1社に絞れるときは、それを初期値にして、調達先の見積書を選んでおく。
            var guesses = files.Select(f => QuoteFiles.GuessSupplier(Path.GetFileName(f), suppliers)).ToList();
            var guessed = guesses.All(g => g != null) && guesses.Select(g => g!.SupplierId).Distinct().Count() == 1 ? guesses[0] : null;
            if (suppliers.Count > 0)
            {
                _supplier.SelectedIndex = guessed != null ? suppliers.ToList().FindIndex(s => s.SupplierId == guessed.SupplierId) : 0;
            }

            if (guessed != null)
            {
                _quote.Checked = true;
                Controls.Add(new Label { Text = "ファイル名から「" + guessed.ShortName + "」と推定しました。", Left = 48, Top = 232, Width = 620, Height = 24, ForeColor = UiStyle.Primary });
            }

            _sub.SetBounds(16, 272, 668, 30);
            _sub.Text = "指定のサブフォルダ（そのままの名前で）";
            _folder.SetBounds(48, 308, 420, 32);
            _folder.Items.AddRange(FolderNames.Subfolders.Cast<object>().ToArray());
            _folder.SelectedIndex = 8;

            Controls.AddRange(new Control[] { _client, _quote, _supplier, _sub, _folder });

            var ok = UiStyle.CreateButton("保存", true, 140);
            ok.Left = 394; ok.Top = 398;
            ok.Click += (s, e) => Accept();
            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 544; cancel.Top = 398;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            if (_quote.Checked)
            {
                if (_supplier.SelectedIndex < 0)
                {
                    MessageBox.Show(this, "この案件に、調達先が加えられていません。", "ファイルの保存先", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Selected = Target.SupplierQuote;
                Supplier = _suppliers[_supplier.SelectedIndex];
            }
            else if (_sub.Checked)
            {
                Selected = Target.Subfolder;
                SubfolderIndex = _folder.SelectedIndex;
            }
            else
            {
                Selected = Target.ClientRequest;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
