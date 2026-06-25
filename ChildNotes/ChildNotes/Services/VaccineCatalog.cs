namespace ChildNotes.Services;

/// <summary>
/// 疫苗目录（参照原始项目 constants/vaccines.js 简化版）
/// </summary>
public static class VaccineCatalog
{
    public sealed class Dose
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string AgeLabel { get; set; } = string.Empty;
        /// <summary>距出生天数（用于计算推荐接种日期），null 表示按门诊安排</summary>
        public int? DueDays { get; set; }
    }

    public sealed class Vaccine
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // free / paid
        public string Name { get; set; } = string.Empty;
        public List<Dose> Doses { get; set; } = new();
    }

    public static readonly IReadOnlyList<Vaccine> All = new[]
    {
        new Vaccine
        {
            Id = "hepb", Category = "free", Name = "乙肝疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "出生时", DueDays = 0 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "1月龄", DueDays = 30 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "6月龄", DueDays = 180 },
            ],
        },
        new Vaccine
        {
            Id = "bcg", Category = "free", Name = "卡介苗",
            Doses = [new Dose { Id = "dose1", Label = "1剂", AgeLabel = "出生时", DueDays = 0 }],
        },
        new Vaccine
        {
            Id = "ipv", Category = "free", Name = "脊灰灭活疫苗(IPV)",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "3月龄", DueDays = 90 },
            ],
        },
        new Vaccine
        {
            Id = "bopv", Category = "free", Name = "脊灰减毒活疫苗(bOPV)",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4周岁", DueDays = 1460 },
            ],
        },
        new Vaccine
        {
            Id = "dtap", Category = "free", Name = "百白破疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "3月龄", DueDays = 90 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "5月龄", DueDays = 150 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "18月龄", DueDays = 540 },
                new Dose { Id = "dose5", Label = "第5剂加强", AgeLabel = "6周岁", DueDays = 2190 },
            ],
        },
        new Vaccine
        {
            Id = "men_a", Category = "free", Name = "A群流脑多糖疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "6月龄", DueDays = 180 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "9月龄", DueDays = 270 },
            ],
        },
        new Vaccine
        {
            Id = "mmr", Category = "free", Name = "麻腮风疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "8月龄", DueDays = 240 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "18月龄", DueDays = 540 },
            ],
        },
        new Vaccine
        {
            Id = "je_live", Category = "free", Name = "乙脑减毒活疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "8月龄", DueDays = 240 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "2周岁", DueDays = 730 },
            ],
        },
        new Vaccine
        {
            Id = "hepa_live", Category = "free", Name = "甲肝减毒活疫苗",
            Doses = [new Dose { Id = "dose1", Label = "1剂", AgeLabel = "18月龄", DueDays = 540 }],
        },
        new Vaccine
        {
            Id = "men_ac", Category = "free", Name = "A群C群流脑多糖疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "3周岁", DueDays = 1095 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "6周岁", DueDays = 2190 },
            ],
        },
        new Vaccine
        {
            Id = "pcv13", Category = "paid", Name = "13价肺炎球菌结合疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "6月龄", DueDays = 180 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "12-15月龄", DueDays = 365 },
            ],
        },
        new Vaccine
        {
            Id = "rota5", Category = "paid", Name = "五价轮状病毒疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "6-12周龄", DueDays = 42 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "10-22周龄", DueDays = 70 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "14-32周龄", DueDays = 98 },
            ],
        },
        new Vaccine
        {
            Id = "hib", Category = "paid", Name = "b型流感嗜血杆菌(Hib)疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "3月龄", DueDays = 90 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "18月龄", DueDays = 540 },
            ],
        },
        new Vaccine
        {
            Id = "varicella", Category = "paid", Name = "水痘疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "12月龄", DueDays = 365 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4-6岁", DueDays = 1460 },
            ],
        },
        new Vaccine
        {
            Id = "flu", Category = "paid", Name = "流感疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "首次第1剂", AgeLabel = "满6月龄", DueDays = 180 },
                new Dose { Id = "dose2", Label = "首次第2剂", AgeLabel = "间隔4周", DueDays = 210 },
            ],
        },
        new Vaccine
        {
            Id = "ev71", Category = "paid", Name = "肠道病毒71型(EV71)疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "6月龄", DueDays = 180 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "7月龄", DueDays = 210 },
            ],
        },
        new Vaccine
        {
            Id = "pentavalent", Category = "paid", Name = "五联疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "3月龄", DueDays = 90 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "18月龄", DueDays = 540 },
            ],
        },
        new Vaccine
        {
            Id = "hepa_inact", Category = "paid", Name = "甲肝灭活疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "18月龄", DueDays = 540 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "24月龄", DueDays = 730 },
            ],
        },
        new Vaccine
        {
            Id = "je_inact", Category = "paid", Name = "乙脑灭活疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "8月龄", DueDays = 240 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "间隔7-10天", DueDays = 250 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "2周岁", DueDays = 730 },
                new Dose { Id = "dose4", Label = "第4剂", AgeLabel = "6周岁", DueDays = 2190 },
            ],
        },
    };

    /// <summary>
    /// 展开所有剂次，返回 (疫苗名+剂次, 月龄标签, 距出生天数) 列表
    /// </summary>
    public static IEnumerable<(string Name, string AgeLabel, int? DueDays)> FlattenDoses()
    {
        foreach (var v in All)
        {
            foreach (var d in v.Doses)
            {
                var name = string.IsNullOrEmpty(d.Label) ? v.Name : $"{v.Name} {d.Label}";
                yield return (name, d.AgeLabel, d.DueDays);
            }
        }
    }
}
