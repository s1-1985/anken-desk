using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace AnkenDesk.OutlookAccess
{
    /// <summary>
    /// Outlookの既定の署名（テキスト版）を読む。COMで作ったメールには署名が自動では入らない
    /// （HANDOFF.md §8.1 検証5）ので、アプリが本文の末尾に付ける。
    /// 画像つきのHTMLの署名は再現しない（テキスト版だけ）。読めなければ null。
    /// </summary>
    public static class OutlookSignature
    {
        public static string? TryReadText()
        {
            try
            {
                string? name = null;
                foreach (var version in new[] { "16.0", "15.0", "14.0" })
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office\" + version + @"\Common\MailSettings"))
                    {
                        var value = key == null ? null : key.GetValue("NewSignature") as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            name = value;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(name))
                {
                    return null;
                }

                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Signatures", name + ".txt");
                if (!File.Exists(path))
                {
                    return null;
                }

                // Outlookは、テキスト版の署名を、その端末の既定の文字コード（日本語環境ではShift-JIS）で保存する。
                using (var reader = new StreamReader(path, Encoding.Default, true))
                {
                    var text = reader.ReadToEnd().TrimEnd();
                    return text.Length == 0 ? null : text;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }
    }
}
