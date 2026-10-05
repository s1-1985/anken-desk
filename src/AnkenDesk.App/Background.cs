using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>Outlookの操作中に出す「お待ちください」の画面。操作が終わるまで閉じられない。</summary>
    internal sealed class ProgressForm : Form
    {
        private bool _allowClose;

        public ProgressForm(string message)
        {
            UiStyle.Apply(this);
            Text = "処理中";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 150);

            Controls.Add(new Label { Text = message, Left = 20, Top = 20, Width = 480, Height = 56 });
            Controls.Add(new ProgressBar { Left = 20, Top = 90, Width = 480, Height = 24, Style = ProgressBarStyle.Marquee });
        }

        public void CloseWhenDone()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
            }

            base.OnFormClosing(e);
        }
    }

    internal static class Background
    {
        /// <summary>
        /// 時間のかかる処理（Outlookの操作）を、別の STA スレッドで行い、その間「お待ちください」を出す。
        /// COMのオブジェクトは、work の中で作って、work の中で解放すること。
        /// </summary>
        public static T Run<T>(IWin32Window owner, string message, Func<T> work)
        {
            T result = default!;
            Exception? error = null;
            using (var dlg = new ProgressForm(message))
            {
                dlg.Shown += (s, e) =>
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            result = work();
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                        }
                        finally
                        {
                            dlg.BeginInvoke((Action)dlg.CloseWhenDone);
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.IsBackground = true;
                    thread.Start();
                };
                dlg.ShowDialog(owner);
            }

            if (error != null)
            {
                throw new InvalidOperationException(error.Message, error);
            }

            return result;
        }
    }
}
