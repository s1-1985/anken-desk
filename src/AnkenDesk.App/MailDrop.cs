using System;
using System.IO;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>OutlookからドラッグしたメールをOutlookのメールとして見分ける。</summary>
    internal static class MailDrop
    {
        /// <summary>
        /// Outlookでメールをドラッグすると、Outlook独自の形式（RenPrivateMessages / RenPrivateItem）と、
        /// ファイルの記述子（FileGroupDescriptorW。「件名.msg」という仮想ファイル）が入る。エクスプローラーからのファイル（FileDrop）とは別の形。
        /// 独自の形式が無くても、記述子のファイル名が .msg ならメールとみなす（Outlookの版による違いへの備え）。
        /// 実際のメールは、ドロップのあとに、Outlookで選ばれているメールをCOMで読んで取り出す（.msgの中身は、ここでは読まない）。
        /// 実機（会社PC）での確認は未了。
        /// </summary>
        public static bool IsOutlookMail(IDataObject? data)
        {
            if (data == null)
            {
                return false;
            }

            try
            {
                if (data.GetDataPresent("RenPrivateMessages") || data.GetDataPresent("RenPrivateItem"))
                {
                    return true;
                }

                return !data.GetDataPresent(DataFormats.FileDrop) && DescriptorHasMsg(data);
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is System.Runtime.InteropServices.ExternalException || ex is IOException)
            {
                return false;
            }
        }

        private static bool DescriptorHasMsg(IDataObject data)
        {
            if (!data.GetDataPresent("FileGroupDescriptorW"))
            {
                return false;
            }

            var stream = data.GetData("FileGroupDescriptorW") as MemoryStream;
            return stream != null && AnkenDesk.Core.FileGroupDescriptor.HasMsg(stream.ToArray());
        }
    }
}
