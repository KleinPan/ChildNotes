using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

public partial class RecordSheetViewModel : RecordFormHostViewModel
{
    /// <summary>保存成功后触发（用于刷新首页/喂养页数据）。</summary>
    public event Action? Saved;

    /// <summary>抽屉关闭时触发（无论保存还是点 X 关闭，统一通知 Shell 重置 IsRecordSheetOpen）。</summary>
    public event Action? Closed;

    /// <summary>是否为编辑模式（true=编辑现有记录，false=新建记录）。</summary>
    [ObservableProperty] private bool _isEditMode;

    /// <summary>编辑模式下保存的记录 ID；新建模式为 0。</summary>
    private long _editingId;

    /// <summary>编辑模式下保存的原始记录日期（用于时间解析回填）。</summary>
    private DateTime _editingDate;

    public async Task OpenAsync(string type)
    {
        ActiveType = type;
        IsEditMode = false;
        _editingId = 0;
        SheetTitle = BuildTitle("记录", type);
        ErrorMessage = string.Empty;
        IsVisible = true;
        // 疫苗类型需要加载时间轴：先显示抽屉占位，再异步填充数据
        if (type == RecordType.Vaccine)
        {
            await VaccineForm.LoadAsync();
        }
    }

    /// <summary>
    /// 编辑模式入口：用现有记录填充表单，标题改为「编辑xxx」。
    /// 复用与新建完全相同的表单字段，保证可编辑所有字段。
    /// </summary>
    public void Edit(ChildRecord record)
    {
        ActiveType = record.RecordType;
        IsEditMode = true;
        _editingId = record.Id;
        _editingDate = record.RecordDate;
        SheetTitle = BuildTitle("编辑", record.RecordType);
        ErrorMessage = string.Empty;
        FillForm(record);
        IsVisible = true;
    }

    /// <summary>疫苗专用：标记某剂次为「已打」并保存</summary>
    public async Task<bool> MarkVaccineDoneAsync(VaccinePlanView plan)
    {
        var dto = VaccineForm.MarkDone(plan);
        if (dto is null) return false;
        try
        {
            RecordService.AddVaccine(dto);
            await VaccineForm.LoadAsync(); // 刷新时间轴状态
            Saved?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>疫苗专用：标记某剂次为「跳过」并保存</summary>
    public async Task<bool> MarkVaccineSkippedAsync(VaccinePlanView plan)
    {
        var dto = VaccineForm.MarkSkipped(plan);
        if (dto is null) return false;
        try
        {
            RecordService.AddVaccine(dto);
            await VaccineForm.LoadAsync();
            Saved?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>疫苗专用：添加自定义疫苗到时间轴</summary>
    public async Task<(bool Ok, string Error)> AddCustomVaccineAsync()
    {
        var (ok, error) = await VaccineForm.AddCustomVaccineAsync();
        if (!ok) ErrorMessage = error;
        else ErrorMessage = string.Empty;
        return (ok, error);
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;
        if (!ValidateActiveForm()) return;
        try
        {
            if (IsEditMode)
            {
                UpdateExisting();
            }
            else
            {
                AddNew();
            }
            IsVisible = false;
            Saved?.Invoke();
            Closed?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
        }
    }

    private void AddNew()
    {
        switch (ActiveType)
        {
            case RecordType.Feed: RecordService.AddFeed(FeedForm.BuildDto()); break;
            case RecordType.Diaper: RecordService.AddDiaper(DiaperForm.BuildDto()); break;
            case RecordType.Sleep: RecordService.AddSleep(SleepForm.BuildDto()); break;
            case RecordType.Temperature: RecordService.AddTemperature(TemperatureForm.BuildDto()); break;
            case RecordType.Growth: RecordService.AddGrowth(GrowthForm.BuildDto()); break;
            case RecordType.Supplement: RecordService.AddSupplement(SupplementForm.BuildDto()); break;
            case RecordType.Pump: RecordService.AddPump(PumpForm.BuildDto()); break;
            case RecordType.Complementary: RecordService.AddComplementary(ComplementaryForm.BuildDto()); break;
            case RecordType.Abnormal: RecordService.AddAbnormal(AbnormalForm.BuildDto()); break;
            case RecordType.Vaccine: RecordService.AddVaccine(VaccineForm.BuildDto()); break;
            case RecordType.Activity: RecordService.AddActivity(ActivityForm.BuildDto()); break;
        }
    }

    /// <summary>编辑模式：用表单数据更新现有记录。复用 RecordFormHostViewModel.FillForm 的反向操作。</summary>
    private void UpdateExisting()
    {
        var existing = RecordService.GetById(_editingId);
        if (existing is null)
        {
            ErrorMessage = "记录不存在";
            return;
        }

        switch (ActiveType)
        {
            case RecordType.Feed:
                var feedDto = FeedForm.BuildDto();
                existing.RecordSubType = feedDto.Type;
                existing.AmountMl = feedDto.Amount;
                existing.LeftDurationSec = (feedDto.LeftDuration ?? 0) * 60;
                existing.RightDurationSec = (feedDto.RightDuration ?? 0) * 60;
                existing.DurationSec = ((feedDto.LeftDuration ?? 0) + (feedDto.RightDuration ?? 0)) * 60;
                existing.RecordTime = ParseTime(feedDto.Time, _editingDate);
                break;
            case RecordType.Diaper:
                var diaDto = DiaperForm.BuildDto();
                existing.RecordSubType = diaDto.Type;
                existing.RecordTime = ParseTime(diaDto.Time, _editingDate);
                break;
            case RecordType.Sleep:
                var slpDto = SleepForm.BuildDto();
                existing.DurationSec = (slpDto.Duration ?? 0) * 60;
                existing.RecordTime = ParseTime(slpDto.StartTime, _editingDate);
                break;
            case RecordType.Temperature:
                var tmpDto = TemperatureForm.BuildDto();
                existing.TemperatureValue = tmpDto.Temperature;
                existing.AbnormalFlag = tmpDto.Temperature >= 37.3m;
                existing.RecordTime = ParseTime(tmpDto.Time, _editingDate);
                break;
            case RecordType.Growth:
                var grwDto = GrowthForm.BuildDto();
                existing.HeightCm = grwDto.Height;
                existing.WeightKg = grwDto.Weight;
                existing.RecordTime = ParseTime(grwDto.Time, _editingDate);
                break;
            case RecordType.Supplement:
                var supDto = SupplementForm.BuildDto();
                existing.RecordSubType = supDto.Type;
                existing.RecordTime = ParseTime(supDto.Time, _editingDate);
                break;
            case RecordType.Pump:
                var pmpDto = PumpForm.BuildDto();
                existing.LeftDurationSec = (pmpDto.LeftDuration ?? 0) * 60;
                existing.RightDurationSec = (pmpDto.RightDuration ?? 0) * 60;
                existing.AmountMl = pmpDto.TotalAmount;
                existing.RecordTime = ParseTime(pmpDto.Time, _editingDate);
                break;
            case RecordType.Complementary:
                var cmpDto = ComplementaryForm.BuildDto();
                existing.RecordSubType = cmpDto.Texture;
                existing.AbnormalFlag = cmpDto.Abnormal;
                existing.RecordTime = ParseTime(cmpDto.Time, _editingDate);
                break;
            case RecordType.Abnormal:
                var abnDto = AbnormalForm.BuildDto();
                existing.TemperatureValue = abnDto.Temperature;
                existing.AbnormalFlag = true;
                // 重新计算 RecordSubType（与 AddAbnormal 保持一致）
                if (abnDto.Temperature.HasValue && abnDto.Temperature.Value >= 38m)
                    existing.RecordSubType = "fever";
                else if (abnDto.Diarrhea.Count > 0)
                    existing.RecordSubType = "diarrhea";
                else if (abnDto.Vomit)
                    existing.RecordSubType = "vomit";
                else if (abnDto.Medicine)
                    existing.RecordSubType = "medicine";
                else
                    existing.RecordSubType = null;
                existing.PayloadJson = System.Text.Json.JsonSerializer.Serialize(abnDto);
                existing.RecordTime = ParseTime(abnDto.Time, _editingDate);
                break;
            case RecordType.Vaccine:
                var vacDto = VaccineForm.BuildDto();
                existing.RecordTime = ParseTime(vacDto.Time, _editingDate);
                break;
            case RecordType.Activity:
                var actDto = ActivityForm.BuildDto();
                existing.RecordSubType = actDto.Category;
                existing.DurationSec = (actDto.Duration ?? 0) * 60;
                existing.RecordTime = ParseTime(actDto.Time, _editingDate);
                break;
        }
        RecordService.Update(existing);
    }

    private static DateTime ParseTime(string timeStr, DateTime date)
    {
        if (TimeSpan.TryParse(timeStr, out var ts))
            return date.Date + ts;
        return date;
    }

    /// <summary>覆写基类钩子：抽屉关闭时统一触发 Closed 事件（保存和 X 关闭共用）。</summary>
    protected override void OnSheetClosed() => Closed?.Invoke();
}
