using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    internal sealed class PEnv : IDisposable
    {
        public TempDir Dir = new TempDir();
        public AnkenDb Db;
        public Client C;
        public Supplier S1, S2, S3;

        public PEnv()
        {
            Db = new AnkenDb(Path.Combine(Dir.Path, "a.db"));
            C = Db.AddClient("ダミーC");
            S1 = Db.AddSupplier("調達先甲", "甲", "k@example.com", "");
            S2 = Db.AddSupplier("調達先乙", "乙", "o@example.com", "");
            S3 = Db.AddSupplier("調達先丙", "丙", "h@example.com", "");
        }

        public AnkenRecord Make(string part, DateTime req, params Supplier[] sups)
        {
            var input = new AnkenInput { ClientId = C.Id, RequestDate = req, PartNumber = part, ReplyDueDate = req.AddDays(7) };
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個/Lot" });
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 10, Unit = "個/Lot" });
            input.Quantities.Add(new QuantityPattern { Kind = "量産", Quantity = 3000, Unit = "個/Lot" });
            input.Quantities.Add(new QuantityPattern { Kind = "年間見込数", Quantity = 36000, Unit = "個/年" });
            var a = Db.InsertAnken(input, Path.Combine("c", part + req.ToString("yyyyMMdd")));
            foreach (var s in sups)
            {
                Db.AddSupplierToAnken(a.Id, s.Id);
            }

            return a;
        }

        public void Answer(AnkenRecord a, Supplier s, DateTime? received, DateTime? valid, params decimal?[] prices)
        {
            var ans = Db.ListAnkenSuppliers(a.Id).Single(x => x.SupplierId == s.Id);
            ans.ReceivedAt = received;
            ans.ValidUntil = valid;
            var pats = Db.ListQuantities(a.Id);
            var quotes = new List<Quote>();
            for (var i = 0; i < prices.Length && i < pats.Count; i++)
            {
                if (prices[i].HasValue)
                {
                    quotes.Add(new Quote { SupplierId = s.Id, PatternId = pats[i].Id, UnitPrice = prices[i], LeadTimeDays = 10 });
                }
            }

            Db.SaveSupplierAnswer(ans, quotes);
        }

        public void Dispose()
        {
            Db.Dispose();
            Dir.Dispose();
        }
    }

    // ---- A. 辞退・検討中 ----
    public class ResponseStatusTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 12);

        [Fact]
        public void Declined_and_considering_are_stored_and_cleared_by_status()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1, e.S2);
                e.Db.SetSupplierResponse(a.Id, e.S1.Id, ResponseStatus.Declined, null, "  工程が合わない  ");
                e.Db.SetSupplierResponse(a.Id, e.S2.Id, ResponseStatus.Considering, new DateTime(2026, 10, 20), "無視される理由");
                var list = e.Db.ListAnkenSuppliers(a.Id);
                var s1 = list.Single(x => x.SupplierId == e.S1.Id);
                var s2 = list.Single(x => x.SupplierId == e.S2.Id);
                Assert.True(s1.IsDeclined);
                Assert.Equal("工程が合わない", s1.DeclineReason);
                Assert.True(s2.IsConsidering);
                Assert.Equal(new DateTime(2026, 10, 20), s2.PromisedDate);
                Assert.Equal("", s2.DeclineReason);
                Assert.False(s1.IsPending);
                Assert.True(s2.IsPending);

                e.Db.SetSupplierResponse(a.Id, e.S1.Id, ResponseStatus.None, null, "");
                Assert.True(e.Db.ListAnkenSuppliers(a.Id).Single(x => x.SupplierId == e.S1.Id).IsPending);
                Assert.Throws<ArgumentException>(() => e.Db.SetSupplierResponse(a.Id, e.S1.Id, "なぞ", null, ""));
                Assert.Throws<InvalidOperationException>(() => e.Db.SetSupplierResponse(a.Id, e.S3.Id, ResponseStatus.Declined, null, ""));
            }
        }

        [Fact]
        public void A_received_date_beats_declined()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1);
                e.Db.SetSupplierResponse(a.Id, e.S1.Id, ResponseStatus.Declined, null, "");
                e.Answer(a, e.S1, new DateTime(2026, 10, 5), null, 100m);
                var s = e.Db.ListAnkenSuppliers(a.Id).Single();
                Assert.True(s.IsAnswered);
                Assert.False(s.IsDeclined);
            }
        }

        [Fact]
        public void Declined_suppliers_are_not_waited_for_or_reminded_and_all_declined_is_not_action()
        {
            using (var e = new PEnv())
            {
                var a1 = e.Make("P1", new DateTime(2026, 10, 1), e.S1, e.S2, e.S3);
                foreach (var s in new[] { e.S1, e.S2, e.S3 })
                {
                    e.Db.MarkRequestSent(a1.Id, s.Id, new DateTime(2026, 10, 2));
                }

                e.Answer(a1, e.S1, new DateTime(2026, 10, 5), null, 100m);
                e.Db.SetSupplierResponse(a1.Id, e.S2.Id, ResponseStatus.Declined, null, "");
                e.Db.SetSupplierResponse(a1.Id, e.S3.Id, ResponseStatus.Considering, new DateTime(2026, 10, 20), "");

                var a2 = e.Make("P2", new DateTime(2026, 10, 1), e.S1, e.S2);
                e.Db.MarkRequestSent(a2.Id, e.S1.Id, new DateTime(2026, 10, 2));
                e.Db.SetSupplierResponse(a2.Id, e.S1.Id, ResponseStatus.Declined, null, "");
                e.Db.SetSupplierResponse(a2.Id, e.S2.Id, ResponseStatus.Declined, null, "");

                var rows = Home.Build(e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), Today);
                var r1 = rows.Single(r => r.Anken.Id == a1.Id);
                Assert.Equal(1, r1.Answered);
                Assert.Equal(1, r1.Declined);
                Assert.Equal(1, r1.Pending);
                Assert.True(r1.NeedsAction); // 検討中の丙が、まだ回答待ち
                Assert.Equal(new[] { "丙" }, r1.PendingNames.ToArray());
                Assert.Contains("検討中 10/20まで", r1.PendingDetail.Single());
                Assert.Empty(r1.RemindSupplierIds); // 検討中で回答予定日がまだ先 → 催促しない
                Assert.Equal("1/3社（辞退1）　未回答: 丙（検討中 10/20まで）", r1.ProgressText(false));

                var r2 = rows.Single(r => r.Anken.Id == a2.Id);
                Assert.True(r2.AllDeclined);
                Assert.False(r2.NeedsAction);
                Assert.False(r2.IsAnswered);
                Assert.Equal("全社辞退", r2.StatusText);
                Assert.Equal("× ", Home.StatusMark(r2));
            }
        }

        [Fact]
        public void Considering_past_its_promised_date_is_reminded_again_and_a_declined_rest_makes_it_answered()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1, e.S2);
                foreach (var s in new[] { e.S1, e.S2 })
                {
                    e.Db.MarkRequestSent(a.Id, s.Id, new DateTime(2026, 10, 2));
                }

                e.Db.SetSupplierResponse(a.Id, e.S1.Id, ResponseStatus.Considering, new DateTime(2026, 10, 10), ""); // 約束の日を過ぎた
                var row = Home.Build(e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), Today).Single();
                Assert.Equal(2, row.RemindSupplierIds.Count);

                e.Answer(a, e.S1, new DateTime(2026, 10, 11), null, 100m);
                e.Db.SetSupplierResponse(a.Id, e.S2.Id, ResponseStatus.Declined, null, "");
                var done = Home.Build(e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), Today).Single();
                Assert.True(done.IsAnswered); // 回答待ちが無い（残りは辞退）
                Assert.False(done.NeedsAction);
                Assert.Equal("回答済み", done.StatusText);
            }
        }

        [Fact]
        public void Stats_count_declines_and_summarise_for_the_picker()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1);
                var b = e.Make("P2", new DateTime(2026, 10, 2), e.S1);
                e.Db.MarkRequestSent(a.Id, e.S1.Id, new DateTime(2026, 10, 2));
                e.Db.MarkRequestSent(b.Id, e.S1.Id, new DateTime(2026, 10, 3));
                e.Db.SetSupplierResponse(a.Id, e.S1.Id, ResponseStatus.Declined, null, "");
                e.Answer(b, e.S1, new DateTime(2026, 10, 5), null, 100m);

                var st = SupplierStats.Compute(e.Db.ListAllAnkenSuppliers(), id => e.Db.ListQuotes(id)).Single();
                Assert.Equal(2, st.Requested);
                Assert.Equal(1, st.Declined);
                Assert.Equal(0.5, st.DeclineRate);
                Assert.Equal("回答50%・平均2.0日・辞退1", st.Summary());
                Assert.Equal("", new SupplierStat().Summary());
            }
        }

        [Fact]
        public void Removed_supplier_keeps_status_and_validity_when_restored()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1);
                e.Answer(a, e.S1, new DateTime(2026, 10, 5), new DateTime(2026, 11, 5), 100m);
                e.Db.SetSupplierResponse(a.Id, e.S1.Id, ResponseStatus.Considering, new DateTime(2026, 10, 20), "");
                e.Db.RemoveSupplierFromAnken(a.Id, e.S1.Id);
                Assert.True(e.Db.RestoreRemovedSupplier(e.Db.ListRemovedSuppliers(a.Id).Single().Id));
                var s = e.Db.ListAnkenSuppliers(a.Id).Single();
                Assert.Equal(new DateTime(2026, 11, 5), s.ValidUntil);
                Assert.Equal(ResponseStatus.Considering, s.ResponseStatus);
                Assert.Equal(new DateTime(2026, 10, 20), s.PromisedDate);
            }
        }
    }

    // ---- B. 有効期限 ----
    public class QuoteValidityTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 12);

        private static AnkenSupplier S(string name, DateTime? valid, bool answered = true)
        {
            return new AnkenSupplier { ShortName = name, ValidUntil = valid, ReceivedAt = answered ? new DateTime(2026, 10, 1) : (DateTime?)null };
        }

        [Fact]
        public void Alerts_list_expired_and_within_a_week_nearest_first_and_skip_others()
        {
            var list = new[]
            {
                S("甲", Today.AddDays(-3)), S("乙", Today.AddDays(7)), S("丙", Today.AddDays(8)),
                S("丁", Today), S("戊", null), S("己", Today.AddDays(1), answered: false),
            };
            var alerts = QuoteValidity.Alerts(list, Today);
            Assert.Equal(new[] { "甲", "丁", "乙" }, alerts.Select(a => a.Supplier.ShortName).ToArray());
            Assert.True(alerts[0].Expired);
            Assert.Equal("甲: 有効期限切れ（3日前）", alerts[0].Text);
            Assert.Equal("丁: 本日まで", alerts[1].Text);
            Assert.Equal("乙: 10/19まで（あと7日）", alerts[2].Text);
        }

        [Fact]
        public void Text_for_the_matrix_row()
        {
            Assert.Equal("", QuoteValidity.Text(S("甲", null), Today));
            Assert.Equal("2026/12/01", QuoteValidity.Text(S("甲", new DateTime(2026, 12, 1)), Today));
            Assert.Contains("【期限切れ 2日前】", QuoteValidity.Text(S("甲", Today.AddDays(-2)), Today));
            Assert.Contains("（あと3日）", QuoteValidity.Text(S("甲", Today.AddDays(3)), Today));
        }

        [Fact]
        public void Home_filter_shows_anken_with_expiring_quotes_but_not_closed_ones()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1);
                var b = e.Make("P2", new DateTime(2026, 10, 1), e.S1);
                e.Answer(a, e.S1, new DateTime(2026, 10, 3), Today.AddDays(2), 100m);
                e.Answer(b, e.S1, new DateTime(2026, 10, 3), Today.AddDays(-1), 100m);
                e.Db.SetAnkenStatus(b.Id, AnkenStatus.Won);
                var rows = Home.Build(e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), Today);
                var hit = Home.Filter(rows, HomeFilter.QuoteExpiring, null);
                Assert.Equal(a.Id, hit.Single().Anken.Id);
                Assert.Contains("見積の期限: 甲: ", hit.Single().ProgressText(false));
            }
        }

        [Fact]
        public void ValidUntil_roundtrips_through_the_answer()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1);
                e.Answer(a, e.S1, new DateTime(2026, 10, 3), new DateTime(2026, 12, 31), 100m);
                Assert.Equal(new DateTime(2026, 12, 31), e.Db.ListAnkenSuppliers(a.Id).Single().ValidUntil);
                e.Answer(a, e.S1, new DateTime(2026, 10, 3), null, 100m);
                Assert.Null(e.Db.ListAnkenSuppliers(a.Id).Single().ValidUntil);
            }
        }
    }

    // ---- C. 比較表のチェック ----
    public class QuoteCheckTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 12);

        private static List<QuantityPattern> Pats()
        {
            return new List<QuantityPattern>
            {
                new QuantityPattern { Id = 1, Kind = "試作", Quantity = 3, Unit = "個/Lot" },
                new QuantityPattern { Id = 2, Kind = "試作", Quantity = 10, Unit = "個/Lot" },
                new QuantityPattern { Id = 3, Kind = "量産", Quantity = 3000, Unit = "個/Lot" },
                new QuantityPattern { Id = 4, Kind = "年間見込数", Quantity = 36000, Unit = "個/年" },
            };
        }

        private static AnkenSupplier S(long id, string name, bool answered = true, DateTime? valid = null, string extra = "")
        {
            return new AnkenSupplier { SupplierId = id, ShortName = name, SupplierName = name, ReceivedAt = answered ? new DateTime(2026, 10, 1) : (DateTime?)null, ValidUntil = valid ?? Today.AddDays(30), ExtraCost = extra };
        }

        private static Quote Q(long s, long p, decimal price, int? lt = 10)
        {
            return new Quote { SupplierId = s, PatternId = p, UnitPrice = price, LeadTimeDays = lt };
        }

        [Fact]
        public void Clean_data_has_no_warnings()
        {
            var w = QuoteChecks.Run(Pats(), new[] { S(1, "甲"), S(2, "乙") },
                new[] { Q(1, 1, 5000), Q(1, 2, 4500), Q(1, 3, 900), Q(2, 1, 5200), Q(2, 2, 4700), Q(2, 3, 950) }, Today);
            Assert.Empty(w);
        }

        [Fact]
        public void More_quantity_but_higher_price_and_mass_above_prototype_are_flagged()
        {
            var w = QuoteChecks.Run(Pats(), new[] { S(1, "甲") }, new[] { Q(1, 1, 4000), Q(1, 2, 4500), Q(1, 3, 5000) }, Today);
            Assert.Contains(w, x => x.Level == WarningLevel.Check && x.PatternId == 2 && x.Text.Contains("数量が多い"));
            Assert.Contains(w, x => x.Level == WarningLevel.Check && x.PatternId == 3 && x.Text.Contains("量産"));
            Assert.DoesNotContain(w, x => x.PatternId == 4); // 年間見込数は対象外
        }

        [Fact]
        public void Outlier_among_three_and_big_gap_between_two()
        {
            var three = QuoteChecks.Run(Pats(), new[] { S(1, "甲"), S(2, "乙"), S(3, "丙") }, new[] { Q(1, 1, 1000), Q(2, 1, 1100), Q(3, 1, 2000) }, Today);
            var out1 = Assert.Single(three, x => x.Text.Contains("中央値"));
            Assert.Equal(3, out1.SupplierId);
            Assert.Contains("高い", out1.Text);

            var two = QuoteChecks.Run(Pats(), new[] { S(1, "甲"), S(2, "乙") }, new[] { Q(1, 1, 1000), Q(2, 1, 2500) }, Today);
            Assert.Single(two, x => x.Text.Contains("倍です"));
            var close = QuoteChecks.Run(Pats(), new[] { S(1, "甲"), S(2, "乙") }, new[] { Q(1, 1, 1000), Q(2, 1, 1900) }, Today);
            Assert.DoesNotContain(close, x => x.Text.Contains("倍です"));
        }

        [Fact]
        public void Answered_without_prices_missing_one_pattern_and_long_lead_time()
        {
            var w = QuoteChecks.Run(Pats(), new[] { S(1, "甲"), S(2, "乙"), S(3, "丙") },
                new[] { Q(1, 1, 1000, 10), Q(2, 1, 1000, 10), Q(2, 2, 900, 10), Q(1, 2, 900, 10), Q(3, 1, 1000, 40) }, Today);
            Assert.Contains(w, x => x.SupplierId == 3 && x.PatternId == 2 && x.Text.Contains("未入力"));
            Assert.Contains(w, x => x.SupplierId == 3 && x.Level == WarningLevel.Notice && x.Text.Contains("リードタイム"));

            var none = QuoteChecks.Run(Pats(), new[] { S(1, "甲") }, new Quote[0], Today);
            Assert.Contains(none, x => x.Level == WarningLevel.Check && x.Text.Contains("1つも入っていません"));
            var waiting = QuoteChecks.Run(Pats(), new[] { S(1, "甲", answered: false) }, new Quote[0], Today);
            Assert.Empty(waiting); // まだ回答していない調達先は、責めない
        }

        [Fact]
        public void Extra_cost_without_amount_and_validity_problems()
        {
            var w = QuoteChecks.Run(Pats(), new[]
            {
                S(1, "甲", extra: "治具費は別途相談"), S(2, "乙", extra: "治具費 30,000円"),
                new AnkenSupplier { SupplierId = 3, ShortName = "丙", ReceivedAt = new DateTime(2026, 10, 1) },           // 有効期限なし（既定の30日後が入らないよう直接作る）
                S(4, "丁", valid: Today.AddDays(-1)),
            }, new[] { Q(1, 1, 1000), Q(2, 1, 1000), Q(3, 1, 1000), Q(4, 1, 1000) }, Today);
            Assert.Single(w, x => x.Text.Contains("別費用に、金額がありません"));
            Assert.Single(w, x => x.SupplierId == 3 && x.Text.Contains("有効期限が未入力"));
            Assert.Single(w, x => x.SupplierId == 4 && x.Level == WarningLevel.Check && x.Text.Contains("有効期限が切れています"));
            Assert.Equal(WarningLevel.Check, w[0].Level); // 確認が先
        }
    }

    // ---- D. 前回比 ----
    public class PriceHistoryTests
    {
        [Fact]
        public void Previous_quote_is_the_latest_earlier_anken_of_the_same_part_same_supplier_and_quantity()
        {
            using (var e = new PEnv())
            {
                var old = e.Make("DUMMY-001", new DateTime(2026, 1, 1), e.S1);
                var recent = e.Make("DUMMY 001", new DateTime(2026, 6, 1), e.S1, e.S2);
                var other = e.Make("OTHER-9", new DateTime(2026, 7, 1), e.S1);
                var later = e.Make("DUMMY-001", new DateTime(2026, 12, 1), e.S1);
                var cur = e.Make("DUMMY-001", new DateTime(2026, 10, 1), e.S1, e.S2);
                e.Answer(old, e.S1, new DateTime(2026, 1, 5), null, 4000m, 3500m, 800m);
                e.Answer(recent, e.S1, new DateTime(2026, 6, 5), null, 4200m, null, 850m);
                e.Answer(recent, e.S2, new DateTime(2026, 6, 5), null, 5000m);
                e.Answer(other, e.S1, new DateTime(2026, 7, 5), null, 1m);
                e.Answer(later, e.S1, new DateTime(2026, 12, 5), null, 9999m);

                var prev = PriceHistory.PreviousQuotes(e.Db, e.Db.GetAnken(cur.Id)!);
                var p1 = prev[PriceHistory.QuoteKey(e.S1.Id, "試作 3個/Lot")];
                Assert.Equal(4200m, p1.Price);                           // 新しい方（6月）
                Assert.Equal(new DateTime(2026, 6, 1), p1.Date);
                Assert.Equal(3500m, prev[PriceHistory.QuoteKey(e.S1.Id, "試作 10個/Lot")].Price); // 6月には無いので、1月のもの
                Assert.Equal(5000m, prev[PriceHistory.QuoteKey(e.S2.Id, "試作 3個/Lot")].Price);
                Assert.DoesNotContain(prev.Values, v => v.Price == 9999m || v.Price == 1m); // 今回より後・別の品番は使わない
                Assert.Equal("前回 ¥4,200（+10.0%）", PreviousPrice.Format(p1, 4620m));
                Assert.Equal("前回 ¥4,200", PreviousPrice.Format(p1, null));
                Assert.Equal(-5.0m, p1.DiffPercent(3990m));
            }
        }

        [Fact]
        public void Previous_selling_price_comes_from_client_prices_of_the_same_part()
        {
            using (var e = new PEnv())
            {
                var old = e.Make("DUMMY-001", new DateTime(2026, 1, 1), e.S1);
                var cur = e.Make("DUMMY-001", new DateTime(2026, 10, 1), e.S1);
                var pats = e.Db.ListQuantities(old.Id);
                e.Db.SaveClientPrices(old.Id, new[] { new ClientPrice { PatternId = pats[2].Id, SellingPrice = 1100m } });
                var prev = PriceHistory.PreviousSelling(e.Db, e.Db.GetAnken(cur.Id)!);
                Assert.Equal(1100m, prev["量産 3,000個/Lot"].Price);
                Assert.Single(prev);
            }
        }
    }

    // ---- E. 失注理由と集計 ----
    public class ResultStatsTests
    {
        [Fact]
        public void Reason_is_kept_only_for_lost_or_on_hold()
        {
            using (var e = new PEnv())
            {
                var a = e.Make("P1", new DateTime(2026, 10, 1), e.S1);
                e.Db.SetAnkenResult(a.Id, AnkenStatus.Lost, new DateTime(2026, 10, 9), "他社が安かった", null, "価格");
                Assert.Equal("価格", e.Db.GetAnken(a.Id)!.ResultReason);
                e.Db.SetAnkenResult(a.Id, AnkenStatus.Won, new DateTime(2026, 10, 10), "", e.S1.Id, "価格");
                Assert.Equal("", e.Db.GetAnken(a.Id)!.ResultReason);
                e.Db.SetAnkenResult(a.Id, AnkenStatus.OnHold, null, "", null, "客先の都合（中止・延期）");
                Assert.Equal("客先の都合（中止・延期）", e.Db.GetAnken(a.Id)!.ResultReason);
                Assert.Contains("価格", LossReasons.All);
            }
        }

        [Fact]
        public void Report_counts_win_rate_margin_reasons_and_respects_the_period()
        {
            using (var e = new PEnv())
            {
                Func<string, DateTime, string, long?, AnkenRecord> make = (part, req, status, adopted) =>
                {
                    var a = e.Make(part, req, e.S1, e.S2);
                    e.Db.SetAnkenResult(a.Id, status, req.AddDays(5), "", adopted, status == AnkenStatus.Lost ? "価格" : "");
                    return a;
                };
                var won1 = make("W1", new DateTime(2026, 9, 1), AnkenStatus.Won, e.S1.Id);
                var won2 = make("W2", new DateTime(2026, 9, 10), AnkenStatus.Won, e.S2.Id);
                make("L1", new DateTime(2026, 9, 12), AnkenStatus.Lost, null);
                make("H1", new DateTime(2026, 10, 2), AnkenStatus.OnHold, null);
                make("OLD", new DateTime(2025, 1, 1), AnkenStatus.Lost, null);
                e.Make("OPEN", new DateTime(2026, 10, 3), e.S1);

                // 粗利: 受注W1は 仕入1000→提出1250（20%）、W2は 仕入800→提出1000（20%）と 仕入500→提出500（0%）→ 案件平均10%
                var p1 = e.Db.ListQuantities(won1.Id);
                e.Answer(won1, e.S1, new DateTime(2026, 9, 3), null, 1000m);
                e.Db.SaveClientPrices(won1.Id, new[] { new ClientPrice { PatternId = p1[0].Id, SellingPrice = 1250m, AdoptedSupplierId = e.S1.Id } });
                var p2 = e.Db.ListQuantities(won2.Id);
                e.Answer(won2, e.S2, new DateTime(2026, 9, 12), null, 800m, 500m);
                e.Db.SaveClientPrices(won2.Id, new[]
                {
                    new ClientPrice { PatternId = p2[0].Id, SellingPrice = 1000m, AdoptedSupplierId = e.S2.Id },
                    new ClientPrice { PatternId = p2[1].Id, SellingPrice = 500m, AdoptedSupplierId = e.S2.Id },
                });

                var all = ResultStats.Compute(e.Db, null, null);
                Assert.Equal(2, all.Overall.Won);
                Assert.Equal(2, all.Overall.Lost);
                Assert.Equal(1, all.Overall.OnHold);
                Assert.Equal(1, all.Overall.Open);
                Assert.Equal(50.0m, all.Overall.WinRate);
                Assert.Equal(15.0m, all.Overall.AverageMargin); // (20% + 10%) の平均
                Assert.Equal(new[] { "調達先乙", "調達先甲" }.OrderBy(x => x), all.BySupplier.Select(r => r.Label).OrderBy(x => x));
                Assert.Equal(new[] { new KeyValuePair<string, int>("価格", 2), new KeyValuePair<string, int>("（未入力）", 1) }, all.LossReasons.ToArray());
                Assert.Equal("2026/10", all.ByMonth[0].Label);

                var thisYear = ResultStats.Compute(e.Db, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
                Assert.Equal(1, thisYear.Overall.Lost); // 2025年の失注は除く
                Assert.Equal(66.7m, thisYear.Overall.WinRate);

                var none = ResultStats.Compute(e.Db, new DateTime(2030, 1, 1), null);
                Assert.Null(none.Overall.WinRate);
                Assert.Empty(none.ByClient);
            }
        }
    }

    // ---- H. CC（Core側）, G. カテゴリ ----
    public class CcAndCategoryTests
    {
        [Fact]
        public void Cc_address_is_part_of_the_request_and_validated_like_to()
        {
            var r = new MailRequest { ToAddress = "a@example.com", CcAddress = "me@example.com;boss@example.com" };
            Assert.True(MailSender.IsValidAddressList(r.CcAddress));
            Assert.False(MailSender.IsValidAddressList("not-an-address"));
        }

        [Fact]
        public void Category_is_added_through_the_inbox_and_the_name_is_fixed()
        {
            using (var inbox = new FakeMailInbox())
            {
                Assert.True(inbox.AddCategory("E1", InboundImporter.CategoryName));
                Assert.Equal(new[] { "E1|案件デスク" }, inbox.Categorized.ToArray());
            }
        }
    }

    // ---- I. 客先見積書 ----
    public class ClientQuoteTests
    {
        [Fact]
        public void Lines_use_selling_prices_and_amounts_only_for_per_piece_units()
        {
            var pats = new[]
            {
                new QuantityPattern { Id = 1, Kind = "試作", Quantity = 3, Unit = "個/Lot" },
                new QuantityPattern { Id = 2, Kind = "量産", Quantity = 3000, Unit = "個" },
                new QuantityPattern { Id = 3, Kind = "年間見込数", Quantity = 36000, Unit = "個/年" },
                new QuantityPattern { Id = 4, Kind = "量産", Quantity = 1, Unit = "式" },
                new QuantityPattern { Id = 5, Kind = "試作", Quantity = 5, Unit = "個/Lot" }, // 提出単価なし
            };
            var prices = new[]
            {
                new ClientPrice { PatternId = 1, SellingPrice = 5800.5m }, new ClientPrice { PatternId = 2, SellingPrice = 1000m },
                new ClientPrice { PatternId = 3, SellingPrice = 900m }, new ClientPrice { PatternId = 4, SellingPrice = 123456m },
            };
            var lines = ClientQuoteSheet.BuildLines(pats, prices);
            Assert.Equal(3, lines.Count); // 年間見込数と、提出単価なしは出さない
            Assert.Equal(17401.5m, lines[0].Amount);
            Assert.Equal(3000000m, lines[1].Amount);
            Assert.Null(lines[2].Amount); // 「式」は金額を出さない
        }

        [Fact]
        public void Default_delivery_is_the_longest_lead_time_of_the_chosen_suppliers()
        {
            var prices = new[]
            {
                new ClientPrice { PatternId = 1, SellingPrice = 1m, AdoptedSupplierId = 10 }, new ClientPrice { PatternId = 2, SellingPrice = 1m, AdoptedSupplierId = 20 },
                new ClientPrice { PatternId = 3, SellingPrice = null, AdoptedSupplierId = 20 },
            };
            var quotes = new[]
            {
                new Quote { SupplierId = 10, PatternId = 1, LeadTimeDays = 14 }, new Quote { SupplierId = 20, PatternId = 2, LeadTimeDays = 30 },
                new Quote { SupplierId = 20, PatternId = 3, LeadTimeDays = 90 }, new Quote { SupplierId = 30, PatternId = 1, LeadTimeDays = 99 },
            };
            Assert.Equal("受注後 30日", ClientQuoteSheet.DefaultDelivery(prices, quotes));
            Assert.Equal("", ClientQuoteSheet.DefaultDelivery(new ClientPrice[0], quotes));
        }

        [Fact]
        public void Sheet_has_no_supplier_information_and_is_saved_in_folder_7_with_numbering()
        {
            using (var t = new TempDir())
            {
                var anken = new AnkenRecord { Id = 1, ClientName = "ダミー得意先", PartNumber = "DUMMY-001", PartName = "ブラケット", RequestDate = new DateTime(2026, 10, 1) };
                var input = new ClientQuoteInput
                {
                    Anken = anken, ToName = "ダミー得意先　試作", QuoteDate = new DateTime(2026, 10, 12), ValidUntil = new DateTime(2026, 11, 11),
                    Delivery = "受注後 30日", Remarks = "価格は税抜です。\r\n型費は別途ご相談です。", CompanyName = "ダミー商事", PersonName = "担当 ダミー", Contact = "TEL 00-0000-0000",
                };
                input.Lines.Add(new ClientQuoteLine { Pattern = "試作 3個/Lot", UnitPrice = 5800m, Amount = 17400m });
                input.Lines.Add(new ClientQuoteLine { Pattern = "量産 1式", UnitPrice = 123456m });

                Directory.CreateDirectory(Path.Combine(t.Path, "anken"));
                var path = ClientQuoteSheet.SaveToAnkenFolder(Path.Combine(t.Path, "anken"), input);
                var second = ClientQuoteSheet.SaveToAnkenFolder(Path.Combine(t.Path, "anken"), input);
                Assert.Equal(Path.Combine(t.Path, "anken", FolderNames.Subfolders[6], "20261012　DUMMY-001　見積書.xlsx"), path);
                Assert.EndsWith("見積書 (2).xlsx", second);

                using (var wb = new XLWorkbook(path))
                {
                    var ws = wb.Worksheet("見積書");
                    var text = string.Join("\n", ws.CellsUsed().Select(c => c.GetString()));
                    Assert.Contains("御　見　積　書", text);
                    Assert.Contains("ダミー得意先　試作　御中", text);
                    Assert.Contains("DUMMY-001　ブラケット のお見積り", text);
                    Assert.Contains("2026/11/11", text);
                    Assert.Contains("受注後 30日", text);
                    Assert.Contains("型費は別途ご相談です。", text);
                    Assert.Contains("ダミー商事", text);
                    Assert.DoesNotContain("調達先", text);
                    var amountRow = ws.CellsUsed().First(c => c.GetString() == "試作 3個/Lot").Address.RowNumber;
                    Assert.Equal(5800, ws.Cell(amountRow, 3).GetDouble());
                    Assert.Equal(17400, ws.Cell(amountRow, 4).GetDouble());
                    Assert.True(ws.Cell(amountRow + 1, 4).IsEmpty()); // 「式」の金額は空欄
                }
            }
        }

        [Fact]
        public void Without_lines_nothing_is_created()
        {
            using (var t = new TempDir())
            {
                Assert.Throws<InvalidOperationException>(() => ClientQuoteSheet.Save(Path.Combine(t.Path, "x.xlsx"), new ClientQuoteInput()));
                Assert.Throws<DirectoryNotFoundException>(() => ClientQuoteSheet.SaveToAnkenFolder(Path.Combine(t.Path, "無い"), new ClientQuoteInput()));
            }
        }
    }
}
