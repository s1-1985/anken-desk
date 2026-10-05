using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AnkenDesk.Core
{
    // 案件の項目の編集、見積依頼書の項目の既定値。
    public sealed partial class AnkenDb
    {
        /// <summary>登録済みの案件を、見積依頼書の入力（品番・品名・数量・項目・回答希望期日）として読み出す。</summary>
        public AnkenInput LoadAnkenInput(long ankenId)
        {
            var a = GetAnken(ankenId);
            if (a == null)
            {
                throw new InvalidOperationException("案件が見つかりません。");
            }

            var input = new AnkenInput
            {
                ClientId = a.ClientId,
                RequestDate = a.RequestDate,
                PartNumber = a.PartNumber,
                PartName = a.PartName,
                Note = a.Note,
                ReplyDueDate = a.ReplyDueDate,
            };
            foreach (var q in ListQuantities(ankenId))
            {
                input.Quantities.Add(q);
            }

            foreach (var kv in GetItems(ankenId))
            {
                input.Items[kv.Key] = kv.Value;
            }

            return input;
        }

        /// <summary>品名・回答希望期日・見積依頼書の項目を更新する。項目は、渡したものに置き換える（空の項目は保存しない）。</summary>
        public void UpdateAnkenDetails(long ankenId, string partName, DateTime replyDueDate, IDictionary<string, string> items)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE anken SET part_name = $n, reply_due_date = $d WHERE id = $id";
                    cmd.Parameters.AddWithValue("$n", (partName ?? "").Trim());
                    cmd.Parameters.AddWithValue("$d", replyDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("$id", ankenId);
                    if (cmd.ExecuteNonQuery() == 0)
                    {
                        throw new InvalidOperationException("案件が見つかりません。");
                    }
                }

                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM anken_item WHERE anken_id = $id";
                    del.Parameters.AddWithValue("$id", ankenId);
                    del.ExecuteNonQuery();
                }

                foreach (var kv in items)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value))
                    {
                        continue;
                    }

                    using (var ins = conn.CreateCommand())
                    {
                        ins.Transaction = tx;
                        ins.CommandText = "INSERT INTO anken_item (anken_id, name, value) VALUES ($id, $n, $v)";
                        ins.Parameters.AddWithValue("$id", ankenId);
                        ins.Parameters.AddWithValue("$n", kv.Key);
                        ins.Parameters.AddWithValue("$v", kv.Value.Trim());
                        ins.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
        }

        /// <summary>案件登録の入力欄の初期値。テンプレートの文章を基に、設定で変えた分を上書きする。</summary>
        public IReadOnlyDictionary<string, string> GetItemDefaults()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var template = QuoteRequestSheet.TemplateDefaults();
            foreach (var name in QuoteRequestItems.AllNames)
            {
                var saved = GetSetting(QuoteRequestItems.DefaultSettingPrefix + name);
                string fromTemplate;
                result[name] = saved ?? (template.TryGetValue(name, out fromTemplate) ? fromTemplate : "");
            }

            return result;
        }

        /// <summary>項目の既定値を変える（空にすることもできる）。</summary>
        public void SetItemDefault(string name, string value)
        {
            SetSetting(QuoteRequestItems.DefaultSettingPrefix + name, (value ?? "").Trim());
        }

        /// <summary>設定で変えた既定値を消して、テンプレートの文章に戻す。</summary>
        public void ResetItemDefaults()
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM settings WHERE key LIKE $p";
                cmd.Parameters.AddWithValue("$p", QuoteRequestItems.DefaultSettingPrefix + "%");
                cmd.ExecuteNonQuery();
            }
        }
    }
}
